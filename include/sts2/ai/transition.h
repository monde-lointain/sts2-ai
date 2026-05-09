#pragma once

#include <cstdint>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::ai::transition {

inline constexpr uint8_t kNoTarget = 0xFF;

enum class ActionKind : uint8_t { kPlayCard, kEndTurn };

struct Action {
  ActionKind kind = ActionKind::kEndTurn;
  sts2::game::CardId card_id = sts2::game::CardId::kNone;
  uint8_t target_idx = kNoTarget;
  sts2::game::CardId survivor_discard_id = sts2::game::CardId::kNone;
  bool operator==(const Action&) const = default;
};

[[nodiscard]] std::vector<Action> legal_actions(const CompactState& state);

[[nodiscard]] bool apply_player_action(CompactState& state, const Action& action);

}  // namespace sts2::ai::transition
