#include <gtest/gtest.h>

#include <string>

#include "sts2/ai/power_array.h"
#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/ai/test_helpers.h"
#include "tests/game/test_helpers.h"

namespace {

using snapshot::snapshot;
using sts2::ai::CompactState;
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyState;
using sts2::ai::EnemyStateBuilder;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::tests::ai::make_counts;
using sts2::tests::helpers::make_starter_combat;

TEST(AiState, FromCombat_FreshStarter_SnapshotMatches) {
  sts2::game::Combat combat = make_starter_combat(0xC0FFEEULL);
  const CompactState s = from_combat(combat);

  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_player_strength(), Stat{0});
  EXPECT_EQ(s.get_player_weak(), Stat{0});
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);

  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kIncantation);
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kIncantation);
  EXPECT_TRUE(s.get_enemy(0).get_performed_first_move());
  EXPECT_TRUE(s.get_enemy(1).get_performed_first_move());
  // Kinds set by from_combat so cultist helpers index correct table entries.
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kCultistCalcified);
  EXPECT_EQ(s.get_enemy(1).get_kind(), MonsterKind::kCultistDamp);

  // Ring of the Snake: round-1 hand is 7.
  EXPECT_EQ(s.get_hand().total(), 7);
  // Silent starter deck has 12 cards total.
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);
}

TEST(AiState, CompactStateBuilder_MatchesDirectSetup) {
  const auto hand = make_counts(1, 2, 0, 1);
  const auto draw = make_counts(3, 1, 1, 0);
  const auto discard = make_counts(0, 1, 0, 2);

  EnemyState enemy0 = EnemyStateBuilder{}
                          .kind(MonsterKind::kCultistCalcified)
                          .hp(Stat{12})
                          .block(Stat{5})
                          .strength(Stat{3})
                          .weak(Stat{1})
                          .performed_first_move(true)
                          .current_move(MoveId::kDarkStrike)
                          .alive(true)
                          .build();
  sts2::ai::powers::set_just_applied_ritual(enemy0.powers_mut(), true);
  const CompactState expected = CompactStateBuilder{}
                                    .player_hp(Stat{41})
                                    .player_block(Stat{7})
                                    .player_strength(Stat{2})
                                    .player_weak(Stat{1})
                                    .energy(Stat{3})
                                    .round(4)
                                    .phase(Phase::kAtChanceDraw)
                                    .enemy(0, enemy0)
                                    .hand(hand)
                                    .draw(draw)
                                    .discard(discard)
                                    .build();

  const CompactState built = CompactStateBuilder{expected}.build();

  EXPECT_EQ(built, expected);
  EXPECT_EQ(built.get_player_hp(), Stat{41});
  EXPECT_EQ(built.get_player_block(), Stat{7});
  EXPECT_EQ(built.get_player_strength(), Stat{2});
  EXPECT_EQ(built.get_player_weak(), Stat{1});
  EXPECT_EQ(built.get_energy(), Stat{3});
  EXPECT_EQ(built.get_round(), 4);
  EXPECT_EQ(built.get_phase(), Phase::kAtChanceDraw);
  EXPECT_EQ(built.get_hand(), hand);
  EXPECT_EQ(built.get_draw(), draw);
  EXPECT_EQ(built.get_discard(), discard);

  const auto& built_enemy = built.get_enemies()[0];
  EXPECT_EQ(built_enemy.get_kind(), MonsterKind::kCultistCalcified);
  EXPECT_EQ(built_enemy.get_hp(), Stat{12});
  EXPECT_EQ(built_enemy.get_block(), Stat{5});
  EXPECT_EQ(built_enemy.get_strength(), Stat{3});
  EXPECT_EQ(built_enemy.get_weak(), Stat{1});
  EXPECT_TRUE(sts2::ai::powers::just_applied_ritual(built_enemy.powers()));
  EXPECT_TRUE(built_enemy.get_performed_first_move());
  EXPECT_EQ(built_enemy.get_current_move(), MoveId::kDarkStrike);
  EXPECT_TRUE(built_enemy.get_alive());
}

TEST(EnemyState, DefaultKindIsCalcifiedCultist) {
  // Load-bearing post-wave-35/B.2-β: cultist transition.cc helpers
  // (cultist_dark_strike_base / cultist_ritual_amount) index
  // kMonsterMoveTables[kind] for dsb/ritual. Any change to this default
  // silently changes cultist semantics for tests that don't call .kind()
  // explicitly. Surface drift loudly. See ADR-031.
  EXPECT_EQ(EnemyStateBuilder{}.build().get_kind(),
            MonsterKind::kCultistCalcified);
}

// ---------------------------------------------------------------------------
// EnemyStateBytes_RitualJustApplied_PinnedHex (wave-36/B.1-β permanent guard)
//
// Verifies that the byte-level representation of an EnemyState with
// just_applied_ritual=true is UNCHANGED after the Ritual/CurlUp semantics
// extraction refactor. Uses the snapshot::snapshot() helper (test_helpers.h)
// which serializes public fields field-by-field (avoids padding non-portability
// of raw memcpy on EnemyState).
//
// kGoldenHexA: CalcifiedCultist, hp=48, just_applied_ritual=true (recorded at
//   wave-36/B.1-β baseline on main SHA 0f61c01 using the old typed API).
// kGoldenHexB: DampCultist, hp=50, just_applied_ritual=true (same baseline).
//
// If either assertion fails, the refactor has altered PowerArray byte layout.
// DO NOT update these constants — that would mask a regression. Halt and
// bisect.
// ---------------------------------------------------------------------------
TEST(EnemyStateBytes, RitualJustApplied_PinnedHex) {
  static constexpr std::string_view kGoldenHexA =
      "30000000000000000000000101010000000002010000";
  static constexpr std::string_view kGoldenHexB =
      "32000000000000000100000100010000000002010000";

  // Fixture A: CalcifiedCultist, hp=48, alive, pfm=true, Incantation,
  //   just_applied_ritual=true (set via new free helper post-refactor).
  EnemyState enemy_a = EnemyStateBuilder{}
                           .kind(MonsterKind::kCultistCalcified)
                           .hp(Stat{48})
                           .block(Stat{0})
                           .current_move(MoveId::kIncantation)
                           .alive(true)
                           .performed_first_move(true)
                           .build();
  sts2::ai::powers::set_just_applied_ritual(enemy_a.powers_mut(), true);
  EXPECT_EQ(snapshot(enemy_a), kGoldenHexA)
      << "CalcifiedCultist just_applied_ritual byte sequence drifted; "
         "refactor has a behavioral regression. DO NOT update kGoldenHexA.";

  // Fixture B: DampCultist, hp=50, alive, pfm=false, Incantation,
  //   just_applied_ritual=true (matches cultists_projection wire path).
  EnemyState enemy_b = EnemyStateBuilder{}
                           .kind(MonsterKind::kCultistDamp)
                           .hp(Stat{50})
                           .block(Stat{0})
                           .current_move(MoveId::kIncantation)
                           .alive(true)
                           .performed_first_move(false)
                           .build();
  sts2::ai::powers::set_just_applied_ritual(enemy_b.powers_mut(), true);
  EXPECT_EQ(snapshot(enemy_b), kGoldenHexB)
      << "DampCultist just_applied_ritual byte sequence drifted; "
         "refactor has a behavioral regression. DO NOT update kGoldenHexB.";
}

}  // namespace
