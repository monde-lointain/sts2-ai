#pragma once

#include <algorithm>
#include <array>
#include <cassert>
#include <string_view>

#include "sts2/game/types.h"

// Canonical per-card effect table, shared by the production engine
// (src/game/cards.cc) and the AI transition simulator (src/ai/transition.cc)
// to prevent silent divergence.

namespace sts2::game::card_effects {

struct CardEffect {
  std::string_view name;
  std::string_view wire_model_id;
  std::string_view cpp_name;
  std::string_view short_stats;
  std::array<std::string_view, 2> description;
  CardId id;
  int cost;
  CardType type;
  TargetType target;
  int base_damage;
  int base_block;
  int weak_to_target;
  bool requires_discard;
};

inline constexpr std::array<CardEffect, 4> kCardEffects = {{
    {.name = "Strike",
     .wire_model_id = "StrikeSilent",
     .cpp_name = "kStrike",
     .short_stats = "6dmg",
     .description = {"Deal 6 damage.", ""},
     .id = CardId::kStrike,
     .cost = 1,
     .type = CardType::kAttack,
     .target = TargetType::kAnyEnemy,
     .base_damage = 6,
     .base_block = 0,
     .weak_to_target = 0,
     .requires_discard = false},
    {.name = "Defend",
     .wire_model_id = "DefendSilent",
     .cpp_name = "kDefend",
     .short_stats = "5blk",
     .description = {"Gain 5 Block.", ""},
     .id = CardId::kDefend,
     .cost = 1,
     .type = CardType::kSkill,
     .target = TargetType::kSelf,
     .base_damage = 0,
     .base_block = 5,
     .weak_to_target = 0,
     .requires_discard = false},
    {.name = "Neutralize",
     .wire_model_id = "Neutralize",
     .cpp_name = "kNeutralize",
     .short_stats = "3dmg",
     .description = {"Deal 3 damage.", "Apply 1 Weak."},
     .id = CardId::kNeutralize,
     .cost = 0,
     .type = CardType::kAttack,
     .target = TargetType::kAnyEnemy,
     .base_damage = 3,
     .base_block = 0,
     .weak_to_target = 1,
     .requires_discard = false},
    {.name = "Survivor",
     .wire_model_id = "Survivor",
     .cpp_name = "kSurvivor",
     .short_stats = "8blk",
     .description = {"Gain 8 Block.", "Discard 1 card."},
     .id = CardId::kSurvivor,
     .cost = 1,
     .type = CardType::kSkill,
     .target = TargetType::kSelf,
     .base_damage = 0,
     .base_block = 8,
     .weak_to_target = 0,
     .requires_discard = true},
}};

inline constexpr std::array<CardId, 4> kCountedCardIds = {
    CardId::kStrike,
    CardId::kDefend,
    CardId::kNeutralize,
    CardId::kSurvivor,
};

[[nodiscard]] constexpr const CardEffect& card_effect_for(CardId id) noexcept {
  const auto it =
      std::find_if(kCardEffects.begin(), kCardEffects.end(),
                   [id](const CardEffect& e) { return e.id == id; });
  assert(it != kCardEffects.end() && "card_effect_for: invalid CardId");
  return (it != kCardEffects.end()) ? *it : kCardEffects.front();
}

[[nodiscard]] constexpr std::string_view card_wire_model_id(
    CardId id) noexcept {
  const auto it =
      std::find_if(kCardEffects.begin(), kCardEffects.end(),
                   [id](const CardEffect& e) { return e.id == id; });
  return (it != kCardEffects.end()) ? it->wire_model_id : std::string_view{};
}

[[nodiscard]] constexpr CardId card_id_from_wire_model_id(
    std::string_view model_id) noexcept {
  const auto it = std::find_if(
      kCardEffects.begin(), kCardEffects.end(),
      [model_id](const CardEffect& e) { return e.wire_model_id == model_id; });
  return (it != kCardEffects.end()) ? it->id : CardId::kNone;
}

[[nodiscard]] constexpr std::string_view card_id_cpp_name(CardId id) noexcept {
  if (id == CardId::kNone) {
    return "kNone";
  }
  const auto it =
      std::find_if(kCardEffects.begin(), kCardEffects.end(),
                   [id](const CardEffect& e) { return e.id == id; });
  return (it != kCardEffects.end()) ? it->cpp_name : std::string_view{};
}

}  // namespace sts2::game::card_effects
