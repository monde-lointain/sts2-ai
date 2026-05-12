#include "sts2/game/enemies.h"

#include "sts2/game/combat.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/powers.h"
#include "sts2/game/rng.h"

namespace sts2::enemies {

namespace {

sts2::game::Enemy make_cultist(const CultistArchetype& archetype,
                               sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = archetype.internal_name;
  int hp = rng.uniform_int(archetype.hp_min, archetype.hp_max);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.dark_strike_base = sts2::game::Stat{archetype.dark_strike_base};
  e.ritual_amount = sts2::game::Stat{archetype.ritual_amount};
  return e;
}

}  // namespace

sts2::game::Enemy make_calcified_cultist(sts2::game::Rng& rng) {
  return make_cultist(kCultistArchetypes[0], rng);
}

sts2::game::Enemy make_damp_cultist(sts2::game::Rng& rng) {
  return make_cultist(kCultistArchetypes[1], rng);
}

void roll_next_move(sts2::game::Enemy& e) {
  sts2::game::move_calc::advance_intent(e.performed_first_move, e.current_move);
}

void act(sts2::game::Enemy& e, sts2::game::Combat& combat) {
  sts2::game::move_calc::act_on_intent(
      e.current_move,
      [&]() {
        sts2::powers::apply(e.vitals.powers, sts2::game::PowerKind::kRitual,
                            e.ritual_amount.value());
      },
      [&]() { combat.enemy_attack_player(e, e.dark_strike_base.value()); });
}

}  // namespace sts2::enemies
