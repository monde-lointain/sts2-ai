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

// Pinned-seed regression test for NibbitsWeak, fixture #7, seed=42.
// Mirrors the Louse pin pattern (test_louse_progenitor_search_pins.cc).
//
// Pin captured POST-wave-24 K.beta-fix against fixture 07-nibbits-weak-seed42.
// Iterative-capture protocol per wave-20 §20.alpha: placeholder run prints
// actuals to stdout; values baked in; second run confirms green.
//
// DISABLED by default — Search::solve over NibbitsWeak state space is a
// slow tractability probe. Run explicitly:
//
//   build/Release/sts2_oracle_tests
//     --gtest_also_run_disabled_tests
//     --gtest_filter='*NibbitsWeakFixture7_PinnedAgreement*'
//
// Re-surface trigger: if expected_hp == 70 with expected_rounds < 5,
// Nibbit is still silent-no-op'ing — K.beta-fix regression; STOP and surface.

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::ai::Search;
using sts2::ai::SearchResult;
using sts2::ai::SolveStatus;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::legal_actions;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;
using sts2::oracle::registry::current_phase1_registry_sha256;
using sts2::oracle::registry::PinnedScenarioRow;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — fixture #7 (NibbitsWeak, seed 42),
// captured POST-K.beta-fix (wave-24) on Linux x86_64, GCC + libstdc++.
// Iterative-capture protocol per wave-20 §20.alpha.
// ---------------------------------------------------------------------------
// Captured 2026-05-18 against main @ 81e9aa2 (post K.beta-fix):
//   expected_hp     = 69.217677687600627
//   expected_rounds = 5.1979217430631941
//   tt_size         = 62045014
//   peak_rss_gb     = 6.19 (6496536 kB)
//   wall_clock      = 47 seconds
inline constexpr double kFixture7NibbitsWeakExpectedHp = 69.217677687600627;
inline constexpr double kFixture7NibbitsWeakExpectedRounds = 5.1979217430631941;
constexpr double kPinTolerance = 1e-6;

TEST(NibbitsWeakSearchPins, DISABLED_NibbitsWeakFixture7_PinnedAgreement) {
  const auto bytes = load_fixture_blob("07-nibbits-weak-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant for NIBBITS_WEAK fixture";
  const CompactState s = std::get<CompactState>(r);

  // Sanity: CompactState shape matches fixture #7 (NibbitsWeak @ seed 42 boot).
  ASSERT_EQ(s.get_enemy_count(), 1);
  ASSERT_EQ(s.get_enemy(0).get_kind(), sts2::game::MonsterKind::kNibbit);
  ASSERT_EQ(s.get_enemy(0).get_current_move(), sts2::game::MoveId::kButtMove);
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  EXPECT_GE(s.get_enemy(0).get_hp().value(), 42);
  EXPECT_LE(s.get_enemy(0).get_hp().value(), 46);
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

  ASSERT_EQ(result.status, SolveStatus::kConverged)
      << "NibbitsWeak solve hit kCapExceeded — Case C contingency. "
      << "entries_at_cap=" << result.entries_at_cap;

  // Log actuals for iterative-pin-capture protocol (plan §20.α).
  std::cout << "[NibbitsWeak] expected_hp=" << result.score.expected_hp
            << " expected_rounds=" << result.score.expected_rounds
            << " tt_size=" << search.tt_size() << '\n';

  // SANITY: expected_hp MUST be positive AND < 70 (player took damage).
  // If expected_hp == 70 with expected_rounds < 5, K.beta-fix regressed
  // (Nibbit not attacking). Surface before baking.
  EXPECT_GT(result.score.expected_hp, 0.0);
  EXPECT_LT(result.score.expected_hp, 70.0)
      << "expected_hp=70 likely indicates Nibbit attacks silent-no-op'd. "
      << "Check K.beta-fix substrate; do NOT bake this value.";

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
      .encounter_id = "NIBBITS_WEAK",
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

  // PINNED values — captured POST-K.beta-fix (wave-24). Replace placeholders.
  EXPECT_NEAR(result.score.expected_hp, kFixture7NibbitsWeakExpectedHp,
              kPinTolerance);
  EXPECT_NEAR(result.score.expected_rounds, kFixture7NibbitsWeakExpectedRounds,
              kPinTolerance);
}

}  // namespace
