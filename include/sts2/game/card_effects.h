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
    {.id = CardId::kStrike,
     .name = "Strike",
     .cost = 1,
     .type = CardType::kAttack,
     .target = TargetType::kAnyEnemy,
     .base_damage = 6,
     .base_block = 0,
     .weak_to_target = 0,
     .requires_discard = false,
     .short_stats = "6dmg",
     .description = {"Deal 6 damage.", ""}},
    {.id = CardId::kDefend,
     .name = "Defend",
     .cost = 1,
     .type = CardType::kSkill,
     .target = TargetType::kSelf,
     .base_damage = 0,
     .base_block = 5,
     .weak_to_target = 0,
     .requires_discard = false,
     .short_stats = "5blk",
     .description = {"Gain 5 Block.", ""}},
    {.id = CardId::kNeutralize,
     .name = "Neutralize",
     .cost = 0,
     .type = CardType::kAttack,
     .target = TargetType::kAnyEnemy,
     .base_damage = 3,
     .base_block = 0,
     .weak_to_target = 1,
     .requires_discard = false,
     .short_stats = "3dmg",
     .description = {"Deal 3 damage.", "Apply 1 Weak."}},
    {.id = CardId::kSurvivor,
     .name = "Survivor",
     .cost = 1,
     .type = CardType::kSkill,
     .target = TargetType::kSelf,
     .base_damage = 0,
     .base_block = 8,
     .weak_to_target = 0,
     .requires_discard = true,
     .short_stats = "8blk",
     .description = {"Gain 8 Block.", "Discard 1 card."}},
};

inline constexpr CardId kCountedCardIds[] = {
    CardId::kStrike,
    CardId::kDefend,
    CardId::kNeutralize,
    CardId::kSurvivor,
};

[[nodiscard]] constexpr const CardEffect& card_effect_for(CardId id) noexcept {
  for (const auto& e : kCardEffects) {
    if (e.id == id) {
      return e;
    }
  }
  assert(false && "card_effect_for: invalid CardId");
  return kCardEffects[0];
}

}  // namespace sts2::game::card_effects
