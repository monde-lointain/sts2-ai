// Tests for include/sts2/game/turn_calc.h.
//
// Covers the small canonical primitives shared by the production engine and
// the AI transition simulator: block-reset gate, starting energy, and hand
// draw size.

#include <gtest/gtest.h>

#include "sts2/game/turn_calc.h"

namespace {

namespace turn_calc = sts2::game::turn_calc;

// round_resets_block ---------------------------------------------------

TEST(TurnCalc, RoundResetsBlock_Round0_False) {
  EXPECT_FALSE(turn_calc::round_resets_block(0));
}

TEST(TurnCalc, RoundResetsBlock_Round1_False) {
  EXPECT_FALSE(turn_calc::round_resets_block(1));
}

TEST(TurnCalc, RoundResetsBlock_Round2_True) {
  EXPECT_TRUE(turn_calc::round_resets_block(2));
}

TEST(TurnCalc, RoundResetsBlock_LargeRound_True) {
  EXPECT_TRUE(turn_calc::round_resets_block(100));
}

// starting_energy ------------------------------------------------------

TEST(TurnCalc, StartingEnergy_MatchesCanonicalConstant) {
  EXPECT_EQ(turn_calc::starting_energy(), turn_calc::kPlayerStartingEnergy);
}

// hand_draw_size -------------------------------------------------------

TEST(TurnCalc, HandDrawSize_Round1_SevenCards) {
  EXPECT_EQ(turn_calc::hand_draw_size(1), 7);
}

TEST(TurnCalc, HandDrawSize_Round2_FiveCards) {
  EXPECT_EQ(turn_calc::hand_draw_size(2), 5);
}

TEST(TurnCalc, HandDrawSize_Round3_FiveCards) {
  EXPECT_EQ(turn_calc::hand_draw_size(3), 5);
}

TEST(TurnCalc, HandDrawSize_LargeRound_FiveCards) {
  EXPECT_EQ(turn_calc::hand_draw_size(100), 5);
}

}  // namespace
