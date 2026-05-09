#pragma once

#include <cstdint>
#include <vector>

#include "sts2/ai/search.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/input/input.h"

namespace sts2::game {
class Combat;
}  // namespace sts2::game

namespace sts2::ai {

// One step in the principal variation. Truncates at the first chance event
// (the draw following an EndTurn).
struct PvStep {
  enum Kind : uint8_t { kPlayCard, kEndTurn };
  Kind kind = kEndTurn;
  sts2::game::CardId card_id = sts2::game::CardId::kNone;
  sts2::game::EnemySlot target_idx = sts2::game::EnemySlot::none();
  sts2::game::CardId survivor_discard_id = sts2::game::CardId::kNone;
  bool operator==(const PvStep&) const = default;
};

// Recommendation for the current decision point. The expected outcome assumes
// the recommendation is followed plus the engine's random draws.
struct Recommendation {
  // Ready to feed into main.cc. For terminal/combat-over states, kind is
  // kEndTurn and card_idx is HandIndex::none(); gate via combat_over before consuming.
  sts2::input::Action action;
  sts2::game::EnemySlot target_idx = sts2::game::EnemySlot::none();
  sts2::game::CardId survivor_discard_id = sts2::game::CardId::kNone;
  double expected_hp = 0.0;
  double expected_rounds = 0.0;
  std::vector<PvStep> principal_variation;
  bool combat_over = false;
};

// Persistent recommender. Holds a Search across calls so the TT survives
// between moves in the same battle. Construct one Recommender per battle and
// call recommend() each time the player needs a hint.
class Recommender {
 public:
  [[nodiscard]] Recommendation recommend(const sts2::game::Combat& combat);

  // Diagnostics for tests / future profiling.
  [[nodiscard]] std::size_t tt_size() const noexcept {
    return search_.tt_size();
  }

 private:
  Search search_;
};

}  // namespace sts2::ai
