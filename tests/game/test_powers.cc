// Tests for src/game/powers.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §6 (T-PWR-005..150).
//
// No pinned-value caveats: all expected vectors are derived directly from
// the spec's CFG decisions, not toolchain-dependent output.

#include <vector>

#include <gtest/gtest.h>

#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::tests::helpers::ExpectPowersEq;
using sts2::tests::helpers::MakePower;

constexpr PowerKind Weak     = PowerKind::Weak;
constexpr PowerKind Strength = PowerKind::Strength;
constexpr PowerKind Ritual   = PowerKind::Ritual;

// -------------------------------------------------------------------------
// 6.1  powers::find — mutable overload
// -------------------------------------------------------------------------

// T-PWR-005 — BP, EP, BV — Empty container returns nullptr (D1 FALSE on entry).
TEST(PowersFind, T_PWR_005_EmptyReturnsNull) {
    std::vector<Power> v;
    EXPECT_EQ(powers::find(v, Weak), nullptr);
}

// T-PWR-010 — BP, EP — Match at index 0 (D1 TRUE; D2 TRUE on iter 1).
TEST(PowersFind, T_PWR_010_MatchAtFirstIndex) {
    std::vector<Power> v = { MakePower(Weak, 2) };
    Power* p = powers::find(v, Weak);
    ASSERT_NE(p, nullptr);
    EXPECT_EQ(p->kind, Weak);
    EXPECT_EQ(p->amount, 2);
}

// T-PWR-015 — BP, EP — Match at later index (D2 FALSE then TRUE).
TEST(PowersFind, T_PWR_015_MatchAtLaterIndex) {
    std::vector<Power> v = { MakePower(Strength, 1), MakePower(Weak, 3) };
    Power* p = powers::find(v, Weak);
    ASSERT_NE(p, nullptr);
    EXPECT_EQ(p, &v[1]);
    EXPECT_EQ(p->kind, Weak);
    EXPECT_EQ(p->amount, 3);
}

// T-PWR-020 — BP, EP — No match in non-empty vector returns nullptr.
TEST(PowersFind, T_PWR_020_NoMatchReturnsNull) {
    std::vector<Power> v = { MakePower(Strength, 1) };
    EXPECT_EQ(powers::find(v, Weak), nullptr);
}

// T-PWR-025 — EG — First-match semantics with duplicates.
// Locks the linear-search "first hit" contract that powers::apply depends on.
TEST(PowersFind, T_PWR_025_FirstMatchWithDuplicates) {
    std::vector<Power> v = { MakePower(Weak, 1), MakePower(Weak, 2) };
    Power* p = powers::find(v, Weak);
    ASSERT_NE(p, nullptr);
    EXPECT_EQ(p, &v[0]);
    EXPECT_EQ(p->amount, 1);
}

// T-PWR-030 — DF — Mutability through returned pointer.
// Def-use chain: find → caller assigns through pointer → underlying vector mutated.
TEST(PowersFind, T_PWR_030_MutationThroughPointer) {
    std::vector<Power> v = { MakePower(Weak, 2) };
    Power* p = powers::find(v, Weak);
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
    EXPECT_EQ(powers::find(v, Weak), nullptr);
}

// T-PWR-040 — BP — Match (const). Pointer reflects element kind/amount.
TEST(PowersFindConst, T_PWR_040_MatchReflectsElement) {
    const std::vector<Power> v = { MakePower(Strength, 5) };
    const Power* p = powers::find(v, Strength);
    ASSERT_NE(p, nullptr);
    EXPECT_EQ(p->kind, Strength);
    EXPECT_EQ(p->amount, 5);
}

// T-PWR-045 — BP — No-match (const) returns nullptr.
TEST(PowersFindConst, T_PWR_045_NoMatchReturnsNull) {
    const std::vector<Power> v = { MakePower(Strength, 1) };
    EXPECT_EQ(powers::find(v, Weak), nullptr);
}

// -------------------------------------------------------------------------
// 6.2  powers::amount
// -------------------------------------------------------------------------

