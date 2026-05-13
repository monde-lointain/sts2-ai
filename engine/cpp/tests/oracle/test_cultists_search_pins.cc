#include <gtest/gtest.h>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/registry/pin_row.h"
#include "sts2/oracle/registry/sha.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

// S2-T3 (Stream B): within-CULTISTS_NORMAL search pin at the non-fixture
// seed kCombatTestSeed (0xC0FFEE). Generalizes the S1-T5 fixture-bound pin
// pattern to the per-encounter registry shape (Q2-ADR-002) and stamps the
// resulting row with algorithm_sha + registry_sha per Q2-ADR-005.
//
// Mirrors test_adapter_roundtrip.cc's DISABLED_Fixture1_AdapterPlusSearch_*
// shape: sanity-assert CompactState boot, run Search::solve, cross-check
// best_action against legal_actions (re-surface trigger #2 guard), then
// compare against hardcoded pinned constants captured from a prior run.
//
// DISABLED by default — Search::solve over the full Silent starter combat is
// a slow tractability probe. Wired under the `make ci-slow` *DISABLED_*
// filter on sts2_oracle_tests (S2-T0). Run explicitly:
//
//   build/Release/sts2_oracle_tests
//     --gtest_also_run_disabled_tests
//     --gtest_filter='CultistsSearchPins.DISABLED_*'
//
// Re-surface trigger #2: if Search::solve crashes OR produces an action
// absent from transition::legal_actions(state), STOP and return to lead.

namespace {

using sts2::ai::CompactState;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::legal_actions;
using sts2::game::CardId;
using sts2::game::EnemySlot;
using sts2::oracle::adapter::current_manifest;
using sts2::oracle::registry::current_phase1_registry_sha256;
using sts2::oracle::registry::PinnedScenarioRow;
using sts2::tests::helpers::make_starter_combat;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — seed kCombatTestSeed (0xC0FFEE), captured
// 2026-05-12 on Linux x86_64, GCC + libstdc++. Cross-platform STL pinning is
// noted as a Q2 risk in q2-architecture.md §1 (regression pins are STL-impl-
// specific); the 1e-6 eps below accommodates float-determinism drift.
// ---------------------------------------------------------------------------
constexpr ActionKind kSeedC0ffeeExpectedActionKind = ActionKind::kPlayCard;
constexpr CardId kSeedC0ffeeExpectedCardId = CardId::kStrike;
constexpr int kSeedC0ffeeExpectedTargetIdx = 0;
constexpr double kSeedC0ffeeExpectedHp = 40.90829202578665;
constexpr double kSeedC0ffeeExpectedRounds = 6.4579809748486445;
constexpr double kPinTolerance = 1e-6;

TEST(CultistsSearchPins, DISABLED_StarterCombatSeedC0ffee_PinnedAgreement) {
  sts2::game::Combat combat =
      make_starter_combat(sts2::tests::seeds::kCombatTestSeed);
  const CompactState s = from_combat(combat);

  // Sanity: CompactState shape matches Silent starter @ seed 0xC0FFEE boot.
  // Mirrors the S1-T5 sanity asserts; HP/strength/weak are seed-independent
  // for the starter, energy=3, round=1, hand=7 are post-start invariants.
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  EXPECT_GT(s.get_enemy(0).get_hp().value(), 0);
  EXPECT_GT(s.get_enemy(1).get_hp().value(), 0);
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

  // Stamp the row per Q2-ADR-005: algorithm_sha (adapter manifest) +
  // registry_sha (SHA-256 of contracts/registry/phase1-silent.json).
  const PinnedScenarioRow row{
      .encounter_id = "CULTISTS_NORMAL",
      .seed = sts2::tests::seeds::kCombatTestSeed,
      .algorithm_sha = current_manifest().algorithm_sha,
      .registry_sha = current_phase1_registry_sha256(),
      .action_kind = result.best_action.kind,
      .action_card_id = result.best_action.card_id,
      .action_target_idx = result.best_action.target_idx.raw(),
      .expected_hp = result.score.expected_hp,
      .expected_rounds = result.score.expected_rounds,
  };

  // Q2-ADR-005 stamping discipline assertions.
  EXPECT_FALSE(row.algorithm_sha.empty())
      << "algorithm_sha must be populated from current_manifest()";
  EXPECT_EQ(row.registry_sha.size(), 64U)
      << "registry_sha is a 64-char hex string (SHA-256 lowercase-hex)";

  // PINNED action + value.
  EXPECT_EQ(row.action_kind, kSeedC0ffeeExpectedActionKind);
  EXPECT_EQ(row.action_card_id, kSeedC0ffeeExpectedCardId);
  EXPECT_EQ(row.action_target_idx, kSeedC0ffeeExpectedTargetIdx);
  EXPECT_NEAR(row.expected_hp, kSeedC0ffeeExpectedHp, kPinTolerance);
  EXPECT_NEAR(row.expected_rounds, kSeedC0ffeeExpectedRounds, kPinTolerance);
}

}  // namespace
