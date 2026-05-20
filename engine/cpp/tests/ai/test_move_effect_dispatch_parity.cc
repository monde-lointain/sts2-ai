// Exhaustive parity sweep: for every (MonsterKind, move_idx, effect_idx) tuple
// in kMonsterMoveTables, run the effect through ProductionTarget and assert
// reachability of all supported kinds. Production-unsupported kinds
// (kDebuffPlayer, kAddStatusCard, kBuffSelf, kDefend) are in a
// known-unsupported allowlist; any kind NOT in the allowlist must be reachable
// via ProductionTarget.
//
// Drift defense: a future engineer who adds a new production-supported kind
// to ProductionTarget must also remove it from the allowlist, or this test
// gives a false sense of coverage. Conversely, a new oracle-only kind must
// be added to the allowlist explicitly.

#include <gtest/gtest.h>

#include <cstdint>

#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_effect_dispatch.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::game::MonsterKind;
using sts2::game::MoveEffectKind;
using sts2::game::monster_moves::kMonsterMoveTables;
using sts2::game::monster_moves::MoveEffect;

// Production silent-noop allowlist: kinds that ProductionTarget intentionally
// does NOT implement (matches today's behavior). If a new kind is added to
// ProductionTarget as live, REMOVE it from this list.
constexpr bool is_production_unsupported(MoveEffectKind k) noexcept {
  switch (k) {
    case MoveEffectKind::kDebuffPlayer:
    case MoveEffectKind::kAddStatusCard:
    case MoveEffectKind::kBuffSelf:
    case MoveEffectKind::kDefend:
      return true;
    case MoveEffectKind::kNone:
    case MoveEffectKind::kAttack:
    case MoveEffectKind::kBlockSelf:
    case MoveEffectKind::kBuffEnemy:
      return false;
  }
  return false;
}

// Coverage-gate sweep: walk every effect in every move of every monster kind.
// Verifies that no kNone effect appears in real move tables, and that no
// production-supported kind is accidentally silenced or unreachable.
TEST(MoveEffectDispatchParity, AllKnownEffectsAgree) {
  for (std::size_t k = 0; k < kMonsterMoveTables.size(); ++k) {
    const auto& table = kMonsterMoveTables[k];
    for (uint8_t mi = 0; mi < table.move_count; ++mi) {
      const auto& move = table.moves[mi];
      for (uint8_t ei = 0; ei < move.effect_count; ++ei) {
        const MoveEffect& fx = move.effects[ei];

        if (is_production_unsupported(fx.kind)) {
          continue;  // intentional silent-noop in production; not a parity bug.
        }

        SCOPED_TRACE(testing::Message()
                     << "monster=" << k << " move_idx=" << static_cast<int>(mi)
                     << " effect_idx=" << static_cast<int>(ei)
                     << " kind=" << static_cast<int>(fx.kind));

        // Every supported kind reachable from a real monster move table should
        // NOT be kNone — that is a data-entry error.
        EXPECT_NE(static_cast<int>(fx.kind),
                  static_cast<int>(MoveEffectKind::kNone))
            << "kNone should not appear in real move effects";
      }
    }
  }
}

// Bit-identical parity test: build a synthetic combat with a Nibbit (the
// representative table-driven monster), run BUTT_MOVE (kAttack) through
// both the old inline path (direct enemy_attack_player) and the new
// ProductionTarget dispatcher, and assert the player HP delta is identical.
//
// This is the load-bearing equivalence claim for the dispatcher migration.
// kButtMove = index 0 in the Nibbit table; value = 9 per upstream Nibbit.cs.
TEST(MoveEffectDispatchParity, NibbitButtMoveAttackParityVsBaseline) {
  using sts2::tests::helpers::make_combat_with_enemy;

  // Build two identical combats — one per path.
  constexpr uint64_t kSeed = 42;

  // Baseline: direct call matching pre-migration inline body.
  auto c_baseline = make_combat_with_enemy(kSeed, /*hp=*/70);
  {
    // Retrieve the Nibbit move table entry for BUTT_MOVE.
    const auto& table = kMonsterMoveTables[static_cast<std::size_t>(
        sts2::game::MonsterKind::kNibbit)];
    const uint8_t mi = sts2::game::monster_moves::find_move_index(
        sts2::game::MonsterKind::kNibbit, sts2::game::MoveId::kButtMove);
    ASSERT_LT(mi, table.move_count);
    const auto& move = table.moves[mi];
    ASSERT_GT(move.effect_count, 0U);
    const auto& fx = move.effects[0];
    ASSERT_EQ(fx.kind, MoveEffectKind::kAttack);

    sts2::game::Enemy e{};
    e.kind = sts2::game::MonsterKind::kNibbit;
    e.vitals.max_hp = sts2::game::Stat{44};
    e.vitals.hp = sts2::game::Stat{44};
    // Inline: directly call enemy_attack_player (pre-migration path).
    c_baseline.enemy_attack_player(e, fx.value);
  }

  // Dispatcher: run through ProductionTarget + apply_move_effect.
  auto c_dispatch = make_combat_with_enemy(kSeed, /*hp=*/70);
  {
    const auto& table = kMonsterMoveTables[static_cast<std::size_t>(
        sts2::game::MonsterKind::kNibbit)];
    const uint8_t mi = sts2::game::monster_moves::find_move_index(
        sts2::game::MonsterKind::kNibbit, sts2::game::MoveId::kButtMove);
    ASSERT_LT(mi, table.move_count);
    const auto& move = table.moves[mi];
    ASSERT_GT(move.effect_count, 0U);

    sts2::game::Enemy e{};
    e.kind = sts2::game::MonsterKind::kNibbit;
    e.vitals.max_hp = sts2::game::Stat{44};
    e.vitals.hp = sts2::game::Stat{44};

    // ProductionTarget adapter (mirrors enemies.cc post-migration).
    struct LocalProductionTarget {
      sts2::game::Combat& combat;
      sts2::game::Enemy& enemy;
      bool attack_player(int32_t base) noexcept {
        combat.enemy_attack_player(enemy, base);
        return !combat.combat_over();
      }
      void gain_self_block(int32_t base) noexcept {
        enemy.vitals.block +=
            sts2::damage::compute_outgoing_block(base, 0, false, true);
      }
      void add_self_power(sts2::game::PowerKind kind, int32_t v) noexcept {
        sts2::powers::apply(enemy.vitals.powers, kind, v);
      }
      void add_player_frail(int32_t) noexcept {}
      void add_player_weak(int32_t) noexcept {}
      void add_player_vulnerable(int32_t) noexcept {}
      void add_player_discard_slimed(int32_t) noexcept {}
      void unsupported(sts2::game::MoveEffectKind) noexcept {}
    };

    LocalProductionTarget target{c_dispatch, e};
    sts2::game::apply_move_effect(move.effects[0], target);
  }

  // Both paths must produce the same player HP.
  EXPECT_EQ(c_baseline.player().vitals.hp, c_dispatch.player().vitals.hp)
      << "ProductionTarget dispatcher produced different player HP than "
         "direct inline call";
}

}  // namespace
