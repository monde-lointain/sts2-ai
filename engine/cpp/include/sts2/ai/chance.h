#pragma once

#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"

namespace sts2::ai {

// A single outcome of a chance node: weighted child state.
// probability + child sums to 1.0 across all outcomes for a given chance node.
struct ChanceOutcome {
  double probability;
  CompactState child_state;
};

// Sole source of truth for chance-node enumeration (Q2-ADR-010
// §canonical-order). Walks the same 3-case draw/discard/reshuffle logic that
// pre-wave search.cc inlined at lines 175-211. Engineer lifts that code into
// this function.
//
// Precondition: state.get_phase() == Phase::kAtChanceDraw (chance node) AND
// !transition::is_terminal(state). I.e., this is called between
// resolve_end_turn_pre_draw and the actual card draw.
//
// Returns a vector of ChanceOutcome whose probabilities sum to 1.0.
// Order is deterministic — same input state → same output sequence (matters
// because solve_chance's weighted sum is order-sensitive at FP precision).
[[nodiscard]] std::vector<ChanceOutcome> enumerate_chance_outcomes(
    const CompactState& chance_state);

// Thin wrapper — returns true iff state is a player-decision (MAX) node.
// Used by C.2-α recommend() precondition assertion.
[[nodiscard]] inline bool is_max_node(const CompactState& s) noexcept {
  return s.get_phase() == Phase::kPlayerActing;
}

// Thin pass-through to transition::legal_actions. Provides a stable
// chance.h API point so C.1/C.2 consumers can include just chance.h.
// Engineer: if transition::legal_actions already has the right signature,
// just inline-forward; no need to duplicate logic.
[[nodiscard]] inline std::vector<transition::Action> enumerate_legal_actions(
    const CompactState& s) {
  return transition::legal_actions(s);
}

}  // namespace sts2::ai
