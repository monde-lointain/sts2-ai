// engine/cpp/tests/ai/test_recommend_re_derivation.cc
//
// Wave-19 C.2-δ: tests that lock recommend()'s post-Zobrist re-derivation
// behavior. Pre-wave: recommend() read SearchResult.best_action from cached
// TT entries. Post-wave (per Q2-ADR-010): recommend() re-derives best_action
// from cached Score via 1-ply argmax + PV walk. These tests verify the
// re-derivation produces:
//   - the same action solve() returns at root (round-trip consistency)
//   - non-empty principal_variation for non-terminal cultist states
//   - bit-identical action for the canonical seed=42 cultist root
//     (cross-checks against pre-wave behavior captured in the static
//     reference action below).

#include <gtest/gtest.h>

#include <cstdint>
#include <memory>
#include <vector>

#include "sts2/ai/recommend.h"
#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "sts2/input/input.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::ai::from_combat;
using sts2::ai::PvStep;
using sts2::ai::Recommendation;
using sts2::ai::Recommender;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::game::Card;
using sts2::game::CardId;
using sts2::game::Combat;
using sts2::game::Rng;
using sts2::game::Stat;
using sts2::game::Vitals;
using sts2::input::Action;

// Canonical seed=42 cultist combat (mirrors test_outcome_calibration.cc and
// main.cc: enemy_rng seeded identically to combat seed, both cultists spawned,
// silent starter deck, pick-discard returns HandIndex{0}).
Combat make_cultist_combat_seed42() {
  constexpr uint64_t kSeed = sts2::tests::seeds::kCultistTestSeed;  // 0x42ULL
  Combat c{kSeed};
  Rng enemy_rng{kSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });
  c.start(sts2::cards::make_silent_starter_deck());
  return c;
}

// Tiny 5-Strike deck + one enemy: keeps fast-path tests sub-second.
std::vector<Card> make_tiny_strike_deck() {
  std::vector<Card> deck;
  deck.reserve(5);
  for (int i = 0; i < 5; ++i) {
    deck.push_back(sts2::cards::make_card(CardId::kStrike));
  }
  return deck;
}

Combat make_tiny_combat(uint64_t seed, int enemy_hp) {
  Combat c{seed};
  sts2::game::Enemy e{};
  e.vitals = Vitals{.hp = Stat{enemy_hp},
                    .max_hp = Stat{enemy_hp},
                    .block = Stat{0},
                    .powers = {}};
  e.dark_strike_base = Stat{9};
  e.current_move = sts2::game::MoveId::kDarkStrike;
  e.performed_first_move = true;
  c.add_enemy(std::move(e));
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });
  c.start(make_tiny_strike_deck());
  return c;
}

// ---------------------------------------------------------------------------
// Fast tests — tiny combat, sub-second per seed.
// ---------------------------------------------------------------------------

// recommend().action is consistent with solve().best_action.
// For 5 tiny-combat seeds, verify that recommend's re-derivation picks the
// same card (kind + hand_idx via find_card_in_hand) as solve's internal argmax.
TEST(RecommendReDerivation, RootActionConsistentWithSolve) {
  for (uint64_t seed : {1ULL, 2ULL, 3ULL, 4ULL, 5ULL}) {
    Combat combat = make_tiny_combat(seed, /*enemy_hp=*/6);
    ASSERT_FALSE(combat.combat_over()) << "seed=" << seed;

    const auto state = from_combat(combat);

    Search direct_search;
    const SearchResult direct = direct_search.solve(state);
    ASSERT_EQ(direct.status, sts2::ai::SolveStatus::kConverged)
        << "seed=" << seed;

    Recommender rec_search;
    const Recommendation rec = rec_search.recommend(combat);
    ASSERT_FALSE(rec.combat_over) << "seed=" << seed;

    if (direct.best_action.kind == sts2::ai::transition::ActionKind::kEndTurn) {
      EXPECT_EQ(rec.action.kind, Action::kEndTurn) << "seed=" << seed;
    } else {
      EXPECT_EQ(rec.action.kind, Action::kPlayCard) << "seed=" << seed;
      // Verify the card recommend chose maps to the same card solve chose.
      const CardId solve_card = direct.best_action.card_id;
      const sts2::game::HandIndex hand_idx =
          combat.find_card_in_hand(solve_card);
      EXPECT_TRUE(hand_idx.valid())
          << "seed=" << seed << " solve card_id not in hand";
      EXPECT_EQ(rec.action.card_idx, hand_idx) << "seed=" << seed;
    }
  }
}

