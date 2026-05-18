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

  // Wave-23-prep: kind is required to dispatch enemy semantics in
  // CompactState (do_enemy_act, do_roll_next_move,
  // has_pending_random_move_roll). Defaults to kCultistCalcified — preserves
  // the cultist Zobrist byte-identity pin (slot-1 Damp cultist hashes as
  // Calcified under the legacy default; do not "fix" cultist factories to set
  // kCultistDamp without re-pinning). Slime factories MUST set this for the
  // do_enemy_act_slime dispatch to fire through from_combat; the SmallSlimes
  // synthetic test path would otherwise recurse infinitely (Q2-ADR-013
  // amendment).
  MonsterKind kind = MonsterKind::kCultistCalcified;
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
void for_each_alive_enemy(C& enemies, F&& fn) {
  for (auto& e : enemies) {
    if (is_alive(e)) {
      std::forward<F>(fn)(e);
    }
  }
}

}  // namespace sts2::game
