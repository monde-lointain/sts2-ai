// Tests for include/sts2/game/turn_calc.h.
//
// Covers the small canonical primitives shared by the production engine and
// the AI transition simulator: block-reset gate, starting energy, and hand
// draw size.

#include <gtest/gtest.h>

#include <cstddef>
#include <string>
#include <vector>

#include "sts2/game/turn_calc.h"
#include "sts2/game/turn_flow.h"

namespace {

namespace turn_calc = sts2::game::turn_calc;
namespace turn_flow = sts2::game::turn_flow;

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

struct RecordingEndTurnOps {
  std::vector<std::string> calls;
  bool alive[3] = {true, false, true};
  bool stop_after_enemy_0 = false;
  bool stopped = false;
  int current_round = 1;

  void end_player_turn() { calls.emplace_back("end_player_turn"); }

  [[nodiscard]] static std::size_t enemy_count() { return 3; }

  [[nodiscard]] bool enemy_alive(std::size_t slot) const { return alive[slot]; }

  void reset_enemy_block(std::size_t slot) {
    calls.push_back("reset_enemy_block:" + std::to_string(slot));
  }

  void enemy_act(std::size_t slot) {
    calls.push_back("enemy_act:" + std::to_string(slot));
    if (slot == 0 && stop_after_enemy_0) {
      stopped = true;
    }
  }

  [[nodiscard]] bool terminal() const { return stopped; }

  void tick_enemy_powers(std::size_t slot) {
    calls.push_back("tick_enemy_powers:" + std::to_string(slot));
  }

  void increment_round() {
    calls.emplace_back("increment_round");
    ++current_round;
  }

  [[nodiscard]] int round() const { return current_round; }

  void roll_enemy_next_move(std::size_t slot) {
    calls.push_back("roll_enemy_next_move:" + std::to_string(slot));
  }

  void reset_player_block() { calls.emplace_back("reset_player_block"); }

  void refill_player_energy(int amount) {
    calls.push_back("refill_player_energy:" + std::to_string(amount));
  }
};

TEST(TurnFlow, ResolveEndTurnPreDraw_RecordsDeterministicOrder) {
  RecordingEndTurnOps ops;

  turn_flow::resolve_end_turn_pre_draw(ops);

  EXPECT_EQ(ops.calls, (std::vector<std::string>{
                           "end_player_turn",
                           "reset_enemy_block:0",
                           "reset_enemy_block:2",
                           "enemy_act:0",
                           "enemy_act:2",
                           "tick_enemy_powers:0",
                           "tick_enemy_powers:2",
                           "increment_round",
                           "roll_enemy_next_move:0",
                           "roll_enemy_next_move:2",
                           "reset_player_block",
                           "refill_player_energy:3",
                       }));
}

TEST(TurnFlow, ResolveEndTurnPreDraw_EarlyStopSkipsPostEnemySteps) {
  RecordingEndTurnOps ops;
  ops.stop_after_enemy_0 = true;

  turn_flow::resolve_end_turn_pre_draw(ops);

  EXPECT_EQ(ops.calls, (std::vector<std::string>{
                           "end_player_turn",
                           "reset_enemy_block:0",
                           "reset_enemy_block:2",
                           "enemy_act:0",
                       }));
}

}  // namespace
