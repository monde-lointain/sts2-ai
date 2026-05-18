#include <gtest/gtest.h>

#include <iostream>

#include "sts2/ai/search.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/oracle/adapter/adapter.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/registry/pin_row.h"
#include "sts2/oracle/registry/sha.h"
#include "tests/oracle/adapter_fixtures.h"

// Pinned-seed regression test for LouseProgenitorNormal, fixture #5, seed=42.
// Mirrors the cultist pin pattern (test_cultists_search_pins.cc) and the
// adapter round-trip shape (test_adapter_roundtrip.cc).
//
// Constants below are captured POST-POUNCE-fix (wave-20.α) via the
// iterative-pin-capture protocol (plan §20.α): placeholder run prints actuals
// to stdout; values baked in; second run confirms green.
//
// DISABLED by default — Search::solve over LouseProgenitor state space is a
// slow tractability probe (~60s wall-clock on dev machine). Run explicitly:
//
//   build-louse-pin/Release/sts2_oracle_tests \
//     --gtest_also_run_disabled_tests \
//     --gtest_filter='*LouseProgenitorNormalFixture5_PinnedAgreement*'
//
// Re-surface trigger: if pin capture produces NaN / inf / 0.0 STOP and return.

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::legal_actions;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;
using sts2::oracle::registry::current_phase1_registry_sha256;
using sts2::oracle::registry::PinnedScenarioRow;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — fixture #5 (LouseProgenitorNormal, seed 42),
// captured POST-POUNCE-fix (wave-20.α) on Linux x86_64, GCC + libstdc++.
// Cross-platform STL pinning noted as a Q2 risk (regression pins are
// STL-impl-specific); the 1e-6 eps below accommodates float-determinism drift.
// ---------------------------------------------------------------------------
inline constexpr double kFixture5LouseExpectedHp = 0.040793122639484494;
inline constexpr double kFixture5LouseExpectedRounds = 10.151992676894496;
constexpr double kPinTolerance = 1e-6;

TEST(LouseProgenitorSearchPins,
     DISABLED_LouseProgenitorNormalFixture5_PinnedAgreement) {
  const auto bytes = load_fixture_blob("05-louse-progenitor-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant for LOUSE_PROGENITOR_NORMAL fixture";
  const CompactState s = std::get<CompactState>(r);

  // Sanity: CompactState shape matches LouseProgenitor A0 @ seed 42 boot.
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  EXPECT_GT(s.get_enemy(0).get_hp().value(), 0);
  EXPECT_EQ(s.get_player_hp().value(), 70);
  EXPECT_EQ(s.get_player_block().value(), 0);
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

  // Log actuals for iterative-pin-capture protocol (plan §20.α).
  std::cout << "expected_hp=" << result.score.expected_hp
            << " expected_rounds=" << result.score.expected_rounds << '\n';

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
      .encounter_id = "LOUSE_PROGENITOR_NORMAL",
      .seed = 42,
      .algorithm_sha = sts2::oracle::adapter::current_manifest().algorithm_sha,
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

  // PINNED values — captured POST-POUNCE-fix (wave-20.α).
  EXPECT_NEAR(row.expected_hp, kFixture5LouseExpectedHp, kPinTolerance);
  EXPECT_NEAR(row.expected_rounds, kFixture5LouseExpectedRounds, kPinTolerance);
}

}  // namespace
