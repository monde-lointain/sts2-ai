#include <gtest/gtest.h>
#include <sys/resource.h>

#include <chrono>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
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
                    .kind(sts2::game::MonsterKind::kCultistCalcified)
                    .alive(true)
                    .hp(enemy_hp)
                    .current_move(MoveId::kDarkStrike)
                    .performed_first_move(true)
                    .build())
      .enemy(1, EnemyStateBuilder{}
                    .kind(sts2::game::MonsterKind::kCultistCalcified)
                    .alive(false)
                    .hp(Stat{0})
                    .build())
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
  // Calcified Cultist (dsb=9, ritual=2 from kMonsterMoveTables).
  // EndTurn: DarkStrike(9) → 10-9=1 hp; Defend first: 5 block absorbs 5 of 9
  // → 10-4=6 hp. Both survive next turn; Defend preserves more HP → Defend
  // wins. Ritual=2 ensures strength grows so search terminates.
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
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(true)
                        .hp(Stat{3})  // 1 Strike kills (6 dmg)
                        .strength(Stat{0})
                        .current_move(MoveId::kDarkStrike)
                        .just_applied_ritual(false)
                        .performed_first_move(true)
                        .build())
          .enemy(1, EnemyStateBuilder{}
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(false)
                        .hp(Stat{0})
                        .build())
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
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(true)
                        .hp(Stat{50})
                        .current_move(MoveId::kDarkStrike)
                        .performed_first_move(true)
                        .build())
          .enemy(1, EnemyStateBuilder{}
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(false)
                        .hp(Stat{0})
                        .build())
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

TEST(Search, PeekScore_UnvisitedReturnsNullopt) {
  CompactState s = make_lethal_position();
  Search search;
  EXPECT_FALSE(search.peek_score(s).has_value());
  (void)search.solve(s);
  EXPECT_TRUE(search.peek_score(s).has_value());
}

TEST(Search, HorizonCap_RoundOverLimit_ReturnsHorizonScore) {
  // State at round 26 (> kSearchHorizonRounds=25). solve_player should
  // return Score{player_hp, 0.0} without expanding legal actions.
  // Cultist enemy with DarkStrike ensures the state is non-terminal and
  // legal_actions() is non-empty if the horizon check were bypassed.
  CardCounts hand;
  hand[CardId::kStrike] = 1;

  CompactState s =
      CompactStateBuilder{}
          .player_hp(Stat{42})
          .player_block(Stat{0})
          .energy(Stat{3})
          .round(26)  // > kSearchHorizonRounds (25)
          .phase(Phase::kPlayerActing)
          .enemy(0, EnemyStateBuilder{}
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(true)
                        .hp(Stat{30})
                        .current_move(MoveId::kDarkStrike)
                        .performed_first_move(true)
                        .build())
          .enemy(1, EnemyStateBuilder{}
                        .kind(sts2::game::MonsterKind::kCultistCalcified)
                        .alive(false)
                        .hp(Stat{0})
                        .build())
          .hand(hand)
          .build();

  Search search;
  const SearchResult result = search.solve(s);

  EXPECT_EQ(result.status, sts2::ai::SolveStatus::kConverged);
  EXPECT_FALSE(result.terminal);
  EXPECT_DOUBLE_EQ(result.score.expected_hp, 42.0);
  EXPECT_DOUBLE_EQ(result.score.expected_rounds, 0.0);
  // Horizon path skips TT insertion — TT stays empty.
  EXPECT_EQ(search.tt_size(), 0U);
}

TEST(Search, HorizonCap_RoundAtLimit_DoesNotShortCircuit) {
  // Boundary: state at round 25 (== kSearchHorizonRounds) must NOT trigger
  // horizon cap (check is `>`, not `>=`). Use all-enemies-dead terminal so
  // the solve finishes immediately via the terminal-at-root short-circuit.
  CompactState s =
      CompactStateBuilder{}
          .player_hp(Stat{50})
          .energy(Stat{3})
          .round(25)  // == kSearchHorizonRounds; NOT triggered (cap uses >)
          .phase(Phase::kPlayerActing)
          .enemy(0, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
          .enemy(1, EnemyStateBuilder{}.alive(false).hp(Stat{0}).build())
          .build();

  Search search;
  const SearchResult result = search.solve(s);

  EXPECT_EQ(result.status, sts2::ai::SolveStatus::kConverged);
  EXPECT_TRUE(result.terminal);
  EXPECT_DOUBLE_EQ(result.score.expected_hp, 50.0);
  EXPECT_DOUBLE_EQ(result.score.expected_rounds, 0.0);
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

  // Wave-19/§5: peak RSS instrumentation.
  // Linux: ru_maxrss in KB; macOS: ru_maxrss in bytes. Q2 CI is Linux-only.
  struct rusage ru {};
  getrusage(RUSAGE_SELF, &ru);
  const double peak_rss_gb =
      static_cast<double>(ru.ru_maxrss) / (1024.0 * 1024.0);

  std::cout << "[StarterCombat] solve elapsed_ms=" << elapsed_ms
            << " tt_size=" << search.tt_size()
            << " expected_hp=" << r.score.expected_hp
            << " expected_rounds=" << r.score.expected_rounds
            << " peak_rss_gb=" << peak_rss_gb << '\n';
  EXPECT_FALSE(r.terminal);
  ASSERT_LT(peak_rss_gb, 16.0)
      << "Cultist solve exceeded 16 GB ceiling (Q2-ADR-011)";
}

}  // namespace
