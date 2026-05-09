#include "sts2/ai/recommend.h"

#include <cassert>
#include <cstddef>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "sts2/input/input.h"

namespace sts2::ai {

namespace {

int find_hand_index(const sts2::game::Combat& combat,
                    sts2::game::CardId card_id) {
  const auto& hand = combat.player().hand;
  for (std::size_t i = 0; i < hand.size(); ++i) {
    if (hand[i].id == card_id) return static_cast<int>(i);
  }
  return -1;
}

int normalize_target(uint8_t target) {
  return (target == transition::kNoTarget) ? -1 : static_cast<int>(target);
}

}  // namespace

Recommendation Recommender::recommend(const sts2::game::Combat& combat) {
  Recommendation rec;
  CompactState state = from_combat(combat);

  if (combat.combat_over() || transition::is_terminal(state)) {
    rec.combat_over = true;
    rec.action = sts2::input::Action{sts2::input::Action::kEndTurn, -1};
    rec.expected_hp = static_cast<double>(state.player_hp);
    return rec;
  }

  const SearchResult result = search_.solve(state);

  const transition::Action& best = result.best_action;
  if (best.kind == transition::ActionKind::kEndTurn) {
    rec.action = sts2::input::Action{sts2::input::Action::kEndTurn, -1};
  } else {
    const int hand_idx = find_hand_index(combat, best.card_id);
    assert(hand_idx >= 0 && "search returned a card not in engine hand");
    rec.action = sts2::input::Action{sts2::input::Action::kPlayCard, hand_idx};
  }

  rec.target_idx = normalize_target(best.target_idx);
  rec.survivor_discard_id = best.survivor_discard_id;
  rec.expected_hp = result.score.expected_hp;
  rec.expected_rounds = result.score.expected_rounds;

  // PV walk: follow best_action chain through the TT until terminal or first
  // chance event (truncate at EndTurn — the next step would be a draw).
  CompactState pv_state = state;
  while (!transition::is_terminal(pv_state)) {
    const SearchResult* peeked = search_.peek(pv_state);
    if (peeked == nullptr) break;  // defensive — unreachable on the best line

    const transition::Action& a = peeked->best_action;
    if (a.kind == transition::ActionKind::kEndTurn) {
      PvStep step;
      step.kind = PvStep::kEndTurn;
      rec.principal_variation.push_back(step);
      break;
    }

    PvStep step;
    step.kind = PvStep::kPlayCard;
    step.card_id = a.card_id;
    step.target_idx = normalize_target(a.target_idx);
    step.survivor_discard_id = a.survivor_discard_id;
    rec.principal_variation.push_back(step);

    const bool ok = transition::apply_player_action(pv_state, a);
    assert(ok && "PV walk: TT-suggested action rejected");
    (void)ok;
  }

  return rec;
}

}  // namespace sts2::ai
