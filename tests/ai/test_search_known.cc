#include <gtest/gtest.h>

#include <chrono>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::CardCounts;
using sts2::ai::CompactState;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::ai::Score;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::ActionKind;
using sts2::game::CardId;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::tests::helpers::MakeStarterCombat;

CompactState make_lethal_position() {
  CompactState s;
  s.player_hp = Stat{70};
  s.player_block = Stat{0};
  s.player_strength = Stat{0};
  s.player_weak = Stat{0};
  s.energy = Stat{1};
  s.round = 5;
  s.phase = Phase::kPlayerActing;
  s.enemies[0].alive = true;
  s.enemies[0].hp = Stat{6};
  // Give the enemy a real attack so EndTurn branches terminate (kills player
  // eventually); this guarantees the search bottoms out via player death even
  // if Strike isn't chosen.
  s.enemies[0].current_move = MoveId::kDarkStrike;
  s.enemies[0].dark_strike_base = Stat{9};
  s.enemies[0].performed_first_move = true;
  s.enemies[1].alive = false;
  s.enemies[1].hp = Stat{0};
  s.hand[CardId::kStrike] = 1;
  return s;
}

TEST(Search, TerminalState_ReturnsImmediately) {
  CompactState s;
  s.player_hp = Stat{0};  // dead -> terminal
  s.enemies[0].alive = true;
  s.enemies[0].hp = Stat{10};
  s.phase = Phase::kPlayerActing;
  s.round = 3;

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_TRUE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 0.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
}

TEST(Search, TerminalState_AllEnemiesDead) {
  CompactState s;
  s.player_hp = Stat{42};
  s.enemies[0].alive = false;
  s.enemies[1].alive = false;
  s.phase = Phase::kPlayerActing;
  s.round = 4;

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_TRUE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 42.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
}

TEST(Search, LethalThisTurn_PreferStrike) {
  CompactState s = make_lethal_position();

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_FALSE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 70.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
  EXPECT_EQ(r.best_action.kind, ActionKind::kPlayCard);
  EXPECT_EQ(r.best_action.card_id, CardId::kStrike);
  EXPECT_EQ(r.best_action.target_idx, sts2::game::EnemySlot{0});
}

TEST(Search, OverkillDamage_StillPicksKillingBlow) {
  CompactState s = make_lethal_position();
  s.enemies[0].hp = Stat{4};  // strike does 6, overkill

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_FALSE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 70.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
  EXPECT_EQ(r.best_action.kind, ActionKind::kPlayCard);
  EXPECT_EQ(r.best_action.card_id, CardId::kStrike);
  EXPECT_EQ(r.best_action.target_idx, sts2::game::EnemySlot{0});
}

TEST(Search, DefensivePlayPreservesHp) {
  // EndTurn directly: enemy DarkStrike(8) reduces hp to 2 then we kill on the
  // next turn -> Score{2, 1}. Defend first: block absorbs 5, hp lands at 7
  // before next-turn kill -> Score{7, 1}. HP dominates -> Defend wins.
  // Ritual=2 ensures strength grows so the search terminates (no infinite
  // defending; eventually damage exceeds any block).
  CompactState s;
  s.player_hp = Stat{10};
  s.player_block = Stat{0};
  s.energy = Stat{1};
  s.round = 5;
  s.phase = Phase::kPlayerActing;
  s.hand[CardId::kDefend] = 1;
  s.draw[CardId::kStrike] = 1;  // drawn next turn to deliver lethal
  s.enemies[0].alive = true;
  s.enemies[0].hp = Stat{3};  // 1 Strike kills (6 dmg)
  s.enemies[0].strength = Stat{0};
  s.enemies[0].dark_strike_base = Stat{8};
  s.enemies[0].ritual_amount = Stat{2};
  s.enemies[0].current_move = MoveId::kDarkStrike;
  s.enemies[0].just_applied_ritual = false;
  s.enemies[0].performed_first_move = true;
  s.enemies[1].alive = false;
  s.enemies[1].hp = Stat{0};

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_FALSE(r.terminal);
  EXPECT_EQ(r.best_action.kind, ActionKind::kPlayCard);
  EXPECT_EQ(r.best_action.card_id, CardId::kDefend);
}

