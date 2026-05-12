#pragma once

#include <cstdint>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"

namespace sts2::ai::transition {

enum class ActionKind : uint8_t { kPlayCard, kEndTurn };

struct Action {
  ActionKind kind = ActionKind::kEndTurn;
  sts2::game::CardId card_id = sts2::game::CardId::kNone;
  sts2::game::EnemySlot target_idx = sts2::game::EnemySlot::none();
  sts2::game::CardId survivor_discard_id = sts2::game::CardId::kNone;
  bool operator==(const Action&) const = default;
};

[[nodiscard]] std::vector<Action> legal_actions(const CompactState& state);

[[nodiscard]] bool apply_player_action(CompactState& state,
                                       const Action& action);

// clang-format off
// Phase transitions for end-of-turn resolution. Sequence to advance state
// across the chance boundary:
//   1. apply_player_action(state, EndTurn)             // T3: phase -> kAtChanceDraw
//   2. resolve_end_turn_pre_draw(state)                // T4: enemy phase + start-of-next-turn (no draw)
//   3. for each Outcome in probability::enumerate_draws(state.draw, draw_count(state)):
//        copy state; apply_draw(copy, outcome.hand); recurse.
//
// Runs the deterministic part of Combat::end_turn() up to but excluding the
// draw: end_player_turn (hand->discard, tick player powers), enemy_phase
// (zero block, act, tick), round++, roll enemy moves, reset block (round>1),
// refill energy. Leaves phase = kAtChanceDraw. May leave the player dead --
// caller checks is_terminal(state) before drawing.
// clang-format on
void resolve_end_turn_pre_draw(CompactState& state);

[[nodiscard]] int draw_count(const CompactState& state) noexcept;

// Apply a specific drawn-hand multiset. Drains the multiset from the draw
// pile; if the draw pile lacks any of the requested cards, reshuffles
// discard into draw before draining. After this call,
//   state.hand += drawn
//   state.draw -= drawn (potentially after a discard->draw reshuffle)
// and state.phase becomes kPlayerActing.
void apply_draw(CompactState& state, CardCounts drawn);

[[nodiscard]] bool is_terminal(const CompactState& state) noexcept;

}  // namespace sts2::ai::transition
