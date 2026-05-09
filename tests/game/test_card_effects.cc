#include <gtest/gtest.h>

#include "sts2/game/card_effects.h"
#include "sts2/game/cards.h"
#include "sts2/game/types.h"

namespace {

using sts2::game::CardId;
using sts2::game::card_effects::CardEffect;
using sts2::game::card_effects::card_effect_for;
using sts2::game::card_effects::kCardEffects;
using sts2::game::card_effects::kCountedCardIds;

// Every CardId except kNone is present in the table exactly once.
TEST(CardEffectsTable, AllNonNoneIdsPresent) {
  const CardId all_counted[] = {CardId::kStrike, CardId::kDefend,
                                 CardId::kNeutralize, CardId::kSurvivor};
  for (CardId id : all_counted) {
    bool found = false;
    for (const CardEffect& e : kCardEffects) {
      if (e.id == id) {
        found = true;
        break;
      }
    }
    EXPECT_TRUE(found) << "CardId " << static_cast<int>(id) << " missing";
  }
}

// kNone is absent from the table.
TEST(CardEffectsTable, KNoneAbsent) {
  for (const CardEffect& e : kCardEffects) {
    EXPECT_NE(e.id, CardId::kNone);
  }
}

// kCountedCardIds covers every non-kNone CardId.
TEST(CardEffectsTable, CountedCardIdsComplete) {
  const CardId all_counted[] = {CardId::kStrike, CardId::kDefend,
                                 CardId::kNeutralize, CardId::kSurvivor};
  for (CardId id : all_counted) {
    bool found = false;
    for (CardId cid : kCountedCardIds) {
      if (cid == id) { found = true; break; }
    }
    EXPECT_TRUE(found) << "CardId " << static_cast<int>(id)
                       << " missing from kCountedCardIds";
  }
}

// card_effect_for returns the entry with the matching id.
TEST(CardEffectFor, LooksUpById) {
  for (const CardEffect& e : kCardEffects) {
    EXPECT_EQ(card_effect_for(e.id).id, e.id);
  }
}

// Spec consistency: table values match what the engine factories produce.
TEST(CardEffectsTableConsistency, StrikeMatchesFactory) {
  const auto& fx = card_effect_for(CardId::kStrike);
  auto card = sts2::cards::make_strike();
  EXPECT_EQ(card.cost, fx.cost);
  EXPECT_EQ(card.target, fx.target);
  EXPECT_EQ(card.base_damage, fx.base_damage);
  EXPECT_EQ(card.base_block, fx.base_block);
  EXPECT_EQ(card.name, fx.name);
}

TEST(CardEffectsTableConsistency, DefendMatchesFactory) {
  const auto& fx = card_effect_for(CardId::kDefend);
  auto card = sts2::cards::make_defend();
  EXPECT_EQ(card.cost, fx.cost);
  EXPECT_EQ(card.target, fx.target);
  EXPECT_EQ(card.base_damage, fx.base_damage);
  EXPECT_EQ(card.base_block, fx.base_block);
  EXPECT_EQ(card.name, fx.name);
}

TEST(CardEffectsTableConsistency, NeutralizeMatchesFactory) {
  const auto& fx = card_effect_for(CardId::kNeutralize);
  auto card = sts2::cards::make_neutralize();
  EXPECT_EQ(card.cost, fx.cost);
  EXPECT_EQ(card.target, fx.target);
  EXPECT_EQ(card.base_damage, fx.base_damage);
  EXPECT_EQ(card.base_block, fx.base_block);
  EXPECT_EQ(card.name, fx.name);
  EXPECT_EQ(fx.weak_to_target, 1);
}

TEST(CardEffectsTableConsistency, SurvivorMatchesFactory) {
  const auto& fx = card_effect_for(CardId::kSurvivor);
  auto card = sts2::cards::make_survivor();
  EXPECT_EQ(card.cost, fx.cost);
  EXPECT_EQ(card.target, fx.target);
  EXPECT_EQ(card.base_damage, fx.base_damage);
  EXPECT_EQ(card.base_block, fx.base_block);
  EXPECT_EQ(card.name, fx.name);
  EXPECT_TRUE(fx.requires_discard);
}

}  // namespace
