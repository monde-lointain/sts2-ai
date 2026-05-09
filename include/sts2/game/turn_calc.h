#pragma once

// Canonical turn-flow primitives, shared by the production engine
// (src/game/combat.cc) and the AI transition simulator (src/ai/transition.cc)
// to prevent silent divergence.

namespace sts2::game::turn_calc {

// Canonical player energy at the start of each turn.
inline constexpr int kPlayerStartingEnergy = 3;

// True iff accumulated block from the previous turn should be cleared.
// Block persists only on round 1 (no prior turn to clear from).
[[nodiscard]] inline bool round_resets_block(int round) noexcept {
  return round > 1;
}

// Starting energy for the player at the beginning of each turn.
[[nodiscard]] inline int starting_energy() noexcept {
  return kPlayerStartingEnergy;
}

// Cards drawn at the start of a turn. Round 1 grants a Ring of the Snake
// bonus (+2); all subsequent rounds draw the base amount.
[[nodiscard]] inline int hand_draw_size(int round) noexcept {
  constexpr int kBaseHandDraw = 5;
  constexpr int kRingOfTheSnakeBonus = 2;
  return kBaseHandDraw + (round == 1 ? kRingOfTheSnakeBonus : 0);
}

}  // namespace sts2::game::turn_calc
