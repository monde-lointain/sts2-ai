#pragma once

#include <cassert>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::ai {

// Static, per-CardId metadata. Single source of truth for properties the AI
// transition layer and renderer query by id (cost, target kind, display name).
// Keep entries in sync with the make_*() factories in src/game/cards.cc.
struct CardMetadata {
  sts2::game::CardId id;
  std::string_view name;
  int cost;
  sts2::game::TargetType target;
};

inline constexpr CardMetadata kCardTable[] = {
    {sts2::game::CardId::kStrike, "Strike", 1, sts2::game::TargetType::kAnyEnemy},
    {sts2::game::CardId::kDefend, "Defend", 1, sts2::game::TargetType::kSelf},
    {sts2::game::CardId::kNeutralize, "Neutralize", 0,
     sts2::game::TargetType::kAnyEnemy},
    {sts2::game::CardId::kSurvivor, "Survivor", 1, sts2::game::TargetType::kSelf},
};

inline constexpr sts2::game::CardId kCountedCardIds[] = {
    sts2::game::CardId::kStrike, sts2::game::CardId::kDefend,
    sts2::game::CardId::kNeutralize, sts2::game::CardId::kSurvivor};

[[nodiscard]] constexpr const CardMetadata& card_metadata_for(
    sts2::game::CardId id) {
  for (const auto& m : kCardTable) {
    if (m.id == id) return m;
  }
  assert(false && "card_metadata_for: invalid CardId");
  return kCardTable[0];
}

}  // namespace sts2::ai
