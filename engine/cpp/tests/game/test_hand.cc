// Tests for include/sts2/game/hand.h and src/game/hand.cc.
//
// Covers: kMaxSize bound, add(), find(), play(), discard_at(),
// dump_into(), draw_from(), valid(), at(), size(), empty(), cards().

#include <gtest/gtest.h>

#include <cstddef>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/deck.h"
#include "sts2/game/hand.h"
#include "sts2/game/index_types.h"
#include "sts2/game/rng.h"

namespace {

using sts2::game::Card;
using sts2::game::CardId;
using sts2::game::Deck;
using sts2::game::Hand;
using sts2::game::HandIndex;
using sts2::game::Rng;

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------

Hand make_hand_of_n(int n) {
  Hand h;
  for (int i = 0; i < n; ++i) {
    h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  return h;
}

// Build a Deck loaded with n Defend cards (uses RNG only for shuffle).
Deck make_deck_of(int n, Rng& rng) {
  std::vector<Card> cards;
  cards.reserve(static_cast<std::size_t>(n));
  for (int i = 0; i < n; ++i) {
    cards.push_back(sts2::cards::make_card(sts2::game::CardId::kDefend));
  }
  Deck d;
  d.load_starter(std::move(cards), rng);
  return d;
}

// -------------------------------------------------------------------------
// kMaxSize and add()
// -------------------------------------------------------------------------

TEST(HandMaxSize, ConstantIsTen) { EXPECT_EQ(Hand::kMaxSize, 10); }

TEST(HandAdd, FillsToMaxSize) {
  Hand h;
  for (int i = 0; i < Hand::kMaxSize; ++i) {
    h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  EXPECT_EQ(h.size(), static_cast<std::size_t>(Hand::kMaxSize));
}

TEST(HandAdd, NoOpAtMaxSize) {
  Hand h = make_hand_of_n(Hand::kMaxSize);
  ASSERT_EQ(h.size(), static_cast<std::size_t>(Hand::kMaxSize));

  h.add(sts2::cards::make_card(
      sts2::game::CardId::kDefend));  // should be silently dropped

  EXPECT_EQ(h.size(), static_cast<std::size_t>(Hand::kMaxSize));
  // All cards should still be Strikes (the Defend was not added).
  for (const auto& c : h.cards()) {
    EXPECT_EQ(c.id, CardId::kStrike);
  }
}

TEST(HandAdd, GrowsBeforeMaxSize) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  EXPECT_EQ(h.size(), 1U);
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  EXPECT_EQ(h.size(), 2U);
}

// -------------------------------------------------------------------------
// empty() and size()
// -------------------------------------------------------------------------

TEST(HandSize, EmptyOnDefault) {
  Hand h;
  EXPECT_TRUE(h.empty());
  EXPECT_EQ(h.size(), 0U);
}

TEST(HandSize, TracksMutations) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  EXPECT_FALSE(h.empty());
  EXPECT_EQ(h.size(), 1U);
}

// -------------------------------------------------------------------------
// valid() and at()
// -------------------------------------------------------------------------

TEST(HandValid, NoneIsInvalid) {
  Hand h = make_hand_of_n(3);
  EXPECT_FALSE(h.valid(HandIndex::none()));
}

TEST(HandValid, NegativeIsInvalid) {
  Hand h = make_hand_of_n(3);
  EXPECT_FALSE(h.valid(HandIndex{-1}));
}

TEST(HandValid, InRangeIsValid) {
  Hand h = make_hand_of_n(3);
  EXPECT_TRUE(h.valid(HandIndex{0}));
  EXPECT_TRUE(h.valid(HandIndex{2}));
}

TEST(HandValid, PastEndIsInvalid) {
  Hand h = make_hand_of_n(3);
  EXPECT_FALSE(h.valid(HandIndex{3}));
}

TEST(HandAt, ReturnsCorrectCard) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  EXPECT_EQ(h.at(HandIndex{0}).id, CardId::kStrike);
  EXPECT_EQ(h.at(HandIndex{1}).id, CardId::kDefend);
}

// -------------------------------------------------------------------------
// find()
// -------------------------------------------------------------------------

TEST(HandFind, AbsentReturnsNone) {
  Hand h = make_hand_of_n(3);  // all Strikes
  EXPECT_EQ(h.find(CardId::kDefend), HandIndex::none());
}

TEST(HandFind, PresentReturnsFirstIndex) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));

  const HandIndex idx = h.find(CardId::kDefend);
  EXPECT_TRUE(h.valid(idx));
  EXPECT_EQ(idx.raw(), 1);
}

TEST(HandFind, EmptyHandReturnsNone) {
  Hand h;
  EXPECT_EQ(h.find(CardId::kStrike), HandIndex::none());
}

TEST(HandFind, FirstOfDuplicatesReturned) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  EXPECT_EQ(h.find(CardId::kDefend).raw(), 0);
}

// -------------------------------------------------------------------------
// play() — pops and returns the card, removes it from hand
// -------------------------------------------------------------------------

TEST(HandPlay, RemovesCardAndReturnsIt) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  h.add(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  ASSERT_EQ(h.size(), 3U);

  Card played = h.play(HandIndex{1});  // Defend at index 1

  EXPECT_EQ(played.id, CardId::kDefend);
  ASSERT_EQ(h.size(), 2U);
  EXPECT_EQ(h.cards()[0].id, CardId::kStrike);
  EXPECT_EQ(h.cards()[1].id, CardId::kNeutralize);
}

TEST(HandPlay, CanPlayFirst) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  Card played = h.play(HandIndex{0});

  EXPECT_EQ(played.id, CardId::kStrike);
  ASSERT_EQ(h.size(), 1U);
  EXPECT_EQ(h.cards()[0].id, CardId::kDefend);
}

