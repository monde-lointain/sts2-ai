#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/adapter.h"
#include "tests/oracle/adapter_fixtures.h"

// Pinned-seed regression test for NibbitsNormal, fixture #8, seed=42.
// Mirrors the NibbitsWeak pin pattern (test_nibbits_weak_search_pins.cc),
// but is DEFERRED — see status block below.
//
// STATUS: DEFERRED — Wave-25 L.β pin re-capture against post-L.α
// canonical-form substrate STILL HIT kCapExceeded (Case B contingency
// PERSISTS). Pin attempt against fixture 08-nibbits-normal-seed42
// (2 Nibbits; slot 0 front SLICE_MOVE, slot 1 back HISS_MOVE) at the
// 370M-entry transposition-table cap exhausted before convergence:
//   entries_at_cap = 370,000,000
//   peak_rss       = 22.37 GB (23,461,540 kB)
//   wall_clock     = 4:15.27 (255.3s)
//   captured       = 2026-05-18 against main @ e465b57 (post L.α
//                    canonical-form pre-Zobrist swap, Q2-ADR-015
//                    Amendment 1)
//
// PRIOR ATTEMPT — Wave-24 K.gamma-pin-normal (pre-canonical-form):
//   entries_at_cap = 370,000,000
//   peak_rss       = 22.16 GB (23,237,368 kB)
//   wall_clock     = 4:31.71 (271.7s)
//   main @ 7bfcffa (post K.gamma_pin_weak)
//
// CANONICAL-FORM IMPACT: L.α reduced wall-clock by ~16s (-6%); RSS delta
// trivial. State-space breadth IS halved for symmetric 2-Nibbit states,
// but the asymmetric branches (slot 0 SLICE vs slot 1 HISS) plus rich
// Nibbit-move sequencing at horizon=25 still saturate the 370M cap.
// Canonical-form is necessary-but-insufficient; Amendment 2 needed.
//
// Test is DOUBLE-DISABLED (DISABLED_DISABLED_) so it does not run under
// --gtest_also_run_disabled_tests. GTEST_SKIP() surfaces the cap-bust
// diagnostic when invoked directly via narrow --gtest_filter.
//
// Wave-25 STILL SHIPS: L.α canonical-form lands as a state-space
// reduction primitive (BIT-IDENTICAL for Cultist/Louse/NibbitsWeak pins;
// confirmed independently). Adapter dispatch for NibbitsNormal STAYS
// LIVE; only the pin regression-lock remains deferred.
//
// Required Amendment 2 directions:
//   G2 — Horizon reduction (25 → 15-20) bounded by SmallSlimes survival
//        guard (Q2-ADR-013 Amendment 3 set 25 as the floor).
//   G3 — LRU TT eviction policy at the cap boundary (revisit
//        kMaxTtEntries policy; trades determinism for breadth).
//   Other — Iterative deepening + alpha-beta pruning on Score lattice.

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;

// ---------------------------------------------------------------------------
// PINNED EXPECTED VALUES — fixture #8 (NibbitsNormal, seed 42).
// PLACEHOLDERS retained (never reached: GTEST_SKIP fires before any
// EXPECT_NEAR would consume them). Amendment 2 work that lifts the
// cap-bust must overwrite these with captured values and re-enable
// the test.
// ---------------------------------------------------------------------------
inline constexpr double kFixture8NibbitsNormalExpectedHp = -1.0;
inline constexpr double kFixture8NibbitsNormalExpectedRounds = -1.0;

// DOUBLE-DISABLED tombstone — Wave-25 L.β Case B contingency PERSISTS
// despite L.α canonical-form. To re-enable post-Amendment 2, rename to
// single DISABLED_ prefix, restore the full solve body (template from
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
      << "(peak_rss_gb=22.37 elapsed_wall=255.3s) post wave-25/L.α "
      << "canonical-form remediation. Case B contingency PERSISTS. "
      << "Wave-25 L.α canonical-form pre-Zobrist swap (Q2-ADR-015 "
      << "Amendment 1) ships independently and is BIT-IDENTICAL for "
      << "Cultist/Louse/NibbitsWeak pins. Adapter dispatch for "
      << "NibbitsNormal STAYS LIVE; only the pin regression-lock is "
      << "deferred until Amendment 2 lands. Required directions: G2 "
      << "horizon reduction (25→15-20, floored by SmallSlimes survival) "
      << "or G3 LRU TT eviction at cap boundary.";

  // Unreachable — PLACEHOLDER constants intentionally never consumed.
  (void)kFixture8NibbitsNormalExpectedHp;
  (void)kFixture8NibbitsNormalExpectedRounds;
}

}  // namespace
