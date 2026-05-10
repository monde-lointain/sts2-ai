#pragma once

#include <string>
#include <utility>
#include <vector>

#include "sts2/game/index_types.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Enemy {
  std::string name;
  Vitals vitals;

  MoveId current_move = MoveId::kIncantation;
  bool performed_first_move = false;

  Stat dark_strike_base;
  Stat ritual_amount;
};

[[nodiscard]] inline bool is_alive(const Enemy& e) noexcept {
  return e.vitals.hp > Stat{0};
}

[[nodiscard]] inline bool is_alive(const std::vector<Enemy>& enemies,
                                   EnemySlot slot) noexcept {
  return slot.in_range(enemies) && is_alive(slot.at(enemies));
}

// Calls fn(e) for each alive enemy in `enemies`, in slot order.
template <typename C, typename F>
void for_each_alive_enemy(C&& enemies, F&& fn) {
  for (auto& e : enemies) {
    if (is_alive(e)) std::forward<F>(fn)(e);
  }
}

}  // namespace sts2::game