TEST(HandPlay, CanPlayLast) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  Card played = h.play(HandIndex{1});

  EXPECT_EQ(played.id, CardId::kDefend);
  ASSERT_EQ(h.size(), 1U);
  EXPECT_EQ(h.cards()[0].id, CardId::kStrike);
}

// -------------------------------------------------------------------------
// discard_at() — semantically distinct from play but same operation
// -------------------------------------------------------------------------

TEST(HandDiscardAt, RemovesAndReturnsCard) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  Card discarded = h.discard_at(HandIndex{0});

  EXPECT_EQ(discarded.id, CardId::kStrike);
  ASSERT_EQ(h.size(), 1U);
  EXPECT_EQ(h.cards()[0].id, CardId::kDefend);
}

// Verify both methods produce the same result (shared implementation).
TEST(HandDiscardAt, SameResultAsPlay) {
  Hand h1;
  h1.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h1.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  Hand h2;
  h2.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h2.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  Card via_play = h1.play(HandIndex{0});
  Card via_discard = h2.discard_at(HandIndex{0});

  EXPECT_EQ(via_play.id, via_discard.id);
  EXPECT_EQ(h1.size(), h2.size());
}

// -------------------------------------------------------------------------
// dump_into()
// -------------------------------------------------------------------------

TEST(HandDumpInto, EmptyHandNoOp) {
  Hand h;
  Deck deck;
  Rng rng{0};
  deck.load_starter({}, rng);

  h.dump_into(deck);

  EXPECT_TRUE(h.empty());
  EXPECT_EQ(deck.discard_size(), 0U);
}

TEST(HandDumpInto, MovesAllCardsLifo) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));
  h.add(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  ASSERT_EQ(h.size(), 3U);

  Deck deck;
  Rng rng{0};
  deck.load_starter({}, rng);

  const CardId id0 = h.cards()[0].id;
  const CardId id1 = h.cards()[1].id;
  const CardId id2 = h.cards()[2].id;

  h.dump_into(deck);

  EXPECT_TRUE(h.empty());
  ASSERT_EQ(deck.discard_size(), 3U);
  // dump_into does LIFO: back is pushed first.
  EXPECT_EQ(deck.discard_pile()[0].id, id2);
  EXPECT_EQ(deck.discard_pile()[1].id, id1);
  EXPECT_EQ(deck.discard_pile()[2].id, id0);
}

// -------------------------------------------------------------------------
// draw_from()
// -------------------------------------------------------------------------

TEST(HandDrawFrom, DrawsUpToN) {
  Rng rng{0};
  Deck deck = make_deck_of(5, rng);
  Hand h;

  h.draw_from(deck, rng, 3);

  EXPECT_EQ(h.size(), 3U);
  EXPECT_EQ(deck.draw_size(), 2U);
}

TEST(HandDrawFrom, StopsAtMaxSize) {
  Rng rng{0};
  Deck deck = make_deck_of(15, rng);
  Hand h = make_hand_of_n(Hand::kMaxSize - 2);
  ASSERT_EQ(h.size(), static_cast<std::size_t>(Hand::kMaxSize - 2));

  h.draw_from(deck, rng, 5);  // only 2 slots remain

  EXPECT_EQ(h.size(), static_cast<std::size_t>(Hand::kMaxSize));
  EXPECT_EQ(deck.draw_size(), 13U);  // 15 - 2 drawn
}

TEST(HandDrawFrom, StopsAtDeckExhaustion) {
  Rng rng{0};
  Deck deck = make_deck_of(2, rng);
  Hand h;

  h.draw_from(deck, rng, 5);  // deck only has 2

  EXPECT_EQ(h.size(), 2U);
  EXPECT_EQ(deck.draw_size(), 0U);
}

TEST(HandDrawFrom, DrawZeroNoOp) {
  Rng rng{0};
  Deck deck = make_deck_of(3, rng);
  Hand h;

  h.draw_from(deck, rng, 0);

  EXPECT_TRUE(h.empty());
  EXPECT_EQ(deck.draw_size(), 3U);
}

// Exercises the Deck::draw_one reshuffle path: draw pile starts empty,
// discard has cards, draw_from succeeds by triggering an auto-reshuffle.
TEST(HandDrawFrom, ReshufflesDiscardWhenDrawEmpty) {
  Rng rng{0xDEADBEEFULL};
  Deck deck;  // both piles empty; draw empty, discard will be seeded below

  constexpr int k_n = 4;
  for (int i = 0; i < k_n; ++i) {
    deck.discard(sts2::cards::make_card(sts2::game::CardId::kStrike));
  }
  ASSERT_EQ(deck.draw_size(), 0U);
  ASSERT_EQ(deck.discard_size(), static_cast<std::size_t>(k_n));

  Hand h;
  h.draw_from(deck, rng, k_n);

  // All k_n cards should have been drawn after the reshuffle.
  EXPECT_EQ(h.size(), static_cast<std::size_t>(k_n));
  EXPECT_EQ(deck.draw_size(), 0U);
  EXPECT_EQ(deck.discard_size(), 0U);
}

// -------------------------------------------------------------------------
// cards() — read-only span
// -------------------------------------------------------------------------

TEST(HandCards, SpanReferencesStoredElements) {
  Hand h;
  h.add(sts2::cards::make_card(sts2::game::CardId::kStrike));
  h.add(sts2::cards::make_card(sts2::game::CardId::kDefend));

  const auto span = h.cards();
  ASSERT_EQ(span.size(), 2U);
  EXPECT_EQ(span[0].id, CardId::kStrike);
  EXPECT_EQ(span[1].id, CardId::kDefend);
  // Pointer identity: span points into the same storage.
  EXPECT_EQ(span.data(), &h.at(HandIndex{0}));
}

}  // namespace
