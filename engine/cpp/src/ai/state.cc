#include "sts2/ai/state.h"

#include <cassert>
#include <cstddef>
#include <numeric>
#include <span>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemy.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/vitals.h"

namespace sts2::ai {

namespace {

void tally(CardCounts& counts, std::span<const sts2::game::Card> pile) {
  for (const auto& c : pile) {
    assert(c.id != sts2::game::CardId::kNone && "kNone in pile");
    ++counts[c.id];
  }
}

EnemyState build_enemy_state(const sts2::game::Enemy& e) {
  const sts2::game::Power* ritual =
      sts2::powers::find(e.vitals.powers, sts2::game::PowerKind::kRitual);
  // Look up move_index from the kind's table; falls back to 0 for cultist
  // moves (table lookup returns 0xFF for kIncantation/kDarkStrike on a
  // non-cultist table, but cultists DO have those in their tables → index
  // 0 or 1 resolves). For slimes, this resolves the table-driven move_index
  // needed by chance.cc + transition.cc to dispatch enemy semantics.
  const uint8_t move_idx =
      sts2::game::monster_moves::find_move_index(e.kind, e.current_move);
  // Build via builder; strength/weak/just_applied_ritual route through powers_.
  // dark_strike_base and ritual_amount remain scalar fields.
  // Wave-23-prep: kind + move_index must be set so do_enemy_act + chance.cc
  // route slime states through their table-driven semantics instead of
  // falling through to the cultist act_on_intent stub (which is a silent
  // no-op for slime MoveIds → infinite recursion).
  return EnemyStateBuilder{}
      .alive(e.vitals.hp > sts2::game::Stat{0})
      .hp(e.vitals.hp)
      .block(e.vitals.block)
      .strength(sts2::game::Stat{sts2::powers::amount(
          e.vitals.powers, sts2::game::PowerKind::kStrength)})
      .weak(sts2::game::Stat{
          sts2::powers::amount(e.vitals.powers, sts2::game::PowerKind::kWeak)})
      .dark_strike_base(e.dark_strike_base)
      .ritual_amount(e.ritual_amount)
      .just_applied_ritual(ritual != nullptr && ritual->just_applied)
      .performed_first_move(e.performed_first_move)
      .current_move(e.current_move)
      .kind(e.kind)
      .move_index(move_idx == 0xFF ? 0U : move_idx)
      .build();
}

}  // namespace

CardCounts& CardCounts::operator+=(const CardCounts& o) noexcept {
  for (std::size_t i = 0; i < counts.size(); ++i) {
    counts[i] = static_cast<uint8_t>(counts[i] + o.counts[i]);
  }
  return *this;
}

CardCounts& CardCounts::operator-=(const CardCounts& o) noexcept {
  for (std::size_t i = 0; i < counts.size(); ++i) {
    assert(counts[i] >= o.counts[i] && "CardCounts::operator-= underflow");
    counts[i] = static_cast<uint8_t>(counts[i] - o.counts[i]);
  }
  return *this;
}

bool CardCounts::covers(const CardCounts& subset) const noexcept {
  for (std::size_t i = 0; i < counts.size(); ++i) {
    if (counts[i] < subset.counts[i]) {
      return false;
    }
  }
  return true;
}

int CardCounts::total() const noexcept {
  return std::accumulate(counts.begin(), counts.end(), 0);
}

CompactState from_combat(const sts2::game::Combat& combat) {
  const auto& p = combat.player();
  assert(sts2::powers::amount(p.vitals.powers,
                              sts2::game::PowerKind::kStrength) == 0);
  assert(sts2::powers::amount(p.vitals.powers, sts2::game::PowerKind::kWeak) ==
         0);
  assert(sts2::powers::find(p.vitals.powers, sts2::game::PowerKind::kRitual) ==
         nullptr);

  CompactStateBuilder builder;
  builder.player_hp(p.vitals.hp)
      .player_block(p.vitals.block)
      .player_strength(sts2::game::Stat{0})
      .player_weak(sts2::game::Stat{0})
      .energy(sts2::game::Stat{p.energy});
  assert(combat.round() >= 0);
  builder.round(static_cast<uint16_t>(combat.round()))
      .phase(Phase::kPlayerActing);

  const auto& es = combat.enemies();
  assert(es.size() <= kMaxEnemies);
  for (std::size_t i = 0; i < es.size(); ++i) {
    builder.enemy(i, build_enemy_state(es[i]));
  }
  // enemy_count_ is set automatically by builder.enemy() calls.

  CardCounts hand;
  CardCounts draw;
  CardCounts discard;
  tally(hand, p.hand.cards());
  tally(draw, p.deck.draw_pile());
  tally(discard, p.deck.discard_pile());
  builder.hand(hand).draw(draw).discard(discard);

  return builder.build();
}

}  // namespace sts2::ai
