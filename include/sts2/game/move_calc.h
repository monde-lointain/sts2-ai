#pragma once

#include "sts2/game/types.h"

// Canonical enemy move + ritual-tick primitives, shared by the production
// engine (src/game/enemies.cc, src/game/powers.cc) and the AI transition
// simulator (src/ai/transition.cc) to prevent silent divergence.

namespace sts2::game::move_calc {

// Advance enemy intent through its move sequence. kIncantation -> kDarkStrike;
// other moves are stable.
[[nodiscard]] inline MoveId next_move(MoveId current) noexcept {
  if (current == MoveId::kIncantation) return MoveId::kDarkStrike;
  return current;
}

// Ritual tick decision. Mutates just_applied to false. Returns true iff this
// tick should grant Strength (the just-applied turn skips the gain).
[[nodiscard]] inline bool ritual_should_grant_strength(bool& just_applied) noexcept {
  if (just_applied) {
    just_applied = false;
    return false;
  }
  return true;
}

}  // namespace sts2::game::move_calc