// T-PWR-050 — BP — Not present → 0 (ternary FALSE branch).
TEST(PowersAmount, T_PWR_050_NotPresentReturnsZero) {
    const std::vector<Power> v;
    EXPECT_EQ(powers::amount(v, Strength), 0);
}

// T-PWR-055 — BP, EP — Present → returns its amount (ternary TRUE branch).
TEST(PowersAmount, T_PWR_055_PresentReturnsAmount) {
    const std::vector<Power> v = { MakePower(Strength, 4) };
    EXPECT_EQ(powers::amount(v, Strength), 4);
}

// T-PWR-060 — BV — Negative amount returned literally (no clamping at this layer).
TEST(PowersAmount, T_PWR_060_NegativeReturnedLiterally) {
    const std::vector<Power> v = { MakePower(Strength, -2) };
    EXPECT_EQ(powers::amount(v, Strength), -2);
}

// -------------------------------------------------------------------------
// 6.3  powers::apply
// -------------------------------------------------------------------------

// T-PWR-065 — BP, EP — New non-Ritual power → push_back. D1 FALSE; init ternary FALSE.
TEST(PowersApply, T_PWR_065_NewNonRitualPushBack) {
    std::vector<Power> v;
    powers::apply(v, Weak, 2);
    ExpectPowersEq(v, { MakePower(Weak, 2, false) });
}

// T-PWR-070 — BP, EP — New Ritual power → push_back with just_applied=true.
// D1 FALSE; init ternary TRUE.
TEST(PowersApply, T_PWR_070_NewRitualMarksJustApplied) {
    std::vector<Power> v;
    powers::apply(v, Ritual, 3);
    ExpectPowersEq(v, { MakePower(Ritual, 3, true) });
}

// T-PWR-075 — BP — Existing non-Ritual → amount accumulates and `just_applied`
// preserved unchanged for non-Ritual. Seeding `just_applied=true` locks the
// invariant against a buggy `apply` that always writes `just_applied=true`.
// D1 TRUE; D2 FALSE.
// Covers: amount accumulation and `just_applied` preserved unchanged for non-Ritual.
TEST(PowersApply, T_PWR_075_ExistingNonRitualAccumulates) {
    std::vector<Power> v = { MakePower(Weak, 2, true) };
    powers::apply(v, Weak, 1);
    ExpectPowersEq(v, { MakePower(Weak, 3, true) });
}

// T-PWR-080 — BP — Existing Ritual → amount accumulates and just_applied=true.
// D1 TRUE; D2 TRUE.
TEST(PowersApply, T_PWR_080_ExistingRitualSetsJustApplied) {
    std::vector<Power> v = { MakePower(Ritual, 2, false) };
    powers::apply(v, Ritual, 1);
    ExpectPowersEq(v, { MakePower(Ritual, 3, true) });
}

// T-PWR-085 — EG, BV — Apply zero amount still creates the entry.
// Locks contract that apply(_, _, 0) creates a "corpse-on-arrival" Weak.
TEST(PowersApply, T_PWR_085_ApplyZeroCreatesEntry) {
    std::vector<Power> v;
    powers::apply(v, Weak, 0);
    ExpectPowersEq(v, { MakePower(Weak, 0, false) });
}

// T-PWR-090 — EG — Apply negative amount accumulates arithmetically.
TEST(PowersApply, T_PWR_090_ApplyNegativeAccumulates) {
    std::vector<Power> v = { MakePower(Strength, 2, false) };
    powers::apply(v, Strength, -3);
    ExpectPowersEq(v, { MakePower(Strength, -1, false) });
}

// -------------------------------------------------------------------------
// 6.4  powers::tick_at_turn_end
// -------------------------------------------------------------------------

// T-PWR-100 — BP, EP — Empty no-op. D1 FALSE; D3 immediate exit.
TEST(PowersTick, T_PWR_100_EmptyNoOp) {
    std::vector<Power> v;
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {});
}

