#pragma once

#include "sts2/game/types.h"

// Canonical enemy move + ritual-tick primitives, shared by the production
// engine (src/game/enemies.cc, src/game/powers.cc) and the AI transition
// simulator (src/ai/transition.cc) to prevent silent divergence.

namespace sts2::game::move_calc {

// Advance enemy intent. Currently kIncantation -> kDarkStrike; otherwise no-op.
[[nodiscard]] inline MoveId next_move(MoveId current) noexcept {
  if (current == MoveId::kIncantation) return MoveId::kDarkStrike;
  return current;
}

// Ritual tick. If just_applied: clear and grant 0 Strength (skip-first-turn).
// Else: grant ritual_amount Strength. Mutates just_applied to false.
inline int ritual_tick_strength_gain(bool& just_applied, int ritual_amount) noexcept {
  if (just_applied) {
    just_applied = false;
    return 0;
  }
  return ritual_amount;
}

}  // namespace sts2::game::move_calc
