#include <gtest/gtest.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <map>
#include <vector>

#include "sts2/ai/probability.h"
#include "sts2/ai/state.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/rng.h"

namespace {

using sts2::ai::CardCounts;
using sts2::ai::probability::binom;
using sts2::ai::probability::enumerate_draws;
using sts2::ai::probability::Outcome;

constexpr double kEps = 1e-12;

CardCounts make_pool(int s, int d, int n, int v) {
  CardCounts c;
  c.strike = static_cast<uint8_t>(s);
  c.defend = static_cast<uint8_t>(d);
  c.neutralize = static_cast<uint8_t>(n);
  c.survivor = static_cast<uint8_t>(v);
  return c;
}

double weight_sum(const std::vector<Outcome>& v) {
  double s = 0.0;
  for (const auto& o : v) s += o.weight;
  return s;
}

TEST(Probability, BinomBasic) {
  EXPECT_EQ(binom(0, 0), 1u);
  EXPECT_EQ(binom(5, 0), 1u);
  EXPECT_EQ(binom(5, 5), 1u);
  EXPECT_EQ(binom(5, 2), 10u);
  EXPECT_EQ(binom(12, 5), 792u);
  EXPECT_EQ(binom(12, 7), 792u);
  EXPECT_EQ(binom(-1, 0), 0u);
  EXPECT_EQ(binom(5, 6), 0u);
  EXPECT_EQ(binom(5, -1), 0u);
}

TEST(Probability, EnumerateZeroDraw) {
  auto out = enumerate_draws(make_pool(5, 5, 1, 1), 0);
  ASSERT_EQ(out.size(), 1u);
  EXPECT_EQ(out[0].hand, (CardCounts{}));
  EXPECT_DOUBLE_EQ(out[0].weight, 1.0);
}

TEST(Probability, EnumerateFullDraw) {
  auto out = enumerate_draws(make_pool(5, 5, 1, 1), 12);
  ASSERT_EQ(out.size(), 1u);
  EXPECT_EQ(out[0].hand, make_pool(5, 5, 1, 1));
  EXPECT_DOUBLE_EQ(out[0].weight, 1.0);
}

TEST(Probability, EnumerateStarterDeckTurn1) {
  const CardCounts pool = make_pool(5, 5, 1, 1);
  const int k = 7;
  auto out = enumerate_draws(pool, k);
  ASSERT_FALSE(out.empty());

  EXPECT_NEAR(weight_sum(out), 1.0, kEps);

  const uint64_t total_ordered = binom(12, k);
  double recovered = 0.0;
  for (const auto& o : out) {
    recovered += o.weight * static_cast<double>(total_ordered);
  }
  EXPECT_NEAR(recovered, static_cast<double>(total_ordered), kEps);

  // Most-likely multiset must contain at least 1 Strike and at least 1 Defend:
  // 7 non-Strike cards exist (5+1+1), so a 0-Strike hand is possible only as
  // exactly that 7-tuple; same for 0-Defend. Neither can dominate.
  auto best = std::max_element(
      out.begin(), out.end(),
      [](const Outcome& a, const Outcome& b) { return a.weight < b.weight; });
  EXPECT_GE(best->hand.strike, 1);
  EXPECT_GE(best->hand.defend, 1);
}

TEST(Probability, EnumerateAfterTurn1Sample) {
  const CardCounts pool = make_pool(3, 4, 0, 1);
  const int k = 5;
  auto out = enumerate_draws(pool, k);
  EXPECT_NEAR(weight_sum(out), 1.0, kEps);

  const CardCounts target = make_pool(2, 2, 0, 1);
  auto it = std::find_if(out.begin(), out.end(), [&](const Outcome& o) {
    return o.hand == target;
  });
  ASSERT_NE(it, out.end());
  // C(3,2)*C(4,2)*C(1,1) / C(8,5) = 18/56 = 9/28
  EXPECT_NEAR(it->weight, 9.0 / 28.0, kEps);
}

TEST(Probability, WeightsSumToOne) {
  for (int s = 0; s <= 5; ++s) {
    for (int d = 0; d <= 5; ++d) {
      for (int n = 0; n <= 1; ++n) {
        for (int v = 0; v <= 1; ++v) {
          CardCounts pool = make_pool(s, d, n, v);
          const int total = pool.total();
          if (total < 1) continue;
          const int kmax = std::min(total, 7);
          for (int k = 0; k <= kmax; ++k) {
            auto out = enumerate_draws(pool, k);
            ASSERT_FALSE(out.empty()) << "pool=(" << s << "," << d << "," << n
                                      << "," << v << ") k=" << k;
            EXPECT_NEAR(weight_sum(out), 1.0, kEps)
                << "pool=(" << s << "," << d << "," << n << "," << v
                << ") k=" << k;
          }
        }
      }
    }
  }
}

TEST(Probability, EngineDistributionMonteCarlo) {
  const CardCounts pool = make_pool(5, 5, 1, 1);
  const int k = 7;
  auto analytic = enumerate_draws(pool, k);

  std::map<std::array<uint8_t, 4>, std::size_t> hist;
  constexpr int kTrials = 5000;
  for (int seed = 1; seed <= kTrials; ++seed) {
    sts2::game::Combat c{static_cast<uint64_t>(seed)};
    sts2::game::Rng enemy_rng{static_cast<uint64_t>(seed)};
    c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
    c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
    c.set_pick_discard_callback([](const sts2::game::Combat&) { return 0; });
    c.start(sts2::cards::make_silent_starter_deck());
    const auto snap = sts2::ai::from_combat(c);
    ASSERT_EQ(snap.hand.total(), k);
    std::array<uint8_t, 4> key{snap.hand.strike, snap.hand.defend,
                               snap.hand.neutralize, snap.hand.survivor};
    ++hist[key];
  }

  double chi2 = 0.0;
  int df = 0;
  for (const auto& o : analytic) {
    std::array<uint8_t, 4> key{o.hand.strike, o.hand.defend, o.hand.neutralize,
                               o.hand.survivor};
    const double expected = o.weight * kTrials;
    const auto it = hist.find(key);
    const double observed =
        it == hist.end() ? 0.0 : static_cast<double>(it->second);
    if (expected > 0.0) {
      const double delta = observed - expected;
      chi2 += (delta * delta) / expected;
      ++df;
    }
  }
  ASSERT_GT(df, 1);
  // Pool {5,5,1,1} k=7 has support up to ~24 multisets but typically ~20 with
  // nonzero expected count for this pool/k. Threshold 80 is comfortably above
  // the 99.9th-percentile chi-square critical value for any df <= 24
  // (chi^2(19) at 99.9% ~= 43.7, chi^2(23) ~= 49.7), keeping the test robust
  // under seed luck without losing power against real bugs.
  EXPECT_LT(chi2, 80.0) << "chi2=" << chi2 << " bins=" << df;
}

}  // namespace
