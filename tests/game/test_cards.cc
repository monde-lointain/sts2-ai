// Tests for src/game/cards.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §8 (T-CRD-005..065).
//
// No pinned-value caveats: card factories are pure constants, and the
// Combat-using tests do not depend on shuffle order beyond "all 3 cards
// drawn into the starting hand". The fixed seed kCombatTestSeed is used
// for any Combat construction to match the broader test suite convention.

#include <gtest/gtest.h>

#include <utility>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::tests::helpers::MakeCombatWithEnemy;
using sts2::tests::seeds::kCombatTestSeed;

using Card = sts2::game::Card;
using CardId = sts2::game::CardId;
using CardType = sts2::game::CardType;
using Combat = sts2::game::Combat;
using Power = sts2::game::Power;
using PowerKind = sts2::game::PowerKind;
using TargetType = sts2::game::TargetType;

// -------------------------------------------------------------------------
// 8.1  cards::make_strike()
// -------------------------------------------------------------------------

// T-CRD-005 — BP — Static fields populated as specified; on_play is set.
TEST(CardsMakeStrike, T_CRD_005_StaticFields) {
  Card c = sts2::cards::make_strike();
  EXPECT_EQ(c.id, CardId::kStrike);
  EXPECT_EQ(c.name, "Strike");
  EXPECT_EQ(c.cost, 1);
  EXPECT_EQ(c.type, CardType::kAttack);
  EXPECT_EQ(c.target, TargetType::kAnyEnemy);
  EXPECT_EQ(c.base_damage, 6);
  EXPECT_EQ(c.short_stats, "6dmg");
  ASSERT_EQ(c.description.size(), 1U);
  EXPECT_EQ(c.description[0], "Deal 6 damage.");
  EXPECT_TRUE(static_cast<bool>(c.on_play));
}

// T-CRD-010 — DF — on_play deals base_damage to the targeted enemy.
// Capture `base = 6` is the def, deal_damage_to_enemy(0, base) is the use.
TEST(CardsMakeStrike, T_CRD_010_OnPlayDealsBaseDamage) {
  Combat combat = MakeCombatWithEnemy(kCombatTestSeed);

  Card card = sts2::cards::make_strike();
  ASSERT_TRUE(static_cast<bool>(card.on_play));
  card.on_play(combat, sts2::game::EnemySlot{0});

  ASSERT_EQ(combat.enemies().size(), 1U);
  EXPECT_EQ(combat.enemies()[0].vitals.hp, 34);
}

// T-CRD-015 — EG — Lambda value-captures `base`; post-construction mutation
// of `base_damage` does not affect the closure (locks capture-by-copy).
TEST(CardsMakeStrike, T_CRD_015_LambdaCapturesBaseByValue) {
  Combat combat = MakeCombatWithEnemy(kCombatTestSeed);

  Card c1 = sts2::cards::make_strike();
  c1.base_damage = 999;
  ASSERT_TRUE(static_cast<bool>(c1.on_play));
  c1.on_play(combat, sts2::game::EnemySlot{0});

  // Damage applied is 6 (captured value), not 999.
  EXPECT_EQ(combat.enemies()[0].vitals.hp, 34);
}

// -------------------------------------------------------------------------
// 8.2  cards::make_defend()
// -------------------------------------------------------------------------

// T-CRD-020 — BP — Static fields populated as specified; on_play is set.
TEST(CardsMakeDefend, T_CRD_020_StaticFields) {
  Card c = sts2::cards::make_defend();
  EXPECT_EQ(c.id, CardId::kDefend);
  EXPECT_EQ(c.name, "Defend");
  EXPECT_EQ(c.cost, 1);
  EXPECT_EQ(c.type, CardType::kSkill);
  EXPECT_EQ(c.target, TargetType::kSelf);
  EXPECT_EQ(c.base_block, 5);
  EXPECT_EQ(c.short_stats, "5blk");
  ASSERT_EQ(c.description.size(), 1U);
  EXPECT_EQ(c.description[0], "Gain 5 Block.");
  EXPECT_TRUE(static_cast<bool>(c.on_play));
}

