#include "sts2/ai/chance.h"

#include <cassert>
#include <cstddef>
#include <cstdint>
#include <vector>

#include "sts2/ai/probability.h"
#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
#include "sts2/ai/transition.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

namespace sts2::ai {

namespace {

using sts2::game::monster_moves::FollowUpRule;
using sts2::game::monster_moves::kMonsterMoveTables;
using sts2::game::monster_moves::MonsterMove;

// Pre-wave-22 card-draw enumeration (existing wave-19 logic; outcomes have
// phase=kPlayerActing).
[[nodiscard]] std::vector<ChanceOutcome> enumerate_draw_outcomes(
    const CompactState& state) {
  assert(state.get_phase() == Phase::kAtChanceDraw &&
         "enumerate_draw_outcomes called on non-draw chance state");
  assert(!transition::is_terminal(state) &&
         "enumerate_draw_outcomes called on terminal chance state");

  const int k = transition::draw_count(state);
  const CardCounts& draw = state.get_draw();
  const CardCounts& discard = state.get_discard();
  const int draw_total = draw.total();
  const int discard_total = discard.total();

  std::vector<ChanceOutcome> outcomes;

  if (draw_total >= k) {
    const auto draws = probability::enumerate_draws(draw, k);
    outcomes.reserve(draws.size());
    for (const auto& o : draws) {
      outcomes.push_back(ChanceOutcome{
          .probability = o.weight,
          .child_state = transition::apply_draw(state, o.hand),
      });
    }
  } else if (draw_total + discard_total <= k) {
    // Engine semantics (Hand::draw_from): when both piles run dry, draw stops
    // early. Player deterministically gets every remaining card.
    const CardCounts everything = draw + discard;
    outcomes.push_back(ChanceOutcome{
        .probability = 1.0,
        .child_state = transition::apply_draw(state, everything),
    });
  } else {
    // Draw pile alone can't satisfy k but draw+discard can: take all of draw
    // deterministically and mix in remainder from discard via reshuffle.
    const CardCounts forced_from_draw = draw;
    const int remaining = k - draw_total;
    const auto draws = probability::enumerate_draws(discard, remaining);
    outcomes.reserve(draws.size());
    for (const auto& o : draws) {
      const CardCounts full_drawn = forced_from_draw + o.hand;
      outcomes.push_back(ChanceOutcome{
          .probability = o.weight,
          .child_state = transition::apply_draw(state, full_drawn),
      });
    }
  }

  return outcomes;
}

// Apply a single enemy-move-RNG outcome to a state: set the indicated
// enemy's move_index + current_move, advance phase to kAtChanceDraw, leave
// all other state unchanged. Mirrors the post-roll state shape produced by
// move_calc::advance_intent_table for kStrict moves (which is what we
// REPLACE in turn_flow's deferred-roll branch).
[[nodiscard]] CompactState apply_enemy_move_outcome(const CompactState& state,
                                                    std::size_t slot,
                                                    uint8_t new_move_idx) {
  const EnemyState& e = state.get_enemy(slot);
  const auto kind_idx = static_cast<std::size_t>(e.get_kind());
  assert(kind_idx < kMonsterMoveTables.size());
  const auto& table = kMonsterMoveTables[kind_idx];
  assert(new_move_idx < table.move_count);
  const auto new_move_id = table.moves[new_move_idx].id;

  EnemyState updated = EnemyStateBuilder{e}
                           .move_index(new_move_idx)
                           .current_move(new_move_id)
                           .build();
  return CompactStateBuilder{state}
      .enemy(slot, updated)
      .phase(Phase::kAtChanceDraw)
      .build();
}

// Expand the cartesian product over all enemy slots whose current move has
// a RandomBranch follow-up. Returns a list of (probability, state) pairs
// where each state has all RandomBranches resolved + phase=kAtChanceDraw.
//
// The cartesian product is built iteratively: start with {(1.0, state)},
// then for each pending-RNG enemy slot, multiplicatively expand each frontier
// state by the slot's eligible branches. Deterministic order (slot ascending,
// branch ascending) is required for FP weighted-sum reproducibility.
[[nodiscard]] std::vector<ChanceOutcome> enumerate_enemy_move_rng_to_draw_phase(
    const CompactState& state) {
  assert(state.get_phase() == Phase::kAtEnemyMoveRng);

  std::vector<ChanceOutcome> frontier;
  frontier.push_back(ChanceOutcome{.probability = 1.0, .child_state = state});

  const uint8_t ec = state.get_enemy_count();
  for (uint8_t slot = 0; slot < ec; ++slot) {
    // Snapshot pending-RNG status against the ORIGINAL state's enemy at this
    // slot — every frontier entry shares the same RandomBranch shape because
    // earlier-slot resolutions only mutate their own slot's move_idx.
    const EnemyState& src = state.get_enemy(slot);
    if (!src.get_alive()) {
      continue;
    }
    const auto kind_idx = static_cast<std::size_t>(src.get_kind());
    if (kind_idx >= kMonsterMoveTables.size()) {
      continue;
    }
    const auto& table = kMonsterMoveTables[kind_idx];
    const uint8_t move_idx = src.get_move_index();
    if (move_idx >= table.move_count) {
      continue;
    }
    const auto& move = table.moves[move_idx];
    if (move.follow_up_rule == FollowUpRule::kStrict) {
      continue;
    }

    const auto branch_outcomes = enumerate_branch_outcomes(move, move_idx);
    assert(!branch_outcomes.empty() &&
           "RandomBranch move with no eligible branches — invalid table data");

    std::vector<ChanceOutcome> next;
    next.reserve(frontier.size() * branch_outcomes.size());
    for (const auto& prev : frontier) {
      for (const auto& branch : branch_outcomes) {
        next.push_back(ChanceOutcome{
            .probability = prev.probability * branch.probability,
            .child_state = apply_enemy_move_outcome(prev.child_state, slot,
                                                    branch.new_move_idx),
        });
      }
    }
    frontier = std::move(next);
  }

  // Every frontier entry now has phase=kAtChanceDraw.
  return frontier;
}

}  // namespace

// ---------------------------------------------------------------------------
// Public helper exposed for testing (chance.h).
// ---------------------------------------------------------------------------
std::vector<BranchOutcome> enumerate_branch_outcomes(const MonsterMove& move,
                                                     uint8_t current_move_idx) {
  assert(move.follow_up_rule != FollowUpRule::kStrict &&
         "enumerate_branch_outcomes called on kStrict move (no RandomBranch)");
  assert(move.branch_count > 0 &&
         "RandomBranch follow-up declared but branch_count is zero — invalid "
         "table data");

  // Step 1: filter eligible branches under CannotRepeat. The "current_move_idx"
  // is the move index of the move that JUST resolved (i.e. the move whose
  // follow-up we are now rolling). A branch i is excluded iff
  //   branch_cannot_repeat[i] AND branch_indices[i] == current_move_idx.
  std::vector<uint8_t> eligible;
  eligible.reserve(move.branch_count);
  uint32_t weight_sum = 0;
  for (uint8_t i = 0; i < move.branch_count; ++i) {
    if (move.branch_cannot_repeat[i] &&
        move.branch_indices[i] == current_move_idx) {
      continue;
    }
    eligible.push_back(i);
    weight_sum += move.branch_weights[i];
  }
  assert(!eligible.empty() &&
         "all branches excluded by CannotRepeat — invalid table data");
  assert(weight_sum > 0 &&
         "eligible branch weights sum to zero — invalid table data");

  // Step 2: emit normalized outcomes (deterministic order = branch_indices
  // ascending across the eligible set; we iterate `eligible` in declaration
  // order which preserves the table's declared branch ordering).
  std::vector<BranchOutcome> outcomes;
  outcomes.reserve(eligible.size());
  const double denom = static_cast<double>(weight_sum);
  for (const uint8_t i : eligible) {
    outcomes.push_back(BranchOutcome{
        .probability = static_cast<double>(move.branch_weights[i]) / denom,
        .new_move_idx = move.branch_indices[i],
    });
  }
  return outcomes;
}

// ---------------------------------------------------------------------------
// enumerate_chance_outcomes — top-level dispatch on state.get_phase().
// ---------------------------------------------------------------------------
std::vector<ChanceOutcome> enumerate_chance_outcomes(
    const CompactState& state) {
  assert(!transition::is_terminal(state) &&
         "enumerate_chance_outcomes called on terminal state");

  if (state.get_phase() == Phase::kAtChanceDraw) {
    return enumerate_draw_outcomes(state);
  }

  if (state.get_phase() == Phase::kAtEnemyMoveRng) {
    // Two-stage chance node: (1) resolve all pending enemy-move RandomBranches
    // → states with phase=kAtChanceDraw + all rolls applied; (2) for each
    // resolved state, expand the card-draw chance node. Cartesian product
    // probabilities multiply. Each final outcome has phase=kPlayerActing.
    const auto move_stage = enumerate_enemy_move_rng_to_draw_phase(state);
    std::vector<ChanceOutcome> outcomes;
    outcomes.reserve(move_stage.size());  // upper bound on size before draws
    for (const auto& mo : move_stage) {
      const auto draw_stage = enumerate_draw_outcomes(mo.child_state);
      for (const auto& d : draw_stage) {
        outcomes.push_back(ChanceOutcome{
            .probability = mo.probability * d.probability,
            .child_state = d.child_state,
        });
      }
    }
    return outcomes;
  }

  // kPlayerActing is not a chance node — callers must guard before invoking.
  assert(false && "enumerate_chance_outcomes called on kPlayerActing state");
  return {};
}

}  // namespace sts2::ai
