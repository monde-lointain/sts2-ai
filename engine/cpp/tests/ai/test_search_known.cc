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
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyStateBuilder;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::ai::Score;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::ActionKind;
using sts2::game::CardId;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::tests::helpers::make_starter_combat;

CompactState make_lethal_position(Stat enemy_hp = Stat{6}) {
  CardCounts hand;
  hand[CardId::kStrike] = 1;

  // Give the enemy a real attack so EndTurn branches terminate (kills player
  // eventually); this guarantees the search bottoms out via player death even
  // if Strike isn't chosen.
  return CompactStateBuilder{}
      .player_hp(Stat{70})
      .player_block(Stat{0})
      .player_strength(Stat{0})
      .player_weak(Stat{0})
      .energy(Stat{1})
      .round(5)
      .phase(Phase::kPlayerActing)
      .enemy(0, EnemyStateBuilder{}
                    .alive(true)
                    .hp(enemy_hp)
                    .current_move(MoveId::kDarkStrike)
                    .dark_strike_base(Stat{9})
                    .performed_first_move(true)
                    .build())
      .enemy(1, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
      .hand(hand)
      .build();
}

TEST(Search, TerminalState_ReturnsImmediately) {
  CompactState s =
      CompactStateBuilder{}
          .player_hp(Stat{0})  // dead -> terminal
          .enemy(0, EnemyStateBuilder{}.alive(true).hp(Stat{10}).build())
          .phase(Phase::kPlayerActing)
          .round(3)
          .build();

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_TRUE(r.terminal);
  EXPECT_DOUBLE_EQ(r.score.expected_hp, 0.0);
  EXPECT_DOUBLE_EQ(r.score.expected_rounds, 0.0);
}

TEST(Search, TerminalState_AllEnemiesDead) {
  CompactState s = CompactStateBuilder{}
                       .player_hp(Stat{42})
                       .enemy(0, EnemyStateBuilder{}.alive(false).build())
                       .enemy(1, EnemyStateBuilder{}.alive(false).build())
                       .phase(Phase::kPlayerActing)
                       .round(4)
                       .build();

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
  CompactState s = make_lethal_position(Stat{4});  // strike does 6, overkill

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
  CardCounts hand;
  hand[CardId::kDefend] = 1;
  CardCounts draw;
  draw[CardId::kStrike] = 1;  // drawn next turn to deliver lethal

  CompactState s =
      CompactStateBuilder{}
          .player_hp(Stat{10})
          .player_block(Stat{0})
          .energy(Stat{1})
          .round(5)
          .phase(Phase::kPlayerActing)
          .hand(hand)
          .draw(draw)
          .enemy(0, EnemyStateBuilder{}
                        .alive(true)
                        .hp(Stat{3})  // 1 Strike kills (6 dmg)
                        .strength(Stat{0})
                        .dark_strike_base(Stat{8})
                        .ritual_amount(Stat{2})
                        .current_move(MoveId::kDarkStrike)
                        .just_applied_ritual(false)
                        .performed_first_move(true)
                        .build())
          .enemy(1, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
          .build();

  Search search;
  const SearchResult r = search.solve(s);
  EXPECT_FALSE(r.terminal);
  EXPECT_EQ(r.best_action.kind, ActionKind::kPlayCard);
  EXPECT_EQ(r.best_action.card_id, CardId::kDefend);
}

TEST(Search, EmptyHand_PicksEndTurn) {
  // DarkStrike kills the player in finite turns; required so the search has a
  // terminal to back-propagate from when there are no playable cards.
  CompactState s =
      CompactStateBuilder{}
          .player_hp(Stat{30})
          .energy(Stat{3})
          .round(2)
          .phase(Phase::kPlayerActing)
          .enemy(0, EnemyStateBuilder{}
                        .alive(true)
                        .hp(Stat{50})
                        .current_move(MoveId::kDarkStrike)
                        .dark_strike_base(Stat{9})
                        .performed_first_move(true)
                        .build())
          .enemy(1, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
          .build();

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
  EXPECT_EQ(search.tt_size(), 0U);
  (void)search.solve(s);
  EXPECT_GE(search.tt_size(), 1U);
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
  sts2::game::Combat combat = make_starter_combat(0xC0FFEEULL);
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
