// Tests for src/game/powers.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §6 (T-PWR-005..150).
//
// No pinned-value caveats: all expected vectors are derived directly from
// the spec's CFG decisions, not toolchain-dependent output.

#include <gtest/gtest.h>

#include <vector>

#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::tests::helpers::expect_powers_eq;
using sts2::tests::helpers::make_power;

using Power = sts2::game::Power;
using PowerKind = sts2::game::PowerKind;

constexpr PowerKind kWeak = PowerKind::kWeak;
constexpr PowerKind kStrength = PowerKind::kStrength;
constexpr PowerKind kRitual = PowerKind::kRitual;

// -------------------------------------------------------------------------
// 6.1  powers::find — mutable overload
// -------------------------------------------------------------------------

// T-PWR-005 — BP, EP, BV — Empty container returns nullptr (D1 FALSE on entry).
TEST(PowersFind, T_PWR_005_EmptyReturnsNull) {
  std::vector<Power> v;
  EXPECT_EQ(sts2::powers::find(v, kWeak), nullptr);
}

// T-PWR-010 — BP, EP — Match at index 0 (D1 TRUE; D2 TRUE on iter 1).
TEST(PowersFind, T_PWR_010_MatchAtFirstIndex) {
  std::vector<Power> v = {make_power(kWeak, 2)};
  Power* p = sts2::powers::find(v, kWeak);
  ASSERT_NE(p, nullptr);
  EXPECT_EQ(p->kind, kWeak);
  EXPECT_EQ(p->amount, 2);
}

// T-PWR-015 — BP, EP — Match at later index (D2 FALSE then TRUE).
TEST(PowersFind, T_PWR_015_MatchAtLaterIndex) {
  std::vector<Power> v = {make_power(kStrength, 1), make_power(kWeak, 3)};
  Power* p = sts2::powers::find(v, kWeak);
  ASSERT_NE(p, nullptr);
  EXPECT_EQ(p, &v[1]);
  EXPECT_EQ(p->kind, kWeak);
  EXPECT_EQ(p->amount, 3);
}

// T-PWR-020 — BP, EP — No match in non-empty vector returns nullptr.
TEST(PowersFind, T_PWR_020_NoMatchReturnsNull) {
  std::vector<Power> v = {make_power(kStrength, 1)};
  EXPECT_EQ(sts2::powers::find(v, kWeak), nullptr);
}

// T-PWR-025 — EG — First-match semantics with duplicates.
// Locks the linear-search "first hit" contract that powers::apply depends on.
TEST(PowersFind, T_PWR_025_FirstMatchWithDuplicates) {
  std::vector<Power> v = {make_power(kWeak, 1), make_power(kWeak, 2)};
  Power* p = sts2::powers::find(v, kWeak);
  ASSERT_NE(p, nullptr);
  EXPECT_EQ(p, v.data());
  EXPECT_EQ(p->amount, 1);
}

// T-PWR-030 — DF — Mutability through returned pointer.
// Def-use chain: find → caller assigns through pointer → underlying vector
// mutated.
TEST(PowersFind, T_PWR_030_MutationThroughPointer) {
  std::vector<Power> v = {make_power(kWeak, 2)};
  Power* p = sts2::powers::find(v, kWeak);
  ASSERT_NE(p, nullptr);
  p->amount = 99;
  EXPECT_EQ(v[0].amount, 99);
}

// -------------------------------------------------------------------------
// 6.1  powers::find — const overload
// -------------------------------------------------------------------------

// T-PWR-035 — BP — Empty (const) returns nullptr.
TEST(PowersFindConst, T_PWR_035_EmptyReturnsNull) {
  const std::vector<Power> v;
  EXPECT_EQ(sts2::powers::find(v, kWeak), nullptr);
}

// T-PWR-040 — BP — Match (const). Pointer reflects element kind/amount.
TEST(PowersFindConst, T_PWR_040_MatchReflectsElement) {
  const std::vector<Power> v = {make_power(kStrength, 5)};
  const Power* p = sts2::powers::find(v, kStrength);
  ASSERT_NE(p, nullptr);
  EXPECT_EQ(p->kind, kStrength);
  EXPECT_EQ(p->amount, 5);
}