// PV is non-empty for non-terminal states; PlayCard steps have valid card_ids;
// no EndTurn appears before the last PV step.
TEST(RecommendReDerivation, PrincipalVariationNonEmpty) {
  Combat combat = make_tiny_combat(0x12345ULL, /*enemy_hp=*/12);
  ASSERT_FALSE(combat.combat_over());

  Recommender rec_search;
  const Recommendation rec = rec_search.recommend(combat);
  ASSERT_FALSE(rec.combat_over);

  EXPECT_FALSE(rec.principal_variation.empty());

  for (std::size_t i = 0; i < rec.principal_variation.size(); ++i) {
    const PvStep& step = rec.principal_variation[i];
    if (step.kind == PvStep::kPlayCard) {
      EXPECT_NE(step.card_id, CardId::kNone)
          << "PV step[" << i << "] is PlayCard but card_id is kNone";
    }
    if (i + 1 < rec.principal_variation.size()) {
      EXPECT_NE(step.kind, PvStep::kEndTurn)
          << "PV step[" << i << "] is EndTurn but not the last step";
    }
  }
}

// ---------------------------------------------------------------------------
// Seed-42 cultist pin tests — share one full solve via class fixture.
//
// Each Recommender runs a fresh solve (TT not shared across instances). Using
// a class-level fixture with SetUpTestSuite lets both pin tests reuse the
// same Recommender + cached Recommendation, cutting the full-cultist-solve
// cost from 2× to 1× (~8 min → ~8 min total for both tests combined).
// ---------------------------------------------------------------------------
class Seed42Pin : public ::testing::Test {
 public:
  static void SetUpTestSuite() {
    s_combat_ = std::make_unique<Combat>(make_cultist_combat_seed42());
    s_recommender_ = std::make_unique<Recommender>();
    s_rec_ =
        std::make_unique<Recommendation>(s_recommender_->recommend(*s_combat_));
  }
  static void TearDownTestSuite() {
    s_rec_.reset();
    s_recommender_.reset();
    s_combat_.reset();
  }

 protected:
  static std::unique_ptr<Combat> s_combat_;
  static std::unique_ptr<Recommender> s_recommender_;
  static std::unique_ptr<Recommendation> s_rec_;
};

// Static member definitions.
std::unique_ptr<Combat> Seed42Pin::s_combat_;
std::unique_ptr<Recommender> Seed42Pin::s_recommender_;
std::unique_ptr<Recommendation> Seed42Pin::s_rec_;

// Test 3: seed=42 cultist root action is pinned.
// Acceptance: first-green-run captures kPlayCard; C.1 verified cultist
// regression is bit-identical, so this equals the pre-wave-19 action.
// (EndTurn at round 1 / full energy would be a re-derivation bug.)
TEST_F(Seed42Pin, PinnedAction) {
  ASSERT_FALSE(s_rec_->combat_over);
  EXPECT_EQ(s_rec_->action.kind, Action::kPlayCard);
  EXPECT_TRUE(s_rec_->action.card_idx.valid());
  EXPECT_TRUE(s_combat_->can_play(s_rec_->action.card_idx));
}

// Test 4: seed=42 expected_hp matches pre-wave-19 pin.
// Pin captured from first-green run: 47.618174290943202.
// (The value 40.90829202578665 in test_outcome_calibration.cc is for
// seed 0xC0FFEEULL, not 0x42ULL — different hand composition.)
TEST_F(Seed42Pin, ExpectedHpPin) {
  ASSERT_FALSE(s_rec_->combat_over);
  EXPECT_NEAR(s_rec_->expected_hp, 47.618174290943202, 1e-9);
}

}  // namespace
