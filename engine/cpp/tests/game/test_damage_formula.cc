// Tests for sts2::damage::compute_outgoing_attack and
// sts2::damage::compute_outgoing_block (wave-16 additions to damage.h/cc).
//
// compute_outgoing_attack: thin wrapper over damage_calc::compute_outgoing.
// compute_outgoing_block: STS canonical block formula.
//   floor((base + dex) * (frail && powered ? 0.75 : 1.0))
//   Integer rounding: (v * 3) / 4.

#include <gtest/gtest.h>

#include "sts2/game/damage.h"

namespace {

using sts2::damage::compute_outgoing_attack;
using sts2::damage::compute_outgoing_block;

// -------------------------------------------------------------------------
// compute_outgoing_attack
// Delegates to damage_calc::compute_outgoing(base, strength, weak).
// -------------------------------------------------------------------------

// Cultist CalcifiedCultist DarkStrike base=9, no buffs → 9
TEST(DamageFormula, Attack_PlainBase_NoBuffs) {
  EXPECT_EQ(compute_outgoing_attack(6, 0, 0), 6);
}

// Strength +3: 6 + 3 = 9
TEST(DamageFormula, Attack_StrengthAdds) {
  EXPECT_EQ(compute_outgoing_attack(6, 3, 0), 9);
}

// Weak: (6 * 3) / 4 = 4 (floor of 4.5)
TEST(DamageFormula, Attack_WeakReduces) {
  EXPECT_EQ(compute_outgoing_attack(6, 0, 1), 4);
}

// Strength + Weak: (6+3)*3/4 = 27/4 = 6
TEST(DamageFormula, Attack_StrengthAndWeak) {
  EXPECT_EQ(compute_outgoing_attack(6, 3, 1), 6);
}

// Zero base with Strength: 0 + 5 = 5
TEST(DamageFormula, Attack_ZeroBaseWithStrength) {
  EXPECT_EQ(compute_outgoing_attack(0, 5, 0), 5);
}

// Zero base with Weak: (0*3)/4 = 0
TEST(DamageFormula, Attack_ZeroBaseWithWeak) {
  EXPECT_EQ(compute_outgoing_attack(0, 0, 1), 0);
}

// Boundary: base=1, Weak → (1*3)/4 = 0 (floor)
TEST(DamageFormula, Attack_WeakFloorToZero) {
  EXPECT_EQ(compute_outgoing_attack(1, 0, 1), 0);
}

// -------------------------------------------------------------------------
// compute_outgoing_block
// effective_block = floor((base + dex) * (frail && powered ? 0.75 : 1.0))
// -------------------------------------------------------------------------

// Plain block, no frail: 5
TEST(DamageFormula, Block_Plain_NoFrail) {
  EXPECT_EQ(compute_outgoing_block(5, 0, false, true), 5);
}

// Frail + powered source: (5 * 3) / 4 = 3 (floor of 3.75)
TEST(DamageFormula, Block_FrailPowered) {
  EXPECT_EQ(compute_outgoing_block(5, 0, true, true), 3);
}

// Frail but unpowered source: frail tax NOT applied per STS canonical
TEST(DamageFormula, Block_FrailUnpowered_NoTax) {
  EXPECT_EQ(compute_outgoing_block(5, 0, true, false), 5);
}

// Dex +2 + Frail + powered: (5+2)*3/4 = 21/4 = 5
TEST(DamageFormula, Block_DexPlusFrailPowered) {
  EXPECT_EQ(compute_outgoing_block(5, 2, true, true), 5);
}

// Zero base, no frail: 0
TEST(DamageFormula, Block_ZeroBase_NoFrail) {
  EXPECT_EQ(compute_outgoing_block(0, 0, false, true), 0);
}

// Dex alone, no frail: base 3 + dex 4 = 7
TEST(DamageFormula, Block_DexAdds) {
  EXPECT_EQ(compute_outgoing_block(3, 4, false, true), 7);
}

}  // namespace