// T-PWR-105 — BP — Ritual just-applied clears flag; no Strength gain.
// D1 TRUE; D2 TRUE.
TEST(PowersTick, T_PWR_105_RitualJustAppliedClears) {
    std::vector<Power> v = { MakePower(Ritual, 2, true) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, { MakePower(Ritual, 2, false) });
}

// T-PWR-110 — BP — Ritual normal → new Strength entry appended.
// D1 TRUE; D2 FALSE; apply push_back path.
TEST(PowersTick, T_PWR_110_RitualNormalAddsNewStrength) {
    std::vector<Power> v = { MakePower(Ritual, 2, false) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {
        MakePower(Ritual, 2, false),
        MakePower(Strength, 2, false),
    });
}

// T-PWR-115 — BP — Ritual normal accumulates into existing Strength.
// D1 TRUE; D2 FALSE; apply D1 TRUE branch.
TEST(PowersTick, T_PWR_115_RitualNormalAccumulatesStrength) {
    std::vector<Power> v = {
        MakePower(Ritual, 3, false),
        MakePower(Strength, 1, false),
    };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {
        MakePower(Ritual, 3, false),
        MakePower(Strength, 4, false),
    });
}

// T-PWR-120 — BP, EP — Weak amount > 1 ticks down. D5 FALSE; ++it branch.
TEST(PowersTick, T_PWR_120_WeakGreaterThanOneTicksDown) {
    std::vector<Power> v = { MakePower(Weak, 3, false) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, { MakePower(Weak, 2, false) });
}

// T-PWR-125 — BP, BV — Weak amount == 1 erases. D5 TRUE → erase + continue.
TEST(PowersTick, T_PWR_125_WeakOneErases) {
    std::vector<Power> v = { MakePower(Weak, 1, false) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {});
}

// T-PWR-130 — EG — Weak amount == 0 corpse erases.
// Tick decrements first to -1, then erases since amount <= 0.
TEST(PowersTick, T_PWR_130_WeakZeroCorpseErases) {
    std::vector<Power> v = { MakePower(Weak, 0, false) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {});
}

// T-PWR-135 — EG — Weak amount == -1 corpse erases (decrement to -2 then erase).
// Locks the `<= 0` comparison includes negatives.
TEST(PowersTick, T_PWR_135_WeakNegativeCorpseErases) {
    std::vector<Power> v = { MakePower(Weak, -1, false) };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {});
}

// T-PWR-140 — DF — Mixed list, Ritual then Weak ordering preserved.
// Ritual handler first (Strength accumulates +1 from Ritual.amount=1);
// then Weak loop ticks 2→1. Order preserved (handler doesn't move entries).
TEST(PowersTick, T_PWR_140_MixedListOrderingPreserved) {
    std::vector<Power> v = {
        MakePower(Strength, 2, false),
        MakePower(Weak, 2, false),
        MakePower(Ritual, 1, false),
    };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {
        MakePower(Strength, 3, false),
        MakePower(Weak, 1, false),
        MakePower(Ritual, 1, false),
    });
}

// T-PWR-145 — EG — Two Weaks: first ticks to 0 and erases mid-iteration,
// second still processed and decremented. Verifies iterator safety post-erase.
TEST(PowersTick, T_PWR_145_TwoWeaksFirstErasesIteratorSafe) {
    std::vector<Power> v = {
        MakePower(Weak, 1, false),
        MakePower(Weak, 3, false),
    };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, { MakePower(Weak, 2, false) });
}

// T-PWR-150 — EG — Weak first, Strength second; Strength preserved.
// D4 FALSE branch (Strength is not ticked).
TEST(PowersTick, T_PWR_150_WeakFirstStrengthSecondPreserved) {
    std::vector<Power> v = {
        MakePower(Weak, 2, false),
        MakePower(Strength, 4, false),
    };
    powers::tick_at_turn_end(v);
    ExpectPowersEq(v, {
        MakePower(Weak, 1, false),
        MakePower(Strength, 4, false),
    });
}

}  // namespace
