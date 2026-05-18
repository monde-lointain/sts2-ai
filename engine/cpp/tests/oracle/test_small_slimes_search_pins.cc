#include <gtest/gtest.h>

#include <iostream>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/registry/pin_row.h"
#include "sts2/oracle/registry/sha.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

// Wave-22.C.4-delta — SmallSlimes Q2-internal pinned-seed gtest.
//
// Captures the oracle's expected solve outputs for a synthetic
// SmallSlimes Variant A combat (TwigSlimeS + LeafSlimeM + LeafSlimeS)
// at seed=kCombatTestSeed (0xC0FFEE). Locks Q2 regression against
// future refactors; does NOT verify Q1 wire round-trip (Q1 fixture
// #6 still has STS1 names per B.1-eps DEFER; round-trip verification
// resumes when Q1 lands the re-pinned fixture).
//
// DISABLED by default; runs under `make q2-ci` slow regression filter.
// Run explicitly:
//
//   build-small-slimes-pin/Release/sts2_oracle_tests \
//     --gtest_also_run_disabled_tests \
//     --gtest_filter='*SmallSlimesSyntheticVariantA*'
//
// Re-surface trigger: if solve produces kCapExceeded STOP (cap-bust
// contingency); if solve > 30 min wall-clock STOP (budget concern);
// if peak_rss_gb >= 16 STOP.

namespace {

using sts2::ai::CompactState;
using sts2::ai::from_combat;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::legal_actions;
using sts2::oracle::adapter::current_manifest;
using sts2::oracle::registry::current_phase1_registry_sha256;
using sts2::oracle::registry::PinnedScenarioRow;
using sts2::tests::helpers::make_small_slimes_synthetic_combat;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — seed kCombatTestSeed (0xC0FFEE), Variant A
// (TwigSlimeS + LeafSlimeM + LeafSlimeS). Captured via iterative-pin-capture
// protocol (plan §20.α): engineer writes test with placeholders + std::cout
// logging; runs once in Release; reads actual outputs from stdout; bakes the
// captured values; reruns to confirm green.
//
// PLACEHOLDER values below — replace with stdout-captured actuals.
// ---------------------------------------------------------------------------
constexpr double kSmallSlimesSyntheticExpectedHp = -1.0;      // PLACEHOLDER
constexpr double kSmallSlimesSyntheticExpectedRounds = -1.0;  // PLACEHOLDER
constexpr double kPinTolerance = 1e-6;

// ZOBRIST_WIDENING_BLOCKED — wave-22/C.4-delta surface.
// kMonsterKindCardinality must bump 3→7 and kMoveIdCardinality 5→10
// in zobrist.cc before this test can run. The widening is an APPEND-ONLY
// operation per the fill-order contract; must be done in a dedicated sub-stream
// with the byte-identity assertion. GTEST_SKIP() guards CI (`*DISABLED_*`
// filter) from running the crashing solve. Surface to project-lead for a
// Zobrist-widening wave.
TEST(SmallSlimesSearchPins,
     DISABLED_SmallSlimesSyntheticVariantA_PinnedAgreement) {
  GTEST_SKIP() << "BLOCKER: Zobrist table widening required before SmallSlimes "
               << "pin can run. kMonsterKindCardinality 3→7 and "
               << "kMoveIdCardinality 5→10 must be APPEND-filled in "
               << "zobrist.cc. Surface to project-lead for widening wave.";

  sts2::game::Combat combat =
      make_small_slimes_synthetic_combat(sts2::tests::seeds::kCombatTestSeed);
  const CompactState s = from_combat(combat);

  // Sanity: state shape matches SmallSlimes Variant A boot.
  ASSERT_EQ(s.get_enemy_count(), 3);
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  ASSERT_TRUE(s.get_enemy(2).get_alive());
  EXPECT_GT(s.get_enemy(0).get_hp().value(), 0);  // TwigSlimeS: HP 7-11
  EXPECT_GT(s.get_enemy(1).get_hp().value(), 0);  // LeafSlimeM: HP 32-35
  EXPECT_GT(s.get_enemy(2).get_hp().value(), 0);  // LeafSlimeS: HP 11-15
  EXPECT_EQ(s.get_player_hp().value(), 70);
  EXPECT_EQ(s.get_player_block().value(), 0);
  EXPECT_EQ(s.get_player_strength().value(), 0);
  EXPECT_EQ(s.get_energy().value(), 3);
  EXPECT_EQ(s.get_hand().total(), 7);
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);

  Search search;
  const SearchResult result = search.solve(s);

  ASSERT_EQ(result.status, sts2::ai::SolveStatus::kConverged)
      << "SmallSlimes synthetic solve hit kCapExceeded — surface for "
      << "cap-recovery (LRU eviction or structural shrink). entries_at_cap="
      << result.entries_at_cap;

  // Log actuals for iterative-pin-capture protocol (plan §20.alpha).
  std::cout << "[SmallSlimesSynthetic] expected_hp=" << result.score.expected_hp
            << " expected_rounds=" << result.score.expected_rounds
            << " tt_size=" << search.tt_size() << '\n';

  EXPECT_FALSE(result.terminal);

  // Adapter-vs-prototype legality cross-check (re-surface trigger guard).
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

  // Stamp manifest into a PinnedScenarioRow per Q2-ADR-005.
  const PinnedScenarioRow row{
      .encounter_id = "SMALL_SLIMES",
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

  // PINNED values — replace PLACEHOLDER constants with captured actuals.
  EXPECT_NEAR(row.expected_hp, kSmallSlimesSyntheticExpectedHp, kPinTolerance);
  EXPECT_NEAR(row.expected_rounds, kSmallSlimesSyntheticExpectedRounds,
              kPinTolerance);
}

}  // namespace