// T-CRD-025 — DF — on_play adds 5 block via gain_player_block.
TEST(CardsMakeDefend, T_CRD_025_OnPlayGrantsBlock) {
  Combat combat{kCombatTestSeed};

  Card card = sts2::cards::make_defend();
  ASSERT_TRUE(static_cast<bool>(card.on_play));
  card.on_play(combat, sts2::game::EnemySlot::none());

  EXPECT_EQ(combat.player().vitals.block, 5);
}

// T-CRD-030 — EG — Capture-by-copy mirror of T-CRD-015.
// Mutating `base_block` after construction does not change the closure.
TEST(CardsMakeDefend, T_CRD_030_LambdaCapturesBaseByValue) {
  Combat combat{kCombatTestSeed};

  Card c1 = sts2::cards::make_defend();
  c1.base_block = 999;
  ASSERT_TRUE(static_cast<bool>(c1.on_play));
  c1.on_play(combat, sts2::game::EnemySlot::none());

  // Block applied is 5 (captured value), not 999.
  EXPECT_EQ(combat.player().vitals.block, 5);
}

// -------------------------------------------------------------------------
// 8.3  cards::make_neutralize()
// -------------------------------------------------------------------------

// T-CRD-035 — BP — Static fields; description has 2 lines.
TEST(CardsMakeNeutralize, T_CRD_035_StaticFields) {
  Card c = sts2::cards::make_neutralize();
  EXPECT_EQ(c.id, CardId::kNeutralize);
  EXPECT_EQ(c.name, "Neutralize");
  EXPECT_EQ(c.cost, 0);
  EXPECT_EQ(c.type, CardType::kAttack);
  EXPECT_EQ(c.target, TargetType::kAnyEnemy);
  EXPECT_EQ(c.base_damage, 3);
  EXPECT_EQ(c.short_stats, "3dmg");
  ASSERT_EQ(c.description.size(), 2U);
  EXPECT_EQ(c.description[0], "Deal 3 damage.");
  EXPECT_EQ(c.description[1], "Apply 1 Weak.");
  EXPECT_TRUE(static_cast<bool>(c.on_play));
}

// T-CRD-040 — DF — on_play deals 3 damage AND applies Weak 1.
// Locks call ordering (damage before Weak per source order).
TEST(CardsMakeNeutralize, T_CRD_040_OnPlayDealsDamageAndAppliesWeak) {
  Combat combat = MakeCombatWithEnemy(kCombatTestSeed);

  Card card = sts2::cards::make_neutralize();
  ASSERT_TRUE(static_cast<bool>(card.on_play));
  card.on_play(combat, sts2::game::EnemySlot{0});

  ASSERT_EQ(combat.enemies().size(), 1U);
  EXPECT_EQ(combat.enemies()[0].vitals.hp, 37);

  const Power* weak =
      sts2::powers::find(combat.enemies()[0].vitals.powers, PowerKind::kWeak);
  ASSERT_NE(weak, nullptr) << "Weak power not found on enemy 0";
  EXPECT_EQ(weak->amount, 1);
}

// -------------------------------------------------------------------------
// 8.4  cards::make_survivor()
// -------------------------------------------------------------------------

// T-CRD-045 — BP — Static fields; description has 2 lines.
TEST(CardsMakeSurvivor, T_CRD_045_StaticFields) {
  Card c = sts2::cards::make_survivor();
  EXPECT_EQ(c.id, CardId::kSurvivor);
  EXPECT_EQ(c.name, "Survivor");
  EXPECT_EQ(c.cost, 1);
  EXPECT_EQ(c.type, CardType::kSkill);
  EXPECT_EQ(c.target, TargetType::kSelf);
  EXPECT_EQ(c.base_block, 8);
  EXPECT_EQ(c.short_stats, "8blk");
  ASSERT_EQ(c.description.size(), 2U);
  EXPECT_EQ(c.description[0], "Gain 8 Block.");
  EXPECT_EQ(c.description[1], "Discard 1 card.");
  EXPECT_TRUE(static_cast<bool>(c.on_play));
}

