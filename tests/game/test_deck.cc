// Tests for include/sts2/game/deck.h + src/game/deck.cc.
// Covers T-DCK-005..045.

#include <gtest/gtest.h>

#include <algorithm>
#include <optional>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/deck.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::tests::helpers::ExpectShuffleMatchesPinned;
using sts2::tests::seeds::kCombatTestSeed;
using sts2::tests::seeds::kSilentDeckShuffled_C0FFEE;

using Card = sts2::game::Card;
using CardId = sts2::game::CardId;
using Deck = sts2::game::Deck;
using Rng = sts2::game::Rng;

// -------------------------------------------------------------------------
// T-DCK-005 — BP — Default-constructed Deck has empty draw and discard piles.
// -------------------------------------------------------------------------
TEST(DeckConstruction, T_DCK_005_DefaultEmpty) {
  Deck d;
  EXPECT_EQ(d.draw_size(), 0U);
  EXPECT_EQ(d.discard_size(), 0U);
  EXPECT_EQ(d.total_size(), 0U);
}

// -------------------------------------------------------------------------
// T-DCK-010 — DF — load_starter shuffles the deck with the given Rng.
// Pinned via kSilentDeckShuffled_C0FFEE: the 12-card silent starter deck
// shuffled with Rng{kCombatTestSeed} must match the pinned order.
// -------------------------------------------------------------------------
TEST(DeckLoadStarter, T_DCK_010_ShuffleMatchesPinned) {
  Deck d;
  Rng rng{kCombatTestSeed};
  const std::vector<Card> original = sts2::cards::make_silent_starter_deck();
  std::vector<Card> input = original;

  d.load_starter(std::move(input), rng);

  ASSERT_EQ(d.draw_size(), original.size());
  // Extract ids from the draw pile to compare against the pinned array.
  std::vector<CardId> got_ids;
  for (const Card& c : d.draw_pile()) {
    got_ids.push_back(c.id);
  }
  std::vector<CardId> orig_ids;
  orig_ids.reserve(original.size());
  for (const Card& c : original) {
    orig_ids.push_back(c.id);
  }
  ExpectShuffleMatchesPinned(got_ids, kSilentDeckShuffled_C0FFEE, orig_ids);
}

