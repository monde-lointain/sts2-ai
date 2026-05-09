#include <gtest/gtest.h>

#include <algorithm>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/types.h"

namespace {

using sts2::ai::CardCounts;
using sts2::ai::CompactState;
using sts2::ai::EnemyState;
using sts2::ai::Phase;
using sts2::ai::transition::Action;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::apply_player_action;
using sts2::ai::transition::legal_actions;
using sts2::game::CardId;

CompactState make_test_state() {
  CompactState s;
  s.player_hp = 70;
  s.player_block = 0;
  s.player_strength = 0;
  s.player_weak = 0;
  s.energy = 3;
  s.round = 1;
  s.phase = Phase::kPlayerActing;
  s.enemies[0].hp = 10;
  s.enemies[0].alive = true;
  s.enemies[1].hp = 10;
  s.enemies[1].alive = true;
  return s;
}

Action play(CardId id, uint8_t target = 0xFF,
            CardId discard = CardId::kNone) {
  Action a;
  a.kind = ActionKind::kPlayCard;
  a.card_id = id;
  a.target_idx = target;
  a.survivor_discard_id = discard;
  return a;
}

Action end_turn() {
  Action a;
  a.kind = ActionKind::kEndTurn;
  return a;
}

TEST(Transition, Strike_PlayedAgainstAliveEnemy_DealsBaseDamage) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kStrike, 0)));

  EXPECT_EQ(s.enemies[0].hp, 4);
  EXPECT_TRUE(s.enemies[0].alive);
  EXPECT_EQ(s.energy, 0);
  EXPECT_EQ(s.hand, (CardCounts{}));
  CardCounts disc;
  disc.strike = 1;
  EXPECT_EQ(s.discard, disc);
}

TEST(Transition, Strike_OnDeadEnemy_ReturnsFalseStateUnchanged) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.energy = 1;
  s.enemies[0].hp = 0;
  s.enemies[0].alive = false;

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(s, play(CardId::kStrike, 0)));
  EXPECT_EQ(s, before);
}

TEST(Transition, Defend_AddsBlock) {
  CompactState s = make_test_state();
  s.hand.defend = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kDefend)));

  EXPECT_EQ(s.player_block, 5);
  EXPECT_EQ(s.energy, 0);
  EXPECT_EQ(s.hand, (CardCounts{}));
  CardCounts disc;
  disc.defend = 1;
  EXPECT_EQ(s.discard, disc);
}

TEST(Transition, Neutralize_DealsDamageAndAppliesWeak) {
  CompactState s = make_test_state();
  s.hand.neutralize = 1;
  s.energy = 2;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kNeutralize, 0)));

  EXPECT_EQ(s.enemies[0].hp, 7);
  EXPECT_EQ(s.enemies[0].weak, 1);
  EXPECT_EQ(s.energy, 2);
  EXPECT_EQ(s.hand, (CardCounts{}));
}

TEST(Transition, Neutralize_StackingWeak_OnEnemyAlreadyWeak_Increments) {
  CompactState s = make_test_state();
  s.hand.neutralize = 1;
  s.energy = 2;
  s.enemies[0].weak = 2;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kNeutralize, 0)));

  EXPECT_EQ(s.enemies[0].weak, 3);
}

TEST(Transition, Survivor_GainsBlockAndDiscardsChosenCard) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.hand.defend = 1;
  s.hand.survivor = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(
      s, play(CardId::kSurvivor, 0xFF, CardId::kStrike)));

  EXPECT_EQ(s.player_block, 8);
  EXPECT_EQ(s.energy, 0);
  CardCounts hand_after;
  hand_after.defend = 1;
  EXPECT_EQ(s.hand, hand_after);
  CardCounts disc;
  disc.strike = 1;
  disc.survivor = 1;
  EXPECT_EQ(s.discard, disc);
}

