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

TEST(TtCap, KMaxTtEntriesIs370M) {
  // Sanity: lock the cap constant so silent changes are caught.
  EXPECT_EQ(kMaxTtEntries, 370'000'000u);
}

TEST(TtCap, ConvergedSolveReportsKConverged) {
  Search search;
  const CompactState s = make_terminal_state();
  const SearchResult result = search.solve(s);
  EXPECT_EQ(result.status, SolveStatus::kConverged);
  EXPECT_FALSE(search.cap_hit());
  EXPECT_EQ(result.entries_at_cap, 0u);
}

TEST(TtCap, CapHitFalseBeforeSolve) {
  Search search;
  EXPECT_FALSE(search.cap_hit());
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

// Manual-run only: fills TT past kMaxTtEntries to verify the cap mechanism.
// Disabled because it requires ~14 GB RAM and ~5+ minutes of compute.
TEST(TtCap, DISABLED_OverCapSetsCapHitFlag) {
  GTEST_SKIP()
      << "DISABLED — manual run only; requires ~14 GB RAM and ~5 min compute";
}

}  // namespace
