// Tests for src/game/damage.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §7 (T-DMG-005..090).

#include <gtest/gtest.h>

#include <vector>

#include "sts2/game/damage.h"
#include "sts2/game/power.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::tests::helpers::MakePower;

using Power = sts2::game::Power;
using PowerKind = sts2::game::PowerKind;
using Vitals = sts2::game::Vitals;

constexpr PowerKind Weak = PowerKind::Weak;
constexpr PowerKind Strength = PowerKind::Strength;

// -------------------------------------------------------------------------
// 7.1  damage::compute_outgoing
// -------------------------------------------------------------------------

// T-DMG-005 — BP, EP — Plain: empty powers, base 6 returns 6 unchanged.
TEST(DamageComputeOutgoing, T_DMG_005_PlainBaseDamage) {
  const std::vector<Power> powers;
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 6);
}

// T-DMG-010 — EP, DF — Strength adds to base (def-use: powers::amount feeds
// sum).
TEST(DamageComputeOutgoing, T_DMG_010_StrengthAdds) {
  const std::vector<Power> powers = {MakePower(Strength, 2)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 8);
}

// T-DMG-015 — EP — Negative Strength subtracts, no clamp pre-Weak.
TEST(DamageComputeOutgoing, T_DMG_015_NegativeStrengthSubtracts) {
  const std::vector<Power> powers = {MakePower(Strength, -3)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 3);
}

// T-DMG-020 — BP — Weak applies 0.75 multiplier (D1 TRUE). int(6*0.75) = 4.
TEST(DamageComputeOutgoing, T_DMG_020_WeakAppliesMultiplier) {
  const std::vector<Power> powers = {MakePower(Weak, 1)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 4);
}

// T-DMG-025 — EP — Strength + Weak combined: int((6+4)*0.75) = int(7.5) = 7.
TEST(DamageComputeOutgoing, T_DMG_025_StrengthAndWeak) {
  const std::vector<Power> powers = {
      MakePower(Strength, 4),
      MakePower(Weak, 1),
  };
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 7);
}

// T-DMG-030 — BP, BV — Negative result clamped to 0 by D2 ternary.
TEST(DamageComputeOutgoing, T_DMG_030_NegativeResultClamped) {
  const std::vector<Power> powers = {MakePower(Strength, -10)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 0), 0);
}

// T-DMG-035 — EG — Weak amount=0 ignored: predicate is `> 0`, not `!= 0`.
// Locks contract that a corpse-on-arrival Weak does not debuff damage.
TEST(DamageComputeOutgoing, T_DMG_035_WeakZeroIgnored) {
  const std::vector<Power> powers = {MakePower(Weak, 0)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 6);
}

// T-DMG-040 — BV — base=0 with no powers returns 0.
TEST(DamageComputeOutgoing, T_DMG_040_ZeroBaseNoPowers) {
  const std::vector<Power> powers;
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 0), 0);
}

// T-DMG-045 — EG — Truncation direction: int(7*0.75) = int(5.25) = 5, not 6.
// Locks `static_cast<int>` truncates toward zero (not rounds).
TEST(DamageComputeOutgoing, T_DMG_045_TruncationTowardZero) {
  const std::vector<Power> powers = {MakePower(Weak, 1)};
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 7), 5);
}

// T-DMG-050 — EG — Multiple Weak entries: any Weak triggers debuff;
// `powers::amount` returns first match's amount; multiplier applied once.
// Locks first-match semantics shared with Powers::find.
TEST(DamageComputeOutgoing, T_DMG_050_MultipleWeakFirstConsulted) {
  const std::vector<Power> powers = {
      MakePower(Weak, 1),
      MakePower(Weak, 5),
  };
  EXPECT_EQ(sts2::damage::compute_outgoing(powers, 6), 4);
}

// -------------------------------------------------------------------------
// 7.2  damage::apply_to_defender
// -------------------------------------------------------------------------

// T-DMG-055 — BP, BV — Block fully absorbs: D1 TRUE (3 <= 5).
// Returns 0; block reduced by incoming; hp untouched.
TEST(DamageApplyToDefender, T_DMG_055_BlockFullyAbsorbs) {
  Vitals target{10, 10, 5, {}};
  const int returned = sts2::damage::apply_to_defender(target, 3);
  EXPECT_EQ(returned, 0);
  EXPECT_EQ(target.block, 2);
  EXPECT_EQ(target.hp, 10);
}

// T-DMG-060 — BV — Block exactly absorbs: D1 TRUE at boundary (5 <= 5).
TEST(DamageApplyToDefender, T_DMG_060_BlockExactlyAbsorbs) {
  Vitals target{10, 10, 5, {}};
  const int returned = sts2::damage::apply_to_defender(target, 5);
  EXPECT_EQ(returned, 0);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 10);
}

// T-DMG-065 — BP, BV — Bleed-through partial: D1 FALSE (5 > 3).
// Block fully consumed; remainder (2) hits hp.
TEST(DamageApplyToDefender, T_DMG_065_BleedThroughPartial) {
  Vitals target{10, 10, 3, {}};
  const int returned = sts2::damage::apply_to_defender(target, 5);
  EXPECT_EQ(returned, 2);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 8);
}

// T-DMG-070 — BP, BV — Overkill clamps to hp: D2 ternary FALSE branch.
// 9 incoming, no block, hp=4: hp_loss = target.hp = 4.
TEST(DamageApplyToDefender, T_DMG_070_OverkillClampsToHp) {
  Vitals target{4, 10, 0, {}};
  const int returned = sts2::damage::apply_to_defender(target, 9);
  EXPECT_EQ(returned, 4);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 0);
}

// T-DMG-075 — BV — Overkill with block: 9 - 2 = 7 > hp=4 → hp_loss = 4.
TEST(DamageApplyToDefender, T_DMG_075_OverkillWithBlock) {
  Vitals target{4, 10, 2, {}};
  const int returned = sts2::damage::apply_to_defender(target, 9);
  EXPECT_EQ(returned, 4);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 0);
}

// T-DMG-080 — BV — Zero incoming: D1 TRUE (0 <= 0); block unchanged; hp
// unchanged.
TEST(DamageApplyToDefender, T_DMG_080_ZeroIncoming) {
  Vitals target{10, 10, 0, {}};
  const int returned = sts2::damage::apply_to_defender(target, 0);
  EXPECT_EQ(returned, 0);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 10);
}

// T-DMG-085 — EG — Negative incoming: D1 TRUE (-3 <= 0); `block -= -3` adds 3.
// Pin this surprising arithmetic — locks the contract that callers must never
// pass negatives to apply_to_defender.
TEST(DamageApplyToDefender, T_DMG_085_NegativeIncomingAddsBlock) {
  Vitals target{10, 10, 0, {}};
  const int returned = sts2::damage::apply_to_defender(target, -3);
  EXPECT_EQ(returned, 0);
  EXPECT_EQ(target.block, 3);
  EXPECT_EQ(target.hp, 10);
}

// T-DMG-090 — EG — Lethal-equals-hp: D1 FALSE (4 > 0); D2 ternary FALSE
// at equality (`4 < 4` is FALSE) → uses target.hp branch. Returns 4; hp=0.
TEST(DamageApplyToDefender, T_DMG_090_LethalEqualsHp) {
  Vitals target{4, 10, 0, {}};
  const int returned = sts2::damage::apply_to_defender(target, 4);
  EXPECT_EQ(returned, 4);
  EXPECT_EQ(target.block, 0);
  EXPECT_EQ(target.hp, 0);
}

}  // namespace
