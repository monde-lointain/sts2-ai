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

// Drift defense: every MoveId in kAllMoveIds must round-trip via
// move_wire_id → try_move_id_from_wire_id back to the same value. Catches
// "added a MoveId, forgot to extend kMoveWireNames" at gtest time
// (constexpr static_assert catches it at compile time, but this test
// surfaces the failure with the specific MoveId).
TEST(MoveCalc, AllMoveIdsRoundTrip) {
  for (sts2::game::MoveId m : sts2::game::kAllMoveIds) {
    const std::string_view name = move_calc::move_wire_id(m);
    ASSERT_FALSE(name.empty())
        << "MoveId " << static_cast<int>(m) << " has empty wire name";
    sts2::game::MoveId out;
    ASSERT_TRUE(move_calc::try_move_id_from_wire_id(name, out))
        << "Wire name '" << name << "' did not round-trip for MoveId "
        << static_cast<int>(m);
    EXPECT_EQ(out, m) << "Round-trip mismatch: '" << name << "' → MoveId "
                      << static_cast<int>(out) << " (expected "
                      << static_cast<int>(m) << ")";
  }
}

}  // namespace
