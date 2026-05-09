#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::tests::helpers::MakeStarterCombat;

TEST(AiState, FromCombat_FreshStarter_SnapshotMatches) {
  sts2::game::Combat combat = MakeStarterCombat(0xC0FFEEULL);
  const CompactState s = from_combat(combat);

  EXPECT_EQ(s.player_hp, Stat{70});
  EXPECT_EQ(s.player_block, Stat{0});
  EXPECT_EQ(s.player_strength, Stat{0});
  EXPECT_EQ(s.player_weak, Stat{0});
  EXPECT_EQ(s.energy, Stat{3});
  EXPECT_EQ(s.round, 1);
  EXPECT_EQ(s.phase, Phase::kPlayerActing);

  ASSERT_TRUE(s.enemies[0].alive);
  ASSERT_TRUE(s.enemies[1].alive);
  EXPECT_EQ(s.enemies[0].current_move, MoveId::kIncantation);
  EXPECT_EQ(s.enemies[1].current_move, MoveId::kIncantation);
  EXPECT_TRUE(s.enemies[0].performed_first_move);
  EXPECT_TRUE(s.enemies[1].performed_first_move);

  // Calcified Cultist: dark_strike=9, ritual=2.
  EXPECT_EQ(s.enemies[0].dark_strike_base, Stat{9});
  EXPECT_EQ(s.enemies[0].ritual_amount, Stat{2});
  // Damp Cultist: dark_strike=1, ritual=5.
  EXPECT_EQ(s.enemies[1].dark_strike_base, Stat{1});
  EXPECT_EQ(s.enemies[1].ritual_amount, Stat{5});

  // Ring of the Snake: round-1 hand is 7.
  EXPECT_EQ(s.hand.total(), 7);
  // Silent starter deck has 12 cards total.
  EXPECT_EQ(s.hand.total() + s.draw.total() + s.discard.total(), 12);
}

}  // namespace
