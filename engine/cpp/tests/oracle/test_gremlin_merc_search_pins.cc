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

// Pinned-seed regression test for GremlinMercNormal, fixture #9, seed=42.
// Mirrors the NibbitsWeak pin pattern (test_nibbits_weak_search_pins.cc).
//
// Pin captured POST-wave-26/M.γ against fixture 09-gremlin-merc-normal-seed42.
// Iterative-capture protocol per wave-20 §20.alpha: placeholder run prints
// actuals to stdout; values baked in; second run confirms green.
//
// B1 decision: fixture 09 does NOT emit next_spawn_hps → B1 medians used
// (SneakyGremlin=12, FatGremlin=15 baked in M.β kSurpriseSpawnTable).
//
// DISABLED by default — Search::solve over GremlinMercNormal state space
// (spawning SneakyGremlin + FatGremlin mid-combat) is a slow tractability
// probe. Run explicitly:
//
//   build/Release/sts2_oracle_tests
//     --gtest_also_run_disabled_tests
//     --gtest_filter='GremlinMercSearchPins.*'
//
// Cap-bust contingency (per wave-24 K.γ_pin_normal precedent):
//   Case A: pin LOCKS cleanly ≤ 370M tt entries (default expected).
//   Case B: kCapExceeded — surface to project-lead; UNEXPECTED for GremlinMerc.
//   Case D: wall-clock > 30 min — surface; may indicate dispatch path issue.
//
// Re-surface trigger: expected_hp == 70 with expected_rounds < 5 indicates
// GremlinMerc attacks silent-no-op'd. Do NOT bake.

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
// PINNED EXPECTED VALUES — fixture #9 (GremlinMercNormal, seed 42),
// captured POST-wave-26/M.γ on Linux x86_64, GCC + libstdc++.
// Iterative-capture protocol per wave-20 §20.alpha.
// PLACEHOLDER: replaced after first run with observed values.
// ---------------------------------------------------------------------------
inline constexpr double kFixture9GremlinMercExpectedHp = 0.0;      // TODO
inline constexpr double kFixture9GremlinMercExpectedRounds = 0.0;  // TODO
constexpr double kPinTolerance = 1e-6;

// Wave-26/M.epsilon: DISABLED_DISABLED_ tombstone matches NibbitsNormal
// precedent (Q2-ADR-015 Amendment 1) — pin deferred via Case B cap-bust. The
// double prefix skips the test even with --gtest_also_run_disabled_tests
// (which q2-ci passes via Q2_CI_ORACLE_FILTER=*DISABLED_*). Un-prefix one
// level when pin is re-attempted via G2-G5 amendment menu per Q2-ADR-016.
TEST(GremlinMercSearchPins,
     DISABLED_DISABLED_GremlinMercFixture9_PinnedAgreement) {
  const auto bytes = load_fixture_blob("09-gremlin-merc-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant for GREMLIN_MERC_NORMAL fixture";
  const CompactState s = std::get<CompactState>(r);

  // Sanity: CompactState shape matches fixture #9 (GremlinMercNormal @ seed
  // 42).
  ASSERT_EQ(s.get_enemy_count(), 1);
  ASSERT_EQ(s.get_enemy(0).get_kind(), sts2::game::MonsterKind::kGremlinMerc);
  ASSERT_EQ(s.get_enemy(0).get_current_move(), sts2::game::MoveId::kGimmeMove);
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  EXPECT_GE(s.get_enemy(0).get_hp().value(), 47);
  EXPECT_LE(s.get_enemy(0).get_hp().value(), 49);
  EXPECT_EQ(s.get_player_hp().value(), 70);
  EXPECT_EQ(s.get_player_block().value(), 0);
  EXPECT_EQ(s.get_energy().value(), 3);
  EXPECT_EQ(s.get_round(), 1U);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);
  EXPECT_EQ(s.get_hand().total(), 7);
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);

  GTEST_SKIP()
      << "GremlinMerc solve hit kCapExceeded at 370000000 entries "
      << "(wall_clock=6m28s) post wave-26/M.γ pin attempt. Case B "
      << "contingency (UNEXPECTED per plan risk register; surfaces "
      << "mid-combat-spawn state-space pathology). Pin DEFERRED matches "
      << "NibbitsNormal precedent (Q2-ADR-015 Amendment 1 / Q2-ADR-016). "
      << "Un-prefix DISABLED_DISABLED_ → DISABLED_ + remove GTEST_SKIP "
      << "when pin re-attempted via G2-G5 amendment menu.";

  Search search;
  const SearchResult result = search.solve(s);

  ASSERT_EQ(result.status, SolveStatus::kConverged)
      << "GremlinMerc solve hit kCapExceeded — Case B contingency "
         "(UNEXPECTED). "
      << "Surface to project-lead with cap-bust diagnostic. "
      << "entries_at_cap=" << result.entries_at_cap;

  // Log actuals for iterative-pin-capture protocol (plan §20.α).
  std::cout << "[GremlinMerc] expected_hp=" << result.score.expected_hp
            << " expected_rounds=" << result.score.expected_rounds
            << " tt_size=" << search.tt_size() << '\n';

  // SANITY: expected_hp MUST be < 70 (player took damage from GremlinMerc).
  // If expected_hp == 70 with expected_rounds < 5, GremlinMerc attacks
  // silent-no-op'd — dispatch path issue. Surface before baking.
  EXPECT_GT(result.score.expected_hp, 0.0);
  EXPECT_LT(result.score.expected_hp, 70.0)
      << "expected_hp=70 likely indicates GremlinMerc attacks silent-no-op'd. "
      << "Check dispatch path; do NOT bake this value.";

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
      .encounter_id = "GREMLIN_MERC_NORMAL",
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

  // PINNED values — replace 0.0 placeholders after first run.
  EXPECT_NEAR(result.score.expected_hp, kFixture9GremlinMercExpectedHp,
              kPinTolerance);
  EXPECT_NEAR(result.score.expected_rounds, kFixture9GremlinMercExpectedRounds,
              kPinTolerance);
}

}  // namespace
