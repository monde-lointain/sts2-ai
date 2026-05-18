#include "sts2/ai/chance.h"

#include <cassert>

#include "sts2/ai/probability.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"

namespace sts2::ai {

std::vector<ChanceOutcome> enumerate_chance_outcomes(
    const CompactState& state) {
  assert(state.get_phase() == Phase::kAtChanceDraw &&
         "enumerate_chance_outcomes called on non-chance state");
  assert(!transition::is_terminal(state) &&
         "enumerate_chance_outcomes called on terminal chance state");

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

}  // namespace sts2::ai
