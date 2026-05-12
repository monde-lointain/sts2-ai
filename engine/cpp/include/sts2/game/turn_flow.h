#pragma once

#include <cstddef>

#include "sts2/game/turn_calc.h"

namespace sts2::game::turn_flow {

// Ops contract for resolve_end_turn_pre_draw:
//   void end_player_turn();
//   std::size_t enemy_count() const;
//   bool enemy_alive(std::size_t slot) const;
//   void reset_enemy_block(std::size_t slot);
//   void enemy_act(std::size_t slot);
//   bool terminal() const;
//   void tick_enemy_powers(std::size_t slot);
//   void increment_round();
//   int round() const;
//   void roll_enemy_next_move(std::size_t slot);
//   void reset_player_block();
//   void refill_player_energy(int amount);
//
// Resolves deterministic end-turn work up to, but not including, card draw.
// Enemy slots are visited in ascending slot order. If terminal() becomes true
// after an enemy acts, later enemy actions and all post-enemy steps are skipped.
template <typename Ops>
void resolve_end_turn_pre_draw(Ops& ops) {
  ops.end_player_turn();

  for (std::size_t slot = 0; slot < ops.enemy_count(); ++slot) {
    if (ops.enemy_alive(slot)) {
      ops.reset_enemy_block(slot);
    }
  }

  for (std::size_t slot = 0; slot < ops.enemy_count(); ++slot) {
    if (!ops.enemy_alive(slot)) {
      continue;
    }
    ops.enemy_act(slot);
    if (ops.terminal()) {
      return;
    }
  }

  for (std::size_t slot = 0; slot < ops.enemy_count(); ++slot) {
    if (ops.enemy_alive(slot)) {
      ops.tick_enemy_powers(slot);
    }
  }

  ops.increment_round();

  for (std::size_t slot = 0; slot < ops.enemy_count(); ++slot) {
    if (ops.enemy_alive(slot)) {
      ops.roll_enemy_next_move(slot);
    }
  }

  if (turn_calc::round_resets_block(ops.round())) {
    ops.reset_player_block();
  }
  ops.refill_player_energy(turn_calc::starting_energy());
}

}  // namespace sts2::game::turn_flow
