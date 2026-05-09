#pragma once

#include <cassert>
#include <cstdint>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::ai {

// Static, per-CardId metadata. Single source of truth for properties the AI
// transition layer and renderer query by id (cost, target kind, display name,
// pointer-to-member into CardCounts). Keep entries in sync with the
// make_*() factories in src/game/cards.cc.
struct CardMetadata {
  sts2::game::CardId id;
  std::string_view name;
  int cost;
  sts2::game::TargetType target;
  uint8_t CardCounts::*count_field;
};

inline constexpr CardMetadata kCardTable[] = {
    {sts2::game::CardId::kStrike, "Strike", 1, sts2::game::TargetType::kAnyEnemy,
     &CardCounts::strike},
    {sts2::game::CardId::kDefend, "Defend", 1, sts2::game::TargetType::kSelf,
     &CardCounts::defend},
    {sts2::game::CardId::kNeutralize, "Neutralize", 0,
     sts2::game::TargetType::kAnyEnemy, &CardCounts::neutralize},
    {sts2::game::CardId::kSurvivor, "Survivor", 1, sts2::game::TargetType::kSelf,
     &CardCounts::survivor},
};

// Linear scan over the 4-entry table; constexpr so it composes in compile-time
// contexts and the optimizer can fold calls with literal ids. Asserts on
// CardId::kNone (and any other unmapped value).
[[nodiscard]] constexpr const CardMetadata& card_metadata_for(
    sts2::game::CardId id) {
  for (const auto& m : kCardTable) {
    if (m.id == id) return m;
  }
  assert(false && "card_metadata_for: invalid CardId");
  return kCardTable[0];
}

}  // namespace sts2::ai
