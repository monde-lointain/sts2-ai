#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/ai/test_helpers.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyStateBuilder;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::tests::ai::make_counts;
using sts2::tests::helpers::make_starter_combat;

TEST(AiState, FromCombat_FreshStarter_SnapshotMatches) {
  sts2::game::Combat combat = make_starter_combat(0xC0FFEEULL);
  const CompactState s = from_combat(combat);

  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_player_strength(), Stat{0});
  EXPECT_EQ(s.get_player_weak(), Stat{0});
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);

  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kIncantation);
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kIncantation);
  EXPECT_TRUE(s.get_enemy(0).get_performed_first_move());
  EXPECT_TRUE(s.get_enemy(1).get_performed_first_move());

  // Calcified Cultist: dark_strike=9, ritual=2.
  EXPECT_EQ(s.get_enemy(0).get_dark_strike_base(), Stat{9});
  EXPECT_EQ(s.get_enemy(0).get_ritual_amount(), Stat{2});
  // Damp Cultist: dark_strike=1, ritual=5.
  EXPECT_EQ(s.get_enemy(1).get_dark_strike_base(), Stat{1});
  EXPECT_EQ(s.get_enemy(1).get_ritual_amount(), Stat{5});

  // Ring of the Snake: round-1 hand is 7.
  EXPECT_EQ(s.get_hand().total(), 7);
  // Silent starter deck has 12 cards total.
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);
}

TEST(AiState, CompactStateBuilder_MatchesDirectSetup) {
  const auto hand = make_counts(1, 2, 0, 1);
  const auto draw = make_counts(3, 1, 1, 0);
  const auto discard = make_counts(0, 1, 0, 2);

  const CompactState expected =
      CompactStateBuilder{}
          .player_hp(Stat{41})
          .player_block(Stat{7})
          .player_strength(Stat{2})
          .player_weak(Stat{1})
          .energy(Stat{3})
          .round(4)
          .phase(Phase::kAtChanceDraw)
          .enemy(0, EnemyStateBuilder{}
                        .hp(Stat{12})
                        .block(Stat{5})
                        .strength(Stat{3})
                        .weak(Stat{1})
                        .dark_strike_base(Stat{9})
                        .ritual_amount(Stat{2})
                        .just_applied_ritual(true)
                        .performed_first_move(true)
                        .current_move(MoveId::kDarkStrike)
                        .alive(true)
                        .build())
          .hand(hand)
          .draw(draw)
          .discard(discard)
          .build();

  const CompactState built = CompactStateBuilder{expected}.build();

  EXPECT_EQ(built, expected);
  EXPECT_EQ(built.get_player_hp(), Stat{41});
  EXPECT_EQ(built.get_player_block(), Stat{7});
  EXPECT_EQ(built.get_player_strength(), Stat{2});
  EXPECT_EQ(built.get_player_weak(), Stat{1});
  EXPECT_EQ(built.get_energy(), Stat{3});
  EXPECT_EQ(built.get_round(), 4);
  EXPECT_EQ(built.get_phase(), Phase::kAtChanceDraw);
  EXPECT_EQ(built.get_hand(), hand);
  EXPECT_EQ(built.get_draw(), draw);
  EXPECT_EQ(built.get_discard(), discard);

  const auto& built_enemy = built.get_enemies()[0];
  EXPECT_EQ(built_enemy.get_hp(), Stat{12});
  EXPECT_EQ(built_enemy.get_block(), Stat{5});
  EXPECT_EQ(built_enemy.get_strength(), Stat{3});
  EXPECT_EQ(built_enemy.get_weak(), Stat{1});
  EXPECT_EQ(built_enemy.get_dark_strike_base(), Stat{9});
  EXPECT_EQ(built_enemy.get_ritual_amount(), Stat{2});
  EXPECT_TRUE(built_enemy.get_just_applied_ritual());
  EXPECT_TRUE(built_enemy.get_performed_first_move());
  EXPECT_EQ(built_enemy.get_current_move(), MoveId::kDarkStrike);
  EXPECT_TRUE(built_enemy.get_alive());
}

}  // namespace
