#include "sts2/ai/state.h"

#include <cassert>
#include <cstddef>
#include <vector>

#include "sts2/ai/card_metadata.h"
#include "sts2/game/card.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemy.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/vitals.h"

namespace sts2::ai {

namespace {

void tally(CardCounts& counts, const std::vector<sts2::game::Card>& pile) {
  for (const auto& c : pile) {
    assert(c.id != sts2::game::CardId::kNone && "kNone in pile");
    ++(counts.*card_metadata_for(c.id).count_field);
  }
}

EnemyState build_enemy_state(const sts2::game::Enemy& e) {
  EnemyState s;
  s.alive = e.vitals.hp > 0;
  s.hp = static_cast<uint8_t>(e.vitals.hp);
  s.block = static_cast<uint8_t>(e.vitals.block);
  s.strength = static_cast<uint8_t>(
      sts2::powers::amount(e.vitals.powers, sts2::game::PowerKind::kStrength));
  s.weak = static_cast<uint8_t>(
      sts2::powers::amount(e.vitals.powers, sts2::game::PowerKind::kWeak));
  s.dark_strike_base = static_cast<uint8_t>(e.dark_strike_base);
  s.ritual_amount = static_cast<uint8_t>(e.ritual_amount);
  const sts2::game::Power* ritual =
      sts2::powers::find(e.vitals.powers, sts2::game::PowerKind::kRitual);
  s.just_applied_ritual = ritual != nullptr && ritual->just_applied;
  s.performed_first_move = e.performed_first_move;
  s.current_move = e.current_move;
  return s;
}

}  // namespace

int CardCounts::total() const noexcept {
  return strike + defend + neutralize + survivor;
}

CompactState from_combat(const sts2::game::Combat& combat) {
  const auto& p = combat.player();
  assert(sts2::powers::amount(p.vitals.powers,
                              sts2::game::PowerKind::kStrength) == 0);
  assert(sts2::powers::amount(p.vitals.powers, sts2::game::PowerKind::kWeak) ==
         0);
  assert(sts2::powers::find(p.vitals.powers, sts2::game::PowerKind::kRitual) ==
         nullptr);

  CompactState s;
  s.player_hp = static_cast<uint8_t>(p.vitals.hp);
  s.player_block = static_cast<uint8_t>(p.vitals.block);
  s.player_strength = 0;
  s.player_weak = 0;
  s.energy = static_cast<uint8_t>(p.energy);
  assert(combat.round() >= 0);
  s.round = static_cast<uint16_t>(combat.round());
  s.phase = Phase::kPlayerActing;

  const auto& es = combat.enemies();
  assert(es.size() <= 2);
  for (std::size_t i = 0; i < es.size(); ++i) {
    s.enemies[i] = build_enemy_state(es[i]);
  }

  tally(s.hand, p.hand);
  tally(s.draw, p.draw_pile);
  tally(s.discard, p.discard_pile);

  return s;
}

}  // namespace sts2::ai