// T-PWR-045 — BP — No-match (const) returns nullptr.
TEST(PowersFindConst, T_PWR_045_NoMatchReturnsNull) {
  const std::vector<Power> v = {make_power(kStrength, 1)};
  EXPECT_EQ(sts2::powers::find(v, kWeak), nullptr);
}

// -------------------------------------------------------------------------
// 6.2  powers::amount
// -------------------------------------------------------------------------

// T-PWR-050 — BP — Not present → 0 (ternary FALSE branch).
TEST(PowersAmount, T_PWR_050_NotPresentReturnsZero) {
  const std::vector<Power> v;
  EXPECT_EQ(sts2::powers::amount(v, kStrength), 0);
}

// T-PWR-055 — BP, EP — Present → returns its amount (ternary TRUE branch).
TEST(PowersAmount, T_PWR_055_PresentReturnsAmount) {
  const std::vector<Power> v = {make_power(kStrength, 4)};
  EXPECT_EQ(sts2::powers::amount(v, kStrength), 4);
}

// T-PWR-060 — BV — Negative amount returned literally (no clamping at this
// layer).
TEST(PowersAmount, T_PWR_060_NegativeReturnedLiterally) {
  const std::vector<Power> v = {make_power(kStrength, -2)};
  EXPECT_EQ(sts2::powers::amount(v, kStrength), -2);
}

// -------------------------------------------------------------------------
// 6.3  powers::apply
// -------------------------------------------------------------------------

// T-PWR-065 — BP, EP — New non-Ritual power → push_back. D1 FALSE; init ternary
// FALSE.
TEST(PowersApply, T_PWR_065_NewNonRitualPushBack) {
  std::vector<Power> v;
  sts2::powers::apply(v, kWeak, 2);
  expect_powers_eq(v, {make_power(kWeak, 2, false)});
}

// T-PWR-070 — BP, EP — New Ritual power → push_back with just_applied=true.
// D1 FALSE; init ternary TRUE.
TEST(PowersApply, T_PWR_070_NewRitualMarksJustApplied) {
  std::vector<Power> v;
  sts2::powers::apply(v, kRitual, 3);
  expect_powers_eq(v, {make_power(kRitual, 3, true)});
}

// T-PWR-075 — BP — Existing non-Ritual → amount accumulates and `just_applied`
// preserved unchanged for non-Ritual. Seeding `just_applied=true` locks the
// invariant against a buggy `apply` that always writes `just_applied=true`.
// D1 TRUE; D2 FALSE.
// Covers: amount accumulation and `just_applied` preserved unchanged for
// non-Ritual.
TEST(PowersApply, T_PWR_075_ExistingNonRitualAccumulates) {
  std::vector<Power> v = {make_power(kWeak, 2, true)};
  sts2::powers::apply(v, kWeak, 1);
  expect_powers_eq(v, {make_power(kWeak, 3, true)});
}

// T-PWR-080 — BP — Existing Ritual → amount accumulates and just_applied=true.
// D1 TRUE; D2 TRUE.
TEST(PowersApply, T_PWR_080_ExistingRitualSetsJustApplied) {
  std::vector<Power> v = {make_power(kRitual, 2, false)};
  sts2::powers::apply(v, kRitual, 1);
  expect_powers_eq(v, {make_power(kRitual, 3, true)});
}

// T-PWR-085 — EG, BV — Apply zero amount still creates the entry.
// Locks contract that apply(_, _, 0) creates a "corpse-on-arrival" Weak.
TEST(PowersApply, T_PWR_085_ApplyZeroCreatesEntry) {
  std::vector<Power> v;
  sts2::powers::apply(v, kWeak, 0);
  expect_powers_eq(v, {make_power(kWeak, 0, false)});
}

// T-PWR-090 — EG — Apply negative amount accumulates arithmetically.
TEST(PowersApply, T_PWR_090_ApplyNegativeAccumulates) {
  std::vector<Power> v = {make_power(kStrength, 2, false)};
  sts2::powers::apply(v, kStrength, -3);
  expect_powers_eq(v, {make_power(kStrength, -1, false)});
}

// -------------------------------------------------------------------------
// 6.4  powers::tick_at_turn_end
// -------------------------------------------------------------------------

