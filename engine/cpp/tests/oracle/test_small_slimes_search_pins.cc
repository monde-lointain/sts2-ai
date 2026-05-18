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

// SECOND_BLOCKER — wave-22/C.4-δ surface (2026-05-18).
//
// Zobrist widening (kMonsterKindCardinality 3→7, kMoveIdCardinality 5→10)
// done as project-lead fixup post-C.4-δ dispatch; APPEND-ONLY fill preserved
// cultist + LouseProgenitor byte identity (Zobrist.CultistRootKey_*
// passes; cultist + Louse pins bit-identical). HOWEVER, attempting to run
// the SmallSlimes synthetic solve produces SIGSEGV (signal 11) within ~1 sec
// wall-clock — peak RSS 529 MB suggests crash early in solve setup, not a
// TT-cap overflow. Root cause is a runtime bug in the slime solve pipeline
// (likely transition.cc handling of kAddStatusCard, chance.cc enumeration
// of weighted RandomBranch, OR a missing dispatch in C.2-α's substrate).
//
// GTEST_SKIP() guards CI from the crash. Resolution requires a debugging
// wave (gdb trace; bisect transition/chance/zobrist code paths) before
// SmallSlimes can be regression-pinned.
TEST(SmallSlimesSearchPins,
     DISABLED_SmallSlimesSyntheticVariantA_PinnedAgreement) {
  // Wave-23-prep findings (2026-05-18):
  //
  // ROOT CAUSE of the original SIGSEGV (signal 11, ~1 sec, RSS 529 MB): the
  // C.2-α from_combat / build_enemy_state path did NOT populate EnemyState's
  // kind_ field (sts2::game::Enemy struct had no `kind` member), so slime
  // EnemyStates defaulted to MonsterKind::kCultistCalcified. With kind
  // wrongly reported as cultist:
  //   - do_enemy_act dispatched slimes through the cultist act_on_intent
  //     path, which is a silent no-op for slime MoveIds (kTackleMove,
  //     kStickyShot, etc.) → no damage to player.
  //   - do_roll_next_move used the cultist advance_intent path, leaving
  //     slime current_move stuck at the initial value forever.
  // Combined effect: slime enemies were passive in CompactState. Combat
  // never terminated from player death; the search recursed via
  // solve_player → solve_chance until round exceeded the Zobrist
  // kMaxRound=256 cap, triggering an OOB read of the round-key table
  // (NDEBUG strips the assert) → SIGSEGV.
  //
  // FIX (wave-23-prep, this commit's type-system stream):
  //   1. Added MonsterKind kind field to sts2::game::Enemy (enemy.h).
  //   2. Slime factories (make_leaf_slime_s/m, make_twig_slime_s/m) set
  //      e.kind correctly (enemies.cc).
  //   3. build_enemy_state in state.cc copies e.kind into EnemyState +
  //      derives move_index_ via monster_moves::find_move_index.
  //   4. do_roll_next_move in transition.cc dispatches ALL kinds through
  //      advance_intent_table (uniform table-driven semantics; cultist
  //      cultist_table happens to encode the legacy advance_intent
  //      sequence exactly, so cultist Zobrist + search pins are
  //      bit-identical post-fix).
  //
  // VERIFIED post-fix:
  //   - Zobrist.CultistRootKey_MatchesPreWave21Pin still passes
  //     (lo=0xf812af56366b5548 hi=0x2c51edb8b6bd404e bit-identical).
  //   - CultistsSearchPins.DISABLED_StarterCombatSeedC0ffee_PinnedAgreement
  //     still passes (expected_hp=40.90829... expected_rounds=6.45798...
  //     bit-identical).
  //   - LouseProgenitorSearchPins.DISABLED_LouseProgenitorNormalFixture5_
  //     PinnedAgreement still passes (expected_hp=0.0407931
  //     expected_rounds=10.152 bit-identical).
  //   - AiStateParity.RandomWalk_CompactStateMatchesCombat still passes
  //     (do_roll_next_move now keeps move_index_ in sync with current_move_
  //     so from_combat(combat) == compact state after each step).
  //
  // SECOND BLOCKER surfaced by the fix (algorithmic, not implementation):
  // SmallSlimes Variant A search still does NOT terminate. The slime
  // damage budget (TwigSlimeS Tackle 4 + LeafSlimeM Clump 8 alternating
  // with STICKY 0 + LeafSlimeS Tackle 3 alternating with GOOP 0) is below
  // player's chained-Defend block budget (3 × Defend 5 = 15 block/turn
  // ≥ avg 9.5 dmg/turn). The "all-defend" sub-branch of the search tree
  // has no terminal state — player blocks forever, slimes never die,
  // round → ∞. Combined with kAddStatusCard accumulating Slimed cards
  // each turn (CardCounts.uint8_t wraparound past 16 → Zobrist
  // kMaxCountPerCardZone OOB), AND probability::enumerate_draws
  // asserting pool.total() ≤ kMaxN=12 (Silent starter only), the
  // search hits one of THREE assertion sites depending on which fires
  // first: probability.cc:66 (pool > 12), zobrist.cc:518 (round > 256
  // or count > 16), or stack overflow in solve_player (recursion depth
  // exceeds rlimit). All three are downstream manifestations of the
  // unbounded state-space issue.
  //
  // RESOLUTION REQUIRES: a wave-22-level architectural decision —
  // either (a) add an explicit search horizon (return horizon-score
  // when round > N), (b) saturate state.round + Slimed counts at the
  // Zobrist cap (with cycle-aware expectimax to avoid re-visiting
  // same state), or (c) reshape the SmallSlimes combat to guarantee
  // termination (e.g. give LeafSlimeS a Strength buff per round).
  // Each is a substrate-semantic change that warrants Q2-ADR
  // ratification. Out of scope for wave-23-prep.
  GTEST_SKIP() << "BLOCKER #3 (wave-23-prep): type-system fix landed (kind "
               << "dispatch repaired) but algorithmic non-convergence of "
               << "SmallSlimes search remains. Combat does not terminate "
               << "in the all-defend sub-branch (slime damage below "
               << "Defend-chain block budget). Resolution requires a "
               << "search-horizon or cycle-aware expectimax design (Q2-"
               << "ADR-013 amendment + new ADR). Surface to project-lead.";

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