TEST(Transition, Survivor_WhenLastCardInHand_NoOpsDiscard) {
  CompactState s = make_test_state();
  s.hand.survivor = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(
      s, play(CardId::kSurvivor, 0xFF, CardId::kNone)));

  EXPECT_EQ(s.player_block, 8);
  EXPECT_EQ(s.hand, (CardCounts{}));
  CardCounts disc;
  disc.survivor = 1;
  EXPECT_EQ(s.discard, disc);
}

TEST(Transition, PlayCard_InsufficientEnergy_ReturnsFalse) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.energy = 0;

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(s, play(CardId::kStrike, 0)));
  EXPECT_EQ(s, before);
}

TEST(Transition, PlayCard_NotInHand_ReturnsFalse) {
  CompactState s = make_test_state();
  s.energy = 3;

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(
      s, play(CardId::kSurvivor, 0xFF, CardId::kNone)));
  EXPECT_EQ(s, before);
}

TEST(Transition, EndTurn_TogglesPhase) {
  CompactState s = make_test_state();
  ASSERT_EQ(s.phase, Phase::kPlayerActing);

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  EXPECT_EQ(s.phase, Phase::kAtChanceDraw);
}

TEST(Transition, LegalActions_FullHandWithBothEnemies_EnumeratesCorrectly) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.hand.defend = 1;
  s.hand.neutralize = 1;
  s.hand.survivor = 1;
  s.energy = 3;

  const auto actions = legal_actions(s);

  std::vector<Action> expected;
  expected.push_back(play(CardId::kStrike, 0));
  expected.push_back(play(CardId::kStrike, 1));
  expected.push_back(play(CardId::kDefend));
  expected.push_back(play(CardId::kNeutralize, 0));
  expected.push_back(play(CardId::kNeutralize, 1));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kStrike));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kDefend));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kNeutralize));
  expected.push_back(end_turn());

  ASSERT_EQ(actions.size(), expected.size()) << "expected 9 actions";
  for (const auto& want : expected) {
    EXPECT_NE(std::find(actions.begin(), actions.end(), want), actions.end())
        << "missing action with card_id=" << static_cast<int>(want.card_id)
        << " target=" << static_cast<int>(want.target_idx)
        << " discard=" << static_cast<int>(want.survivor_discard_id);
  }
}

TEST(Transition, LegalActions_OneEnemyDead_OnlyAliveEnemyTargeted) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.hand.defend = 1;
  s.hand.neutralize = 1;
  s.hand.survivor = 1;
  s.energy = 3;
  s.enemies[1].hp = 0;
  s.enemies[1].alive = false;

  const auto actions = legal_actions(s);

  std::vector<Action> expected;
  expected.push_back(play(CardId::kStrike, 0));
  expected.push_back(play(CardId::kDefend));
  expected.push_back(play(CardId::kNeutralize, 0));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kStrike));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kDefend));
  expected.push_back(play(CardId::kSurvivor, 0xFF, CardId::kNeutralize));
  expected.push_back(end_turn());

  ASSERT_EQ(actions.size(), expected.size());
  for (const auto& want : expected) {
    EXPECT_NE(std::find(actions.begin(), actions.end(), want), actions.end());
  }
}

TEST(Transition, LegalActions_LowEnergy_OnlyFreeCardAvailable) {
  CompactState s = make_test_state();
  s.hand.strike = 1;
  s.hand.defend = 1;
  s.hand.neutralize = 1;
  s.hand.survivor = 1;
  s.energy = 0;

  const auto actions = legal_actions(s);

  std::vector<Action> expected;
  expected.push_back(play(CardId::kNeutralize, 0));
  expected.push_back(play(CardId::kNeutralize, 1));
  expected.push_back(end_turn());

  ASSERT_EQ(actions.size(), expected.size());
  for (const auto& want : expected) {
    EXPECT_NE(std::find(actions.begin(), actions.end(), want), actions.end());
  }
}

}  // namespace
