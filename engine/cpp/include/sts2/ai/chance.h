#pragma once

#include <cstdint>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/monster_moves.h"

namespace sts2::ai {

// A single outcome of a chance node: weighted child state.
// probability + child sums to 1.0 across all outcomes for a given chance node.
struct ChanceOutcome {
  double probability;
  CompactState child_state;
};

// Sole source of truth for chance-node enumeration (Q2-ADR-010
// §canonical-order). Dispatches on state.get_phase():
//
//   kAtChanceDraw    → card-draw outcomes (existing wave-19 logic)
//   kAtEnemyMoveRng  → enemy-move RNG outcomes × card-draw outcomes
//                      (wave-22.α; slime POKEY / GOOP RandomBranch)
//
// Precondition: state.get_phase() ∈ {kAtChanceDraw, kAtEnemyMoveRng} AND
// !transition::is_terminal(state). I.e., this is called between
// resolve_end_turn_pre_draw and the actual card draw.
//
// Returns a vector of ChanceOutcome whose probabilities sum to 1.0; every
// child has phase=kPlayerActing (caller can drive solve_player without
// another intermediate phase). Order is deterministic — same input state →
// same output sequence (matters because solve_chance's weighted sum is
// order-sensitive at FP precision).
[[nodiscard]] std::vector<ChanceOutcome> enumerate_chance_outcomes(
    const CompactState& chance_state);

// ---------------------------------------------------------------------------
// Branch-outcome enumeration helper (wave-22.α).
//
// Resolves a single MonsterMove's RandomBranch follow-up under the
// CannotRepeat re-normalization rule (plan-the-q2-oracle-glittery-pony §22.β):
//
//   1. Filter branches: branch i is ELIGIBLE iff NOT (branch_cannot_repeat[i]
//      AND branch_indices[i] == current_move_idx).
//   2. Sum eligible branch_weights as normalizer N.
//   3. For each eligible branch i: probability = branch_weights[i] / N.
//
// The kStrict follow-up has no branches (branch_count=0) — caller must NOT
// invoke this helper on kStrict moves (assertion fires).
//
// Exposed in the header so test_chance.cc can exercise it directly on
// synthetic MonsterMove instances (slime move tables are zero-filled at
// C.2-α merge time; C.3-β populates real data downstream).
struct BranchOutcome {
  double probability;    // > 0, normalized: sum of probabilities == 1.0
  uint8_t new_move_idx;  // branch_indices[i] (target move index in table)
};

[[nodiscard]] std::vector<BranchOutcome> enumerate_branch_outcomes(
    const sts2::game::monster_moves::MonsterMove& move,
    uint8_t current_move_idx);

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