// T-PWR-100 — BP, EP — Empty no-op. D1 FALSE; D3 immediate exit.
TEST(PowersTick, T_PWR_100_EmptyNoOp) {
  std::vector<Power> v;
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {});
}

// T-PWR-105 — BP — Ritual just-applied clears flag; no Strength gain.
// D1 TRUE; D2 TRUE.
TEST(PowersTick, T_PWR_105_RitualJustAppliedClears) {
  std::vector<Power> v = {make_power(kRitual, 2, true)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {make_power(kRitual, 2, false)});
}

// T-PWR-110 — BP — Ritual normal → new Strength entry appended.
// D1 TRUE; D2 FALSE; apply push_back path.
TEST(PowersTick, T_PWR_110_RitualNormalAddsNewStrength) {
  std::vector<Power> v = {make_power(kRitual, 2, false)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {
                          make_power(kRitual, 2, false),
                          make_power(kStrength, 2, false),
                      });
}

// T-PWR-115 — BP — Ritual normal accumulates into existing Strength.
// D1 TRUE; D2 FALSE; apply D1 TRUE branch.
TEST(PowersTick, T_PWR_115_RitualNormalAccumulatesStrength) {
  std::vector<Power> v = {
      make_power(kRitual, 3, false),
      make_power(kStrength, 1, false),
  };
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {
                          make_power(kRitual, 3, false),
                          make_power(kStrength, 4, false),
                      });
}

// T-PWR-120 — BP, EP — Weak amount > 1 ticks down. D5 FALSE; ++it branch.
TEST(PowersTick, T_PWR_120_WeakGreaterThanOneTicksDown) {
  std::vector<Power> v = {make_power(kWeak, 3, false)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {make_power(kWeak, 2, false)});
}

// T-PWR-125 — BP, BV — Weak amount == 1 erases. D5 TRUE → erase + continue.
TEST(PowersTick, T_PWR_125_WeakOneErases) {
  std::vector<Power> v = {make_power(kWeak, 1, false)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {});
}

// T-PWR-130 — EG — Weak amount == 0 corpse erases.
// Tick decrements first to -1, then erases since amount <= 0.
TEST(PowersTick, T_PWR_130_WeakZeroCorpseErases) {
  std::vector<Power> v = {make_power(kWeak, 0, false)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {});
}

// T-PWR-135 — EG — Weak amount == -1 corpse erases (decrement to -2 then
// erase). Locks the `<= 0` comparison includes negatives.
TEST(PowersTick, T_PWR_135_WeakNegativeCorpseErases) {
  std::vector<Power> v = {make_power(kWeak, -1, false)};
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {});
}

// T-PWR-140 — DF — Mixed list, Ritual then Weak ordering preserved.
// Ritual handler first (Strength accumulates +1 from Ritual.amount=1);
// then Weak loop ticks 2→1. Order preserved (handler doesn't move entries).
TEST(PowersTick, T_PWR_140_MixedListOrderingPreserved) {
  std::vector<Power> v = {
      make_power(kStrength, 2, false),
      make_power(kWeak, 2, false),
      make_power(kRitual, 1, false),
  };
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {
                          make_power(kStrength, 3, false),
                          make_power(kWeak, 1, false),
                          make_power(kRitual, 1, false),
                      });
}

// T-PWR-145 — EG — Two Weaks: first ticks to 0 and erases mid-iteration,
// second still processed and decremented. Verifies iterator safety post-erase.
TEST(PowersTick, T_PWR_145_TwoWeaksFirstErasesIteratorSafe) {
  std::vector<Power> v = {
      make_power(kWeak, 1, false),
      make_power(kWeak, 3, false),
  };
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {make_power(kWeak, 2, false)});
}

// T-PWR-150 — EG — Weak first, Strength second; Strength preserved.
// D4 FALSE branch (Strength is not ticked).
TEST(PowersTick, T_PWR_150_WeakFirstStrengthSecondPreserved) {
  std::vector<Power> v = {
      make_power(kWeak, 2, false),
      make_power(kStrength, 4, false),
  };
  sts2::powers::tick_at_turn_end(v);
  expect_powers_eq(v, {
                          make_power(kWeak, 1, false),
                          make_power(kStrength, 4, false),
                      });
}

}  // namespace
