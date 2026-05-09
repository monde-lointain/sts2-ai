#include "sts2/game/enemies.h"

#include "sts2/game/combat.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/rng.h"

namespace sts2::enemies {

sts2::game::Enemy make_calcified_cultist(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Calcified Cultist";
  int hp = rng.uniform_int(38, 41);
  e.vitals.max_hp = hp;
  e.vitals.hp = hp;
  e.dark_strike_base = 9;
  e.ritual_amount = 2;
  return e;
}

sts2::game::Enemy make_damp_cultist(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Damp Cultist";
  int hp = rng.uniform_int(51, 53);
  e.vitals.max_hp = hp;
  e.vitals.hp = hp;
  e.dark_strike_base = 1;
  e.ritual_amount = 5;
  return e;
}

void roll_next_move(sts2::game::Enemy& e) {
  sts2::game::move_calc::advance_intent(e.performed_first_move, e.current_move);
}

void act(sts2::game::Enemy& e, sts2::game::Combat& combat) {
  sts2::game::move_calc::act_on_intent(
      e.current_move,
      [&]() {
        sts2::game::Combat::apply_power_to_enemy_self(
            e, sts2::game::PowerKind::kRitual, e.ritual_amount);
      },
      [&]() {
        combat.enemy_attack_player(e, e.dark_strike_base);
      });
}

}  // namespace sts2::enemies
