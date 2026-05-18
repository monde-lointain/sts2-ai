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
  // Wave-22.α APPENDED — Slimed-like Exhaust semantic. When true, the played
  // card vanishes (one-way deletion) instead of moving hand→discard. Default
  // false preserves all pre-wave-22 cards' semantic; only Slimed sets this.
  // Upstream: Slimed.cs CanonicalKeywords = {CardKeyword.Exhaust}.
  bool exhaust_on_play = false;
  // Wave-22.α APPENDED — number of cards drawn as a card's OnPlay effect.
  // Slimed.cs OnPlay calls CardPileCmd.Draw(choiceContext, 1, owner). For the
  // oracle this introduces an OnPlay chance node (draw from deck is random).
  // Wave-22.α scope does NOT wire the chance node — Slimed plays in the
  // C.2-α framework are deterministic noops (no card drawn). C.4-δ's
  // SmallSlimes pin verifies that policy never benefits from playing Slimed,
  // so this stub is sound for the pin-passing contract. Full OnPlay-draw
  // chance node deferred to a future wave (surfaced as TODO in transition.cc).
  int draws_on_play = 0;
};

inline constexpr std::array<CardEffect, 5> kCardEffects = {{
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
    // Wave-22.α APPENDED. Upstream: src/Core/Models/Cards/Slimed.cs.
    // cost=1, type=Status, Exhaust keyword, OnPlay: Draw 1 card. Added to
    // discard via slime GOOP / STICKY_SHOT moves (CardPileCmd.AddToCombat-
    // AndPreview targets PileType.Discard). draws_on_play=1 documents the
    // semantic; transition.cc's player-action path stubs the draw effect
    // (full OnPlay-draw chance node deferred — see CardEffect docs).
    {.name = "Slimed",
     .wire_model_id = "Slimed",
     .cpp_name = "kSlimed",
     .short_stats = "draw1",
     .description = {"Draw 1 card.", "Exhaust."},
     .id = CardId::kSlimed,
     .cost = 1,
     .type = CardType::kStatus,
     .target = TargetType::kNoTarget,
     .base_damage = 0,
     .base_block = 0,
     .weak_to_target = 0,
     .requires_discard = false,
     .exhaust_on_play = true,
     .draws_on_play = 1},
}};

inline constexpr std::array<CardId, 5> kCountedCardIds = {
    CardId::kStrike, CardId::kDefend, CardId::kNeutralize, CardId::kSurvivor,
    CardId::kSlimed,  // wave-22.α APPEND (preserves CardId-1 ordering
                      // invariant)
};

[[nodiscard]] constexpr const CardEffect& card_effect_for(CardId id) noexcept {
  const auto* const it = std::ranges::find_if(
      kCardEffects, [id](const CardEffect& e) { return e.id == id; });
  assert(it != kCardEffects.end() && "card_effect_for: invalid CardId");
  return (it != kCardEffects.end()) ? *it : kCardEffects.front();
}

[[nodiscard]] constexpr std::string_view card_wire_model_id(
    CardId id) noexcept {
  const auto* const it = std::ranges::find_if(
      kCardEffects, [id](const CardEffect& e) { return e.id == id; });
  return (it != kCardEffects.end()) ? it->wire_model_id : std::string_view{};
}

[[nodiscard]] constexpr CardId card_id_from_wire_model_id(
    std::string_view model_id) noexcept {
  const auto* const it = std::ranges::find_if(
      kCardEffects,
      [model_id](const CardEffect& e) { return e.wire_model_id == model_id; });
  return (it != kCardEffects.end()) ? it->id : CardId::kNone;
}

[[nodiscard]] constexpr std::string_view card_id_cpp_name(CardId id) noexcept {
  if (id == CardId::kNone) {
    return "kNone";
  }
  const auto* const it = std::ranges::find_if(
      kCardEffects, [id](const CardEffect& e) { return e.id == id; });
  return (it != kCardEffects.end()) ? it->cpp_name : std::string_view{};
}

}  // namespace sts2::game::card_effects
