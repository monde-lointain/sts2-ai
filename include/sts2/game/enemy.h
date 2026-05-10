#pragma once

#include <string>
#include <vector>

#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Enemy {
  std::string name;
  Vitals vitals;

  MoveId current_move = MoveId::kIncantation;
  bool performed_first_move = false;

  int dark_strike_base = 0;
  int ritual_amount = 0;
};

[[nodiscard]] inline bool is_alive(const Enemy& e) noexcept {
  return e.vitals.hp > Stat{0};
}

[[nodiscard]] inline bool is_alive(const std::vector<Enemy>& enemies,
                                   EnemySlot slot) noexcept {
  return slot.in_range(enemies) && is_alive(slot.at(enemies));
}

}  // namespace sts2::game
