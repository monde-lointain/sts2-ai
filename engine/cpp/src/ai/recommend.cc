#include "sts2/ai/recommend.h"

#include <cassert>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"
#include "sts2/input/input.h"

namespace sts2::ai {

Recommendation Recommender::recommend(const sts2::game::Combat& combat) {
  Recommendation rec;
  CompactState state = from_combat(combat);

  if (combat.combat_over() || transition::is_terminal(state)) {
    rec.combat_over = true;
    rec.action = sts2::input::Action{.kind = sts2::input::Action::kEndTurn,
                                     .card_idx = sts2::game::HandIndex::none()};
    rec.expected_hp = static_cast<double>(state.get_player_hp().value());
    return rec;
  }

  const SearchResult result = search_.solve(state);
  // Hash-only TT precondition (Q2-ADR-010 §re-derivation-invariants): a
  // converged solve is required to consume best_action / PV. Cap-aborted
  // solves leave score+action UNSPECIFIED.
  assert(result.status == SolveStatus::kConverged &&
         "Recommender consumed cap-aborted SearchResult");

  const transition::Action& best = result.best_action;
  if (best.kind == transition::ActionKind::kEndTurn) {
    rec.action = sts2::input::Action{.kind = sts2::input::Action::kEndTurn,
                                     .card_idx = sts2::game::HandIndex::none()};
  } else {
    const sts2::game::HandIndex hand_idx =
        combat.find_card_in_hand(best.card_id);
    assert(hand_idx.valid() && "search returned a card not in engine hand");
    rec.action = sts2::input::Action{.kind = sts2::input::Action::kPlayCard,
                                     .card_idx = hand_idx};
  }

  rec.target_idx = best.target_idx;
  rec.survivor_discard_id = best.survivor_discard_id;
  rec.expected_hp = result.score.expected_hp;
  rec.expected_rounds = result.score.expected_rounds;

  // PV walk: re-derive best_action at each successor player-decision node
  // via shared derive_best_action helper (sole source of truth for argmax
  // recovery — same FP path solve_player used). Terminates at terminal,
  // first chance event (EndTurn truncates), or — defensively — first TT
  // miss (unreachable on converged solve).
  CompactState pv_state = state;
  while (!transition::is_terminal(pv_state)) {
    // Only player-decision nodes have a meaningful argmax to recover.
    // Chance-node mid-PV would indicate the previous step was already
    // EndTurn (which breaks the loop below) — guard defensively.
    if (pv_state.get_phase() != Phase::kPlayerActing) {
      break;
    }
    const auto pv_score = search_.peek_score(pv_state);
    if (!pv_score.has_value()) {
      break;  // defensive — unreachable on converged solve
    }
    const transition::Action a =
        derive_best_action(search_, pv_state, *pv_score);

    if (a.kind == transition::ActionKind::kEndTurn) {
      PvStep step;
      step.kind = PvStep::kEndTurn;
      rec.principal_variation.push_back(step);
      break;
    }

    PvStep step;
    step.kind = PvStep::kPlayCard;
    step.card_id = a.card_id;
    step.target_idx = a.target_idx;
    step.survivor_discard_id = a.survivor_discard_id;
    rec.principal_variation.push_back(step);

    const auto next = transition::apply_player_action(pv_state, a);
    assert(next.has_value() && "PV walk: derived action rejected");
    pv_state = *next;
  }

  return rec;
}

}  // namespace sts2::ai