// -------------------------------------------------------------------------
// T-DCK-015 — BP — load_starter clears prior state before loading.
// -------------------------------------------------------------------------
TEST(DeckLoadStarter, T_DCK_015_ClearsPriorState) {
  Deck d;
  Rng rng{kCombatTestSeed};

  // First load.
  std::vector<Card> deck1;
  deck1.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  d.load_starter(std::move(deck1), rng);
  ASSERT_EQ(d.draw_size(), 1U);

  // Discard one card to populate discard pile.
  d.discard(sts2::cards::make_card(sts2::game::CardId::kDefend));
  ASSERT_EQ(d.discard_size(), 1U);

  // Second load — must clear both piles.
  std::vector<Card> deck2;
  deck2.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  deck2.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  d.load_starter(std::move(deck2), rng);

  EXPECT_EQ(d.draw_size(), 2U);
  EXPECT_EQ(d.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// T-DCK-020 — BP — draw_one returns a card from the draw pile.
// -------------------------------------------------------------------------
TEST(DeckDrawOne, T_DCK_020_DrawFromNonEmptyPile) {
  Deck d;
  Rng rng{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.load_starter(std::move(deck), rng);
  ASSERT_EQ(d.draw_size(), 2U);

  std::optional<Card> c = d.draw_one(rng);

  ASSERT_TRUE(c.has_value());
  EXPECT_EQ(d.draw_size(), 1U);
  EXPECT_EQ(d.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// T-DCK-025 — DF — draw_one triggers reshuffle when draw pile is empty.
// Setup: 2-card deck, draw both into hand manually via draw_one, then
// discard one. draw_one on empty draw pile reshuffles and draws from result.
// -------------------------------------------------------------------------
TEST(DeckDrawOne, T_DCK_025_ReshufflesWhenDrawEmpty) {
  Deck d;
  Rng rng{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.load_starter(std::move(deck), rng);

  // Drain draw pile.
  auto c1 = d.draw_one(rng);
  auto c2 = d.draw_one(rng);
  ASSERT_TRUE(c1.has_value());
  ASSERT_TRUE(c2.has_value());
  ASSERT_EQ(d.draw_size(), 0U);

  // Discard one card to populate discard pile.
  d.discard(std::move(*c1));
  ASSERT_EQ(d.discard_size(), 1U);

  // draw_one should reshuffle discard -> draw, then draw.
  auto c3 = d.draw_one(rng);

  EXPECT_TRUE(c3.has_value());
  EXPECT_EQ(d.draw_size(), 0U);
  EXPECT_EQ(d.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// T-DCK-030 — EG — draw_one returns nullopt when both piles are empty.
// -------------------------------------------------------------------------
TEST(DeckDrawOne, T_DCK_030_BothEmptyReturnsNullopt) {
  Deck d;
  Rng rng{kCombatTestSeed};

  std::optional<Card> c = d.draw_one(rng);

  EXPECT_FALSE(c.has_value());
  EXPECT_EQ(d.draw_size(), 0U);
  EXPECT_EQ(d.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// T-DCK-035 — BP — discard pushes a card onto the discard pile.
// -------------------------------------------------------------------------
TEST(DeckDiscard, T_DCK_035_PushesToDiscardPile) {
  Deck d;
  d.discard(sts2::cards::make_card(sts2::game::CardId::kStrike));
  EXPECT_EQ(d.discard_size(), 1U);
  EXPECT_EQ(d.discard_pile()[0].id, CardId::kStrike);

  d.discard(sts2::cards::make_card(sts2::game::CardId::kDefend));
  EXPECT_EQ(d.discard_size(), 2U);
  EXPECT_EQ(d.discard_pile()[1].id, CardId::kDefend);
}

// -------------------------------------------------------------------------
// T-DCK-040 — BP, DF — reshuffle moves all discard -> draw and shuffles.
// Multiset of cards is preserved; discard becomes empty.
// -------------------------------------------------------------------------
TEST(DeckReshuffle, T_DCK_040_MovesDiscardToDrawAndShuffles) {
  Deck d;
  Rng rng{kCombatTestSeed};

  d.discard(sts2::cards::make_card(sts2::game::CardId::kStrike));
  d.discard(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.discard(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  ASSERT_EQ(d.discard_size(), 3U);
  ASSERT_EQ(d.draw_size(), 0U);

  std::vector<CardId> expected_ids;
  for (const Card& c : d.discard_pile()) {
    expected_ids.push_back(c.id);
  }
  std::sort(expected_ids.begin(), expected_ids.end());

  d.reshuffle(rng);

  EXPECT_EQ(d.discard_size(), 0U);
  EXPECT_EQ(d.draw_size(), 3U);
  EXPECT_EQ(d.total_size(), 3U);

  std::vector<CardId> got_ids;
  for (const Card& c : d.draw_pile()) {
    got_ids.push_back(c.id);
  }
  std::sort(got_ids.begin(), got_ids.end());
  EXPECT_EQ(got_ids, expected_ids);
}

// -------------------------------------------------------------------------
// T-DCK-045 — BV — total_size is draw + discard.
// -------------------------------------------------------------------------
TEST(DeckSizeHelpers, T_DCK_045_TotalSizeIsSumOfPiles) {
  Deck d;
  Rng rng{kCombatTestSeed};
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  d.load_starter(std::move(deck), rng);
  d.discard(sts2::cards::make_card(sts2::game::CardId::kNeutralize));

  EXPECT_EQ(d.draw_size(), 2U);
  EXPECT_EQ(d.discard_size(), 1U);
  EXPECT_EQ(d.total_size(), 3U);
}

}  // namespace
