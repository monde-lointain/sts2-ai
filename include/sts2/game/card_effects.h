#pragma once

#include <array>
#include <cassert>
#include <string_view>

#include "sts2/game/types.h"

// Canonical per-card effect table, shared by the production engine
// (src/game/cards.cc) and the AI transition simulator (src/ai/transition.cc)
// to prevent silent divergence.

namespace sts2::game::card_effects {

struct CardEffect {
  CardId id;
  std::string_view name;
  int cost;
  CardType type;
  TargetType target;
  int base_damage;
  int base_block;
  int weak_to_target;
  bool requires_discard;
  std::string_view short_stats;
  std::array<std::string_view, 2> description;
};

inline constexpr CardEffect kCardEffects[] = {
    {CardId::kStrike,    "Strike",    1, CardType::kAttack, TargetType::kAnyEnemy, 6, 0, 0, false, "6dmg", {"Deal 6 damage.", ""}},
    {CardId::kDefend,    "Defend",    1, CardType::kSkill,  TargetType::kSelf,     0, 5, 0, false, "5blk", {"Gain 5 Block.", ""}},
    {CardId::kNeutralize,"Neutralize",0, CardType::kAttack, TargetType::kAnyEnemy, 3, 0, 1, false, "3dmg", {"Deal 3 damage.", "Apply 1 Weak."}},
    {CardId::kSurvivor,  "Survivor",  1, CardType::kSkill,  TargetType::kSelf,     0, 8, 0, true,  "8blk", {"Gain 8 Block.", "Discard 1 card."}},
};

inline constexpr CardId kCountedCardIds[] = {
    CardId::kStrike, CardId::kDefend, CardId::kNeutralize, CardId::kSurvivor,
};

[[nodiscard]] inline constexpr const CardEffect& card_effect_for(CardId id) noexcept {
  for (const auto& e : kCardEffects) {
    if (e.id == id) return e;
  }
  assert(false && "card_effect_for: invalid CardId");
  return kCardEffects[0];
}

}  // namespace sts2::game::card_effects