TEST(Search, EmptyHand_PicksEndTurn) {
  CompactState s;
  s.player_hp = Stat{30};
  s.energy = Stat{3};
  s.round = 2;
  s.phase = Phase::kPlayerActing;
  s.hand = CardCounts{};
  s.draw = CardCounts{};
  s.discard = CardCounts{};
  s.enemies[0].alive = true;
  s.enemies[0].hp = Stat{50};
  // DarkStrike kills the player in finite turns; required so the search has a
  // terminal to back-propagate from when there are no playable cards.
  s.enemies[0].current_move = MoveId::kDarkStrike;
  s.enemies[0].dark_strike_base = Stat{9};
  s.enemies[0].performed_first_move = true;
  s.enemies[1].alive = false;
  s.enemies[1].hp = Stat{0};

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_FALSE(r.terminal);
  EXPECT_EQ(r.best_action.kind, ActionKind::kEndTurn);
}

TEST(Score, BetterThan_HpWins) {
  // HP dominates even when other has dramatically fewer rounds.
  EXPECT_TRUE((Score{5.0, 100.0}.better_than(Score{4.0, 0.0})));
}

TEST(Score, BetterThan_TiebreakRounds) {
  EXPECT_TRUE((Score{5.0, 3.0}.better_than(Score{5.0, 4.0})));
  EXPECT_FALSE((Score{5.0, 4.0}.better_than(Score{5.0, 3.0})));
}

TEST(Score, BetterThan_FloatTolerance) {
  // HP delta within eps -> falls back to rounds tiebreak; other has fewer.
  EXPECT_FALSE((Score{5.0 + 1e-12, 100.0}.better_than(Score{5.0, 0.0})));
}

TEST(Score, BetterThan_FloatTolerance_RoundsTiebreakBothSides) {
  // Within HP eps + within rounds eps -> not strictly better either way.
  EXPECT_FALSE((Score{5.0, 3.0}.better_than(Score{5.0 + 1e-12, 3.0 + 1e-12})));
  EXPECT_FALSE((Score{5.0 + 1e-12, 3.0 + 1e-12}.better_than(Score{5.0, 3.0})));
}

TEST(Search, StarterPositionSolve_PopulatesTt) {
  CompactState s = make_lethal_position();
  Search search;
  EXPECT_EQ(search.tt_size(), 0u);
  (void)search.solve(s);
  EXPECT_GE(search.tt_size(), 1u);
}

TEST(Search, Peek_UnvisitedReturnsNull) {
  CompactState s = make_lethal_position();
  Search search;
  EXPECT_EQ(search.peek(s), nullptr);
  (void)search.solve(s);
  EXPECT_NE(search.peek(s), nullptr);
}

// Slow tractability probe (~3 min). Disabled by default; re-enable via:
//   ctest --preset ninja-debug -- --gtest_also_run_disabled_tests
// or directly:
//   ./build/ninja-debug/Debug/sts2_simulator_tests
//     --gtest_also_run_disabled_tests
//     --gtest_filter='Search.DISABLED_StarterCombatSolves*'
TEST(Search, DISABLED_StarterCombatSolves_LogsDiagnostics) {
  sts2::game::Combat combat = MakeStarterCombat(0xC0FFEEULL);
  CompactState s = from_combat(combat);

  Search search;
  const auto t0 = std::chrono::steady_clock::now();
  const SearchResult r = search.solve(s);
  const auto t1 = std::chrono::steady_clock::now();
  const auto elapsed_ms =
      std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count();

  // Surface diagnostics; not asserting a hard ceiling here --
  // DONE_WITH_CONCERNS applies if this is excessively slow (>~30s).
  std::cerr << "[StarterCombat] solve elapsed_ms=" << elapsed_ms
            << " tt_size=" << search.tt_size()
            << " expected_hp=" << r.score.expected_hp
            << " expected_rounds=" << r.score.expected_rounds << "\n";
  EXPECT_FALSE(r.terminal);
}

}  // namespace
