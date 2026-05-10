#include <gtest/gtest.h>

#include <cstddef>
#include <cstdint>
#include <iostream>
#include <vector>

#include "sts2/ai/recommend.h"
#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "sts2/input/input.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::Recommendation;
using sts2::ai::Recommender;
using sts2::game::Card;
using sts2::game::CardId;
using sts2::game::Combat;
using sts2::game::Enemy;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::game::Vitals;
using sts2::input::Action;
using sts2::tests::helpers::MakeStarterCombat;

// Tiny 5-Strike deck (matches test_recommend_legality.cc; kept local to
// avoid coupling test files via shared headers).
std::vector<Card> MakeTinyStrikeDeck() {
  std::vector<Card> deck;
  for (int i = 0; i < 5; ++i) deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  return deck;
}

// Lethal-this-turn engine combat: 1 enemy at 6 hp, 5-Strike deck. Round-1
// 7-card draw fills hand with all 5 Strikes; energy=3 means a single Strike
// (6 dmg, no Strength) lethals the enemy -> expected_hp = starting hp.
Combat MakeLethalCombat(uint64_t seed) {
  Combat c{seed};
  Enemy e{};
  e.vitals = Vitals{Stat{6}, Stat{6}, Stat{0}, {}};
  e.dark_strike_base = 9;
  e.current_move = MoveId::kDarkStrike;
  e.performed_first_move = true;
  c.add_enemy(std::move(e));
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });
  c.start(MakeTinyStrikeDeck());
  return c;
}

TEST(RecommendCalibration, KnownLethalPosition_ExpectedHpExact) {
  Combat combat = MakeLethalCombat(0xABCDULL);
  const int starting_hp = combat.player().vitals.hp.value();
  ASSERT_FALSE(combat.combat_over());

  Recommender rec;
  const Recommendation r = rec.recommend(combat);
  ASSERT_FALSE(r.combat_over);

  EXPECT_EQ(r.action.kind, Action::kPlayCard);
  EXPECT_EQ(r.target_idx, sts2::game::EnemySlot{0});
  EXPECT_NEAR(r.expected_hp, static_cast<double>(starting_hp),
              sts2::ai::Score::kEps);
  EXPECT_NEAR(r.expected_rounds, 0.0, sts2::ai::Score::kEps);
}

// Disabled by default — the first solve on a fresh starter combat is multi-
// minute. Re-enable via:
//   ctest --preset ninja-debug -- --gtest_also_run_disabled_tests
// or directly:
//   ./build/ninja-debug/Debug/sts2_simulator_tests
//     --gtest_also_run_disabled_tests
//     --gtest_filter='RecommendCalibration.DISABLED_StarterCombat*'
//
// Calibration design: the AI's expected_hp is over the multivariate-
// hypergeometric model of draws from CompactState card piles. Different engine
// seeds realize different concrete draw permutations from the SAME pile
// composition, so all trials share the same root expected_hp. Trials use
// different "draw seeds" while keeping the enemy-spawn seed fixed (so HPs
// match the root state the search planned against).
TEST(RecommendCalibration, DISABLED_StarterCombat_MonteCarloCalibration) {
  constexpr uint64_t kEnemySeed = 0xC0FFEEULL;
  constexpr int kTrials = 10;
  constexpr int kMaxStepsPerTrial = 400;

  // Build one combat per trial: enemy spawns rolled from kEnemySeed (fixed,
  // matches the root state); deck shuffle + draws driven by per-trial seed.
  // MakeStarterCombat ties both to one seed; inline a 2-seed variant.
  auto make_trial_combat = [](uint64_t draw_seed) {
    Combat c{draw_seed};
    sts2::game::Rng enemy_rng{kEnemySeed};
    c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
    c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
    c.set_pick_discard_callback(
        [](const Combat&) { return sts2::game::HandIndex{0}; });
    c.start(sts2::cards::make_silent_starter_deck());
    return c;
  };

  Recommender recommender;
  double expected_hp_at_root = 0.0;
  {
    // Prime the TT and capture expected_hp at the root. Use any draw_seed --
    // the from_combat hand contents will differ across seeds, so this seed's
    // root state is what the AI plans against. (For Monte Carlo, each trial's
    // root is a *different* CompactState because hand contents differ; we
    // therefore record per-trial root expected_hp and average those, then
    // compare to mean realized HP. With M=10 the mean of expected_hp across
    // trials is itself an unbiased estimator of the deck-conditional E[HP]
    // and the realized HPs sample the same distribution.)
    Combat seed_combat = make_trial_combat(0xC0FFEEULL);
    const Recommendation r = recommender.recommend(seed_combat);
    expected_hp_at_root = r.expected_hp;
    std::cerr << "[Calibration] seed-trial expected_hp=" << expected_hp_at_root
              << " expected_rounds=" << r.expected_rounds
              << " tt_size=" << recommender.tt_size() << "\n";
  }

  double sum_final_hp = 0.0;
  double sum_expected_hp = 0.0;
  int trials_completed = 0;

  for (int trial = 0; trial < kTrials; ++trial) {
    const uint64_t draw_seed = 0xC0FFEEULL ^ static_cast<uint64_t>(trial);
    Combat combat = make_trial_combat(draw_seed);

    // Survivor discard: resolve AI's survivor_discard_id to a hand index.
    // Closure captures last_rec by reference so the callback (invoked
    // synchronously inside play_card) sees the most-recent recommendation.
    Recommendation last_rec;
    combat.set_pick_discard_callback(
        [&last_rec](const Combat& c) -> sts2::game::HandIndex {
          if (last_rec.survivor_discard_id == CardId::kNone)
            return sts2::game::HandIndex{0};
          const auto idx = c.find_card_in_hand(last_rec.survivor_discard_id);
          return idx.valid() ? idx : sts2::game::HandIndex{0};
        });

    // Capture this trial's root expected_hp before stepping.
    const Recommendation root_rec = recommender.recommend(combat);
    sum_expected_hp += root_rec.expected_hp;

    int steps = 0;
    while (!combat.combat_over() && steps < kMaxStepsPerTrial) {
      last_rec = recommender.recommend(combat);
      if (last_rec.combat_over) break;

      if (last_rec.action.kind == Action::kEndTurn) {
        combat.end_turn();
      } else {
        ASSERT_EQ(last_rec.action.kind, Action::kPlayCard);
        const bool ok = combat.play_card(last_rec.action.card_idx,
                                         last_rec.target_idx);
        ASSERT_TRUE(ok) << "engine rejected AI play; trial=" << trial
                        << " step=" << steps;
      }
      ++steps;
    }

    sum_final_hp += static_cast<double>(combat.player().vitals.hp.value());
    ++trials_completed;
  }

  ASSERT_GT(trials_completed, 0);
  const double mean_realized = sum_final_hp / trials_completed;
  const double mean_expected = sum_expected_hp / trials_completed;
  std::cerr << "[Calibration] trials=" << trials_completed
            << " mean_realized_hp=" << mean_realized
            << " mean_expected_hp=" << mean_expected
            << " seed_expected_hp=" << expected_hp_at_root
            << " tt_size_final=" << recommender.tt_size() << "\n";

  // Loose tolerance: M=10 trials -> wide standard error.
  EXPECT_NEAR(mean_realized, mean_expected, 5.0);
}

}  // namespace
