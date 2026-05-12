#pragma once

#include <string_view>
#include <utility>

#include "sts2/game/types.h"

// Canonical enemy move + ritual-tick primitives, shared by the production
// engine (src/game/enemies.cc, src/game/powers.cc) and the AI transition
// simulator (src/ai/transition.cc) to prevent silent divergence.

namespace sts2::game::move_calc {

[[nodiscard]] constexpr std::string_view move_wire_id(MoveId id) noexcept {
  switch (id) {
    case MoveId::kIncantation:
      return "INCANTATION_MOVE";
    case MoveId::kDarkStrike:
      return "DARK_STRIKE_MOVE";
  }
  return "";
}

[[nodiscard]] constexpr bool try_move_id_from_wire_id(std::string_view wire_id,
                                                      MoveId& out) noexcept {
  if (wire_id == "INCANTATION_MOVE") {
    out = MoveId::kIncantation;
    return true;
  }
  if (wire_id == "DARK_STRIKE_MOVE") {
    out = MoveId::kDarkStrike;
    return true;
  }
  return false;
}

[[nodiscard]] constexpr MoveId move_id_from_wire_id(
    std::string_view wire_id) noexcept {
  MoveId out = MoveId::kIncantation;
  (void)try_move_id_from_wire_id(wire_id, out);
  return out;
}

// Advance enemy intent through its move sequence. kIncantation -> kDarkStrike;
// other moves are stable.
[[nodiscard]] inline MoveId next_move(MoveId current) noexcept {
  if (current == MoveId::kIncantation) {
    return MoveId::kDarkStrike;
  }
  return current;
}

// Ritual tick decision. Mutates just_applied to false. Returns true iff this
// tick should grant Strength (the just-applied turn skips the gain).
[[nodiscard]] inline bool ritual_should_grant_strength(
    bool& just_applied) noexcept {
  if (just_applied) {
    just_applied = false;
    return false;
  }
  return true;
}

// Advance the enemy's intent state for the next turn. On the first call
// (performed_first_move == false) marks the intent as performed without
// changing it; on subsequent calls advances current_move via next_move().
inline void advance_intent(bool& performed_first_move,
                           MoveId& current_move) noexcept {
  if (!performed_first_move) {
    performed_first_move = true;
    return;
  }
  current_move = next_move(current_move);
}

// Dispatch the enemy's intent to per-effect callables. Sharing this switch
// is the load-bearing T18c guarantee: adding a new MoveId is a one-place
// header change. Each layer supplies lambdas that perform its own effects.
template <typename OnRitual, typename OnDarkStrike>
void act_on_intent(
    MoveId move, OnRitual&& on_ritual,
    OnDarkStrike&& on_dark_strike) noexcept(noexcept(on_ritual()) &&
                                            noexcept(on_dark_strike())) {
  // NOTE: adding a MoveId here is not a compile-time call-site signal —
  // grep act_on_intent users to verify they handle the new case.
  switch (move) {
    case MoveId::kIncantation:
      std::forward<OnRitual>(on_ritual)();
      break;
    case MoveId::kDarkStrike:
      std::forward<OnDarkStrike>(on_dark_strike)();
      break;
  }
}

}  // namespace sts2::game::move_calc
