// TOMBSTONE — SmallSlimes DEPRECATED from oracle supported encounters.
//
// Per Q2-ADR-013 Amendment 4 §SmallSlimes-deprecation (2026-05-18):
// Method (h) layered fix (Slimed cap + LRU eviction + CompactState
// compression) resolved kCapExceeded but exposed an unbounded wall-clock
// failure. SmallSlimes removed from adapter dispatch; projection module
// deleted. This file is retained for discoverability.
//
// make_small_slimes_synthetic_combat is defined in test_helpers.h and is
// NOT removed here — it may serve future encounter tests. No action needed.

#include <gtest/gtest.h>

namespace {

// PLACEHOLDER constants — never reached (GTEST_SKIP fires first).
// Preserved per task spec to make the deprecation explicit.
constexpr double kSmallSlimesSyntheticExpectedHp =
    -1.0;  // PLACEHOLDER; never reached due to deprecation tombstone
constexpr double kSmallSlimesSyntheticExpectedRounds =
    -1.0;  // PLACEHOLDER; never reached due to deprecation tombstone

TEST(SmallSlimesSearchPins,
     DISABLED_SmallSlimesSyntheticVariantA_PinnedAgreement) {
  GTEST_SKIP()
      << "SmallSlimes DEPRECATED from oracle supported encounters per "
         "Q2-ADR-013 "
         "Amendment 4 §SmallSlimes-deprecation (2026-05-18). Method (h) "
         "layered "
         "fix (Slimed cap + LRU eviction + CompactState compression) resolved "
         "the "
         "kCapExceeded path but exposed an unbounded wall-clock failure (40m+ "
         "at "
         "19.2 GB peak RSS, LRU thrashing). State-space breadth of the "
         "all-Defend "
         "branch remains tractability-blocking even with cap=8. A different "
         "Phase-1 encounter will be selected for the next port wave. This "
         "tombstone test preserves discoverability for future revisits.";
}

}  // namespace
