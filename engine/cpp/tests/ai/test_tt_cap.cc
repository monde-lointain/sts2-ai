#include <gtest/gtest.h>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyStateBuilder;
using sts2::ai::kMaxTtEntries;
using sts2::ai::Phase;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::SolveStatus;
using sts2::game::MoveId;
using sts2::game::Stat;

// A terminal state (all enemies dead) — solve returns immediately.
CompactState make_terminal_state(Stat player_hp = Stat{50}) {
  return CompactStateBuilder{}
      .player_hp(player_hp)
      .enemy(0, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
      .enemy(1, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
      .phase(Phase::kPlayerActing)
      .round(1)
      .build();
}

// ---------------------------------------------------------------------------

// wave-22-fix-4/H.beta: kMaxTtEntries 370M → 200M (Q2-ADR-013 Amendment 4
// §LRU-memory-tradeoff). LRU added ~32 B/entry; 200M × 70 B ≈ 14 GB stays
// under the 16 GB ceiling.
TEST(TtCap, KMaxTtEntriesIs200M) { EXPECT_EQ(kMaxTtEntries, 200'000'000u); }

// wave-22-fix-4/H.beta: LRU retires kCapExceeded path; converged solves now
// report eviction_count==0 (no LRU pressure) instead of cap_hit==false.
TEST(TtCap, ConvergedSolveReportsKConverged) {
  Search search;
  const CompactState s = make_terminal_state();
  const SearchResult result = search.solve(s);
  EXPECT_EQ(result.status, SolveStatus::kConverged);
  EXPECT_EQ(search.eviction_count(), 0u);
  EXPECT_EQ(result.entries_at_cap, 0u);
}

// wave-22-fix-4/H.beta: replaces former cap_hit-false-before-solve check.
TEST(TtCap, EvictionCountZeroBeforeSolve) {
  Search search;
  EXPECT_EQ(search.eviction_count(), 0u);
}

TEST(TtCap, SearchReusableAfterClear) {
  // Two consecutive solves on the same Search object both converge; TT
  // capacity is retained across clear() (no allocation churn).
  Search search;

  const CompactState s1 = make_terminal_state(Stat{40});
  const SearchResult r1 = search.solve(s1);
  EXPECT_EQ(r1.status, SolveStatus::kConverged);
  EXPECT_TRUE(r1.terminal);

  const CompactState s2 = make_terminal_state(Stat{70});
  const SearchResult r2 = search.solve(s2);
  EXPECT_EQ(r2.status, SolveStatus::kConverged);
  EXPECT_TRUE(r2.terminal);

  // Scores differ — confirms the TT was cleared between solves (not a stale
  // hit from r1).
  EXPECT_DOUBLE_EQ(r1.score.expected_hp, 40.0);
  EXPECT_DOUBLE_EQ(r2.score.expected_hp, 70.0);
}

TEST(TtCap, TerminalSolveHasZeroRounds) {
  Search search;
  const SearchResult r = search.solve(make_terminal_state(Stat{25}));
  EXPECT_TRUE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 25.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
}

// wave-22-fix-4/H.beta: kCapExceeded path retired; the heavy over-cap probe
// is now superseded by Search.LRU_EvictionFiresAtCap (tiny-cap synthetic in
// test_search_known.cc) which exercises the eviction mechanism without 14 GB.
TEST(TtCap, DISABLED_OverCapSetsCapHitFlag_Retired) {
  GTEST_SKIP() << "RETIRED post wave-22-fix-4/H.beta: see "
                  "Search.LRU_EvictionFiresAtCap for the LRU replacement.";
}

}  // namespace
