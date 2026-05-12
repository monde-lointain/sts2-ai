// Tests for include/sts2/game/move_calc.h.
//
// Covers the small canonical primitives shared by the production engine and
// the AI transition simulator: enemy intent advancement (next_move) and the
// Ritual tick decision (ritual_should_grant_strength).

#include <gtest/gtest.h>

#include "sts2/game/move_calc.h"

namespace {

using sts2::game::MoveId;
namespace move_calc = sts2::game::move_calc;

TEST(MoveCalc, NextMove_IncantationAdvancesToDarkStrike) {
  EXPECT_EQ(move_calc::next_move(MoveId::kIncantation), MoveId::kDarkStrike);
}

TEST(MoveCalc, NextMove_DarkStrikeIsStable) {
  EXPECT_EQ(move_calc::next_move(MoveId::kDarkStrike), MoveId::kDarkStrike);
}

TEST(MoveCalcCatalog, WireIdRoundTrips) {
  EXPECT_EQ(move_calc::move_wire_id(MoveId::kIncantation), "INCANTATION_MOVE");
  EXPECT_EQ(move_calc::move_wire_id(MoveId::kDarkStrike), "DARK_STRIKE_MOVE");
  EXPECT_EQ(move_calc::move_id_from_wire_id("INCANTATION_MOVE"),
            MoveId::kIncantation);
  EXPECT_EQ(move_calc::move_id_from_wire_id("DARK_STRIKE_MOVE"),
            MoveId::kDarkStrike);
}

TEST(MoveCalcCatalog, UnknownWireIdUsesCurrentProjectionFallback) {
  EXPECT_EQ(move_calc::move_id_from_wire_id("UNKNOWN_MOVE"),
            MoveId::kIncantation);
}

TEST(MoveCalc, RitualShouldGrantStrength_JustAppliedSkipsAndClears) {
  bool flag = true;
  EXPECT_FALSE(move_calc::ritual_should_grant_strength(flag));
  EXPECT_FALSE(flag);
}

TEST(MoveCalc,
     RitualShouldGrantStrength_NotJustAppliedGrantsAndPreservesFalse) {
  bool flag = false;
  EXPECT_TRUE(move_calc::ritual_should_grant_strength(flag));
  EXPECT_FALSE(flag);
}

}  // namespace
