#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/adapter.h"
#include "tests/oracle/adapter_fixtures.h"

// Pinned-seed regression test for NibbitsNormal, fixture #8, seed=42.
// Mirrors the NibbitsWeak pin pattern (test_nibbits_weak_search_pins.cc),
// but is DEFERRED — see status block below.
//
// STATUS: DEFERRED — Wave-24 K.gamma-pin-normal hit kCapExceeded (Case B
// contingency). Pin attempt against fixture 08-nibbits-normal-seed42
// (2 Nibbits; slot 0 front SLICE_MOVE, slot 1 back HISS_MOVE) at the
// 370M-entry transposition-table cap exhausted before convergence:
//   entries_at_cap = 370,000,000
//   peak_rss       = 22.16 GB (23,237,368 kB)
//   wall_clock     = 4:31.71 (271.7s)
//   captured       = 2026-05-18 against main @ 7bfcffa (post K.gamma_pin_weak)
//
// Test is DOUBLE-DISABLED (DISABLED_DISABLED_) so it does not run under
// --gtest_also_run_disabled_tests. GTEST_SKIP() surfaces the cap-bust
// diagnostic when invoked directly via narrow --gtest_filter.
//
// Wave-24 STILL SHIPS: NibbitsWeak pin (commit 7bfcffa) is independent
// and unaffected. Adapter dispatch for NibbitsNormal STAYS LIVE (per
// K.gamma_setup); only the pin regression-lock is deferred until the
// Amendment lands.
//
// Favored Amendment direction (G1): canonical-form pre-Zobrist swap.
// Canonicalize 2-Nibbit slot ordering via lex-key on
// (hp, current_move, strength) before computing the transposition-table
// key. Symmetric 2-Nibbit states collapse to one TT entry, roughly
// halving breadth at horizon=25.

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — fixture #8 (NibbitsNormal, seed 42).
// PLACEHOLDERS retained (never reached: GTEST_SKIP fires before any
// EXPECT_NEAR would consume them). Amendment work that lifts the cap-bust
// must overwrite these with captured values and re-enable the test.
// ---------------------------------------------------------------------------
inline constexpr double kFixture8NibbitsNormalExpectedHp = -1.0;
inline constexpr double kFixture8NibbitsNormalExpectedRounds = -1.0;

// DOUBLE-DISABLED tombstone — Wave-24 K.gamma-pin-normal Case B
// contingency triggered. To re-enable post-Amendment, rename to single
// DISABLED_ prefix, restore the full solve body (template from
// test_nibbits_weak_search_pins.cc), capture values, and replace the
// kFixture8NibbitsNormal* placeholders.
TEST(NibbitsNormalSearchPins,
     DISABLED_DISABLED_NibbitsNormalFixture8_PinnedAgreement) {
  // Sanity-load + shape-check the fixture so we still get a tripwire if
  // the projection or fixture wire-encoding drifts (cheap; ~ms).
  const auto bytes = load_fixture_blob("08-nibbits-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant for NIBBITS_NORMAL fixture";
  const CompactState s = std::get<CompactState>(r);

  ASSERT_EQ(s.get_enemy_count(), 2);
  ASSERT_EQ(s.get_enemy(0).get_kind(), sts2::game::MonsterKind::kNibbit);
  ASSERT_EQ(s.get_enemy(1).get_kind(), sts2::game::MonsterKind::kNibbit);
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  ASSERT_EQ(s.get_enemy(0).get_current_move(), sts2::game::MoveId::kSliceMove);
  ASSERT_EQ(s.get_enemy(1).get_current_move(), sts2::game::MoveId::kHissMove);

  GTEST_SKIP()
      << "NibbitsNormal solve hit kCapExceeded at 370000000 entries "
      << "(peak_rss_gb=22.16 elapsed_wall=271.7s). "
      << "Wave-24 K.gamma_pin_normal Case B contingency triggered. "
      << "NibbitsWeak pin (commit 7bfcffa) ships independently. "
      << "Favored Amendment direction: G1 canonical-form pre-Zobrist swap "
      << "(canonicalize 2-Nibbit slot ordering via lex-key on hp/"
      << "current_move/strength). Adapter dispatch for NibbitsNormal STAYS "
      << "LIVE (K.gamma_setup); only the pin regression-lock is deferred "
      << "until Amendment lands.";

  // Unreachable — PLACEHOLDER constants intentionally never consumed.
  (void)kFixture8NibbitsNormalExpectedHp;
  (void)kFixture8NibbitsNormalExpectedRounds;
}

}  // namespace