// T-CRD-050 — DF — on_play gains 8 block AND discards via callback.
// Small deck (3 cards) → start_player_turn draws 7 (5 + Ring bonus 2)
// but only 3 are available, so all 3 land in the hand.
TEST(CardsMakeSurvivor, T_CRD_050_OnPlayGainsBlockAndDiscards) {
  Combat combat{kCombatTestSeed};
  combat.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });

  // Deck size 3 < draw count 7 → hand contains all 3 cards regardless of seed;
  // locks shuffle-order independence for this test. All-Strikes makes the
  // discarded card's id deterministic regardless of post-shuffle order.
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_strike());
  deck.push_back(sts2::cards::make_strike());
  deck.push_back(sts2::cards::make_strike());
  combat.start(std::move(deck));

  ASSERT_EQ(combat.player().hand.size(), 3U);
  ASSERT_EQ(combat.player().discard_pile.size(), 0U);

  Card card = sts2::cards::make_survivor();
  ASSERT_TRUE(static_cast<bool>(card.on_play));
  card.on_play(combat, sts2::game::EnemySlot::none());

  EXPECT_EQ(combat.player().vitals.block, 8);
  EXPECT_EQ(combat.player().hand.size(), 2U);
  ASSERT_EQ(combat.player().discard_pile.size(), 1U);
  EXPECT_EQ(combat.player().discard_pile[0].id, CardId::kStrike);
}

// T-CRD-055 — EG — on_play with empty hand: block applied, discard no-ops.
// Locks the Combat::discard_chosen_from_hand empty-hand early-return path.
TEST(CardsMakeSurvivor, T_CRD_055_OnPlayEmptyHandNoDiscard) {
  Combat combat{kCombatTestSeed};

  ASSERT_TRUE(combat.player().hand.empty());
  ASSERT_TRUE(combat.player().discard_pile.empty());

  Card card = sts2::cards::make_survivor();
  ASSERT_TRUE(static_cast<bool>(card.on_play));
  card.on_play(combat, sts2::game::EnemySlot::none());

  EXPECT_EQ(combat.player().vitals.block, 8);
  EXPECT_TRUE(combat.player().hand.empty());
  EXPECT_TRUE(combat.player().discard_pile.empty());
}

// -------------------------------------------------------------------------
// 8.5  cards::make_silent_starter_deck()
// -------------------------------------------------------------------------

// T-CRD-060 — BP — Deck size and per-id counts. Covers D1+D2 fall-through.
TEST(CardsStarterDeck, T_CRD_060_SizeAndCounts) {
  const std::vector<Card> deck = sts2::cards::make_silent_starter_deck();
  ASSERT_EQ(deck.size(), 12U);

  int strikes = 0;
  int defends = 0;
  int neutralizes = 0;
  int survivors = 0;
  for (const Card& c : deck) {
    switch (c.id) {
      case CardId::kStrike:
        ++strikes;
        break;
      case CardId::kDefend:
        ++defends;
        break;
      case CardId::kNeutralize:
        ++neutralizes;
        break;
      case CardId::kSurvivor:
        ++survivors;
        break;
      case CardId::kNone:
        break;
    }
  }
  EXPECT_EQ(strikes, 5);
  EXPECT_EQ(defends, 5);
  EXPECT_EQ(neutralizes, 1);
  EXPECT_EQ(survivors, 1);
  // Catches CardId::kNone (or any unhandled id) slipping into the deck.
  EXPECT_EQ(
      static_cast<std::size_t>(strikes + defends + neutralizes + survivors),
      deck.size());
}

// T-CRD-065 — DF — Order of construction: 5 Strike, 5 Defend, Neutralize,
// Survivor. Pre-shuffle order matters for tests that pin a specific seed's draw
// sequence.
TEST(CardsStarterDeck, T_CRD_065_OrderOfConstruction) {
  const std::vector<Card> deck = sts2::cards::make_silent_starter_deck();
  ASSERT_EQ(deck.size(), 12U);

  for (std::size_t i = 0; i < 5; ++i) {
    EXPECT_EQ(deck[i].id, CardId::kStrike) << "expected Strike at index " << i;
  }
  for (std::size_t i = 5; i < 10; ++i) {
    EXPECT_EQ(deck[i].id, CardId::kDefend) << "expected Defend at index " << i;
  }
  EXPECT_EQ(deck[10].id, CardId::kNeutralize);
  EXPECT_EQ(deck[11].id, CardId::kSurvivor);
}

}  // namespace
