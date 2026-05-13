#include <gtest/gtest.h>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/adapter.h"
#include "tests/oracle/adapter_fixtures.h"

// Round-trip test: fixture #1's bytes -> adapter facade -> CompactState ->
// expectimax search -> pinned (action, expected_hp, expected_rounds).
//
// The pinned values below are captured by running this test once with
// placeholder values, observing the actual Search output, and hardcoding
// the result back into the constants. This is the "first time the test
// runs, it will FAIL" protocol from the S1 brief — once committed the
// pins anchor the round-trip contract until the algorithm or the wire
// shape changes (in which case both sides regenerate intentionally per
// Q2-ADR-005 algorithm-change protocol).
//
// Re-surface trigger #2: if Search::solve crashes OR produces an action
// absent from transition::legal_actions(state), STOP and return to lead.

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::legal_actions;
using sts2::game::CardId;
using sts2::game::EnemySlot;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — fixture #1 (CultistsNormal, seed 42), captured
// 2026-05-12 on Linux x86_64, GCC + libstdc++. Cross-platform STL pinning
// noted as a risk in q2-architecture.md §1 (regression pins are STL-impl-
// specific); the wider 1e-6 eps below accommodates float-determinism drift.
// ---------------------------------------------------------------------------
constexpr ActionKind kFixture1ExpectedActionKind = ActionKind::kPlayCard;
constexpr CardId kFixture1ExpectedCardId = CardId::kStrike;
constexpr int kFixture1ExpectedTargetIdx = 0;  // CalcifiedCultist slot
constexpr double kFixture1ExpectedHp = 60.774403172281517;
constexpr double kFixture1ExpectedRounds = 6.4320807758307383;
constexpr double kPinTolerance = 1e-6;

// DISABLED by default — Search::solve on the full Silent starter combat is a
// slow tractability probe (~6 minutes on the dev machine). Mirrors the
// existing Search.DISABLED_StarterCombatSolves_LogsDiagnostics pattern in
// engine/cpp/tests/ai/test_search_known.cc. Run explicitly:
//
//   ./build/Release/sts2_oracle_tests \
//     --gtest_also_run_disabled_tests \
//     --gtest_filter='AdapterRoundtrip.DISABLED_*'
//
// CI runs this on the post-S1 verification gate (not on every commit).
TEST(AdapterRoundtrip, DISABLED_Fixture1_AdapterPlusSearch_PinnedAgreement) {
  const auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant for CULTISTS_NORMAL fixture";
  const CompactState s = std::get<CompactState>(r);

  // Sanity: CompactState shape matches Silent starter @ seed 42 boot.
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  EXPECT_EQ(s.get_player_hp().value(), 70);
  EXPECT_EQ(s.get_player_block().value(), 0);
  EXPECT_EQ(s.get_player_strength().value(), 0);
  EXPECT_EQ(s.get_player_weak().value(), 0);
  EXPECT_EQ(s.get_energy().value(), 3);
  EXPECT_EQ(s.get_round(), 1U);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);
  EXPECT_EQ(s.get_hand().total(), 7);
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);

  Search search;
  const SearchResult result = search.solve(s);
  EXPECT_FALSE(result.terminal);

  // Adapter-vs-prototype legality cross-check (re-surface trigger #2 guard).
  if (!result.terminal) {
    const auto actions = legal_actions(s);
    bool found_in_legals = false;
    for (const auto& a : actions) {
      if (a == result.best_action) {
        found_in_legals = true;
        break;
      }
    }
    ASSERT_TRUE(found_in_legals)
        << "Search produced an action absent from transition::legal_actions; "
        << "adapter-vs-prototype divergence (Q2 re-surface trigger #2). "
        << "Action: kind=" << static_cast<int>(result.best_action.kind)
        << " card_id=" << static_cast<int>(result.best_action.card_id)
        << " target=" << result.best_action.target_idx.raw();
  }

  // PINNED action + value.
  EXPECT_EQ(result.best_action.kind, kFixture1ExpectedActionKind);
  EXPECT_EQ(result.best_action.card_id, kFixture1ExpectedCardId);
  EXPECT_EQ(result.best_action.target_idx,
            EnemySlot{kFixture1ExpectedTargetIdx});
  EXPECT_NEAR(result.score.expected_hp, kFixture1ExpectedHp, kPinTolerance);
  EXPECT_NEAR(result.score.expected_rounds, kFixture1ExpectedRounds,
              kPinTolerance);
}

}  // namespace
