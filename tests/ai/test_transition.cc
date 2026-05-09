#include <gtest/gtest.h>

#include <algorithm>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/ai/test_helpers.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::CardCounts;
using sts2::ai::CompactState;
using sts2::ai::EnemyState;
using sts2::ai::from_combat;
using sts2::ai::Phase;
using sts2::ai::transition::Action;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::apply_draw;
using sts2::ai::transition::apply_player_action;
using sts2::ai::transition::draw_count;
using sts2::ai::transition::is_terminal;
using sts2::ai::transition::legal_actions;
using sts2::ai::transition::resolve_end_turn_pre_draw;
using sts2::game::CardId;
using sts2::game::MoveId;
using sts2::tests::ai::make_counts;
using sts2::tests::helpers::MakeStarterCombat;

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
  s.hand[CardId::kStrike] = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kStrike, 0)));

  EXPECT_EQ(s.enemies[0].hp, 4);
  EXPECT_TRUE(s.enemies[0].alive);
  EXPECT_EQ(s.energy, 0);
  EXPECT_EQ(s.hand, (CardCounts{}));
  EXPECT_EQ(s.discard, make_counts(1, 0, 0, 0));
}

TEST(Transition, Strike_OnDeadEnemy_ReturnsFalseStateUnchanged) {
  CompactState s = make_test_state();
  s.hand[CardId::kStrike] = 1;
  s.energy = 1;
  s.enemies[0].hp = 0;
  s.enemies[0].alive = false;

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(s, play(CardId::kStrike, 0)));
  EXPECT_EQ(s, before);
}

TEST(Transition, Defend_AddsBlock) {
  CompactState s = make_test_state();
  s.hand[CardId::kDefend] = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kDefend)));

  EXPECT_EQ(s.player_block, 5);
  EXPECT_EQ(s.energy, 0);
  EXPECT_EQ(s.hand, (CardCounts{}));
  EXPECT_EQ(s.discard, make_counts(0, 1, 0, 0));
}

TEST(Transition, Neutralize_DealsDamageAndAppliesWeak) {
  CompactState s = make_test_state();
  s.hand[CardId::kNeutralize] = 1;
  s.energy = 2;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kNeutralize, 0)));

  EXPECT_EQ(s.enemies[0].hp, 7);
  EXPECT_EQ(s.enemies[0].weak, 1);
  EXPECT_EQ(s.energy, 2);
  EXPECT_EQ(s.hand, (CardCounts{}));
}

TEST(Transition, Neutralize_StackingWeak_OnEnemyAlreadyWeak_Increments) {
  CompactState s = make_test_state();
  s.hand[CardId::kNeutralize] = 1;
  s.energy = 2;
  s.enemies[0].weak = 2;

  ASSERT_TRUE(apply_player_action(s, play(CardId::kNeutralize, 0)));

  EXPECT_EQ(s.enemies[0].weak, 3);
}

TEST(Transition, Survivor_GainsBlockAndDiscardsChosenCard) {
  CompactState s = make_test_state();
  s.hand[CardId::kStrike] = 1;
  s.hand[CardId::kDefend] = 1;
  s.hand[CardId::kSurvivor] = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(
      s, play(CardId::kSurvivor, 0xFF, CardId::kStrike)));

  EXPECT_EQ(s.player_block, 8);
  EXPECT_EQ(s.energy, 0);
  EXPECT_EQ(s.hand, make_counts(0, 1, 0, 0));
  EXPECT_EQ(s.discard, make_counts(1, 0, 0, 1));
}

TEST(Transition, Survivor_WhenLastCardInHand_NoOpsDiscard) {
  CompactState s = make_test_state();
  s.hand[CardId::kSurvivor] = 1;
  s.energy = 1;

  ASSERT_TRUE(apply_player_action(
      s, play(CardId::kSurvivor, 0xFF, CardId::kNone)));

  EXPECT_EQ(s.player_block, 8);
  EXPECT_EQ(s.hand, (CardCounts{}));
  EXPECT_EQ(s.discard, make_counts(0, 0, 0, 1));
}

TEST(Transition, PlayCard_InsufficientEnergy_ReturnsFalse) {
  CompactState s = make_test_state();
  s.hand[CardId::kStrike] = 1;
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
  s.hand[CardId::kStrike] = 1;
  s.hand[CardId::kDefend] = 1;
  s.hand[CardId::kNeutralize] = 1;
  s.hand[CardId::kSurvivor] = 1;
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
  s.hand[CardId::kStrike] = 1;
  s.hand[CardId::kDefend] = 1;
  s.hand[CardId::kNeutralize] = 1;
  s.hand[CardId::kSurvivor] = 1;
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
  s.hand[CardId::kStrike] = 1;
  s.hand[CardId::kDefend] = 1;
  s.hand[CardId::kNeutralize] = 1;
  s.hand[CardId::kSurvivor] = 1;
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

TEST(Transition, EndTurn_PreDrawResolution_PlayerHandToDiscard) {
  CompactState s = make_test_state();
  s.hand[CardId::kStrike] = 2;
  s.hand[CardId::kDefend] = 1;
  s.discard = CardCounts{};

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.hand, (CardCounts{}));
  EXPECT_EQ(s.discard, make_counts(2, 1, 0, 0));
}

TEST(Transition, EndTurn_PreDrawResolution_EnemyBlockResetAndAct) {
  sts2::game::Combat combat = MakeStarterCombat(0xC0FFEEULL);
  CompactState s = from_combat(combat);
  s.enemies[0].block = 7;
  s.enemies[1].block = 4;
  // Drain hand to keep test focused on enemy phase.
  s.discard += s.hand;
  s.hand = CardCounts{};
  const uint8_t hp_before = s.player_hp;

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.enemies[0].block, 0);
  EXPECT_EQ(s.enemies[1].block, 0);
  // Engine semantics (powers::tick_at_turn_end): Ritual::just_applied set by
  // act and cleared by the tick that runs in the same enemy_phase. Strength
  // gain is suppressed for that one cycle.
  EXPECT_FALSE(s.enemies[0].just_applied_ritual);
  EXPECT_FALSE(s.enemies[1].just_applied_ritual);
  EXPECT_EQ(s.enemies[0].strength, 0);
  EXPECT_EQ(s.enemies[1].strength, 0);
  EXPECT_EQ(s.player_hp, hp_before);
  EXPECT_EQ(s.round, 2);
  EXPECT_EQ(s.enemies[0].current_move, MoveId::kDarkStrike);
  EXPECT_EQ(s.enemies[1].current_move, MoveId::kDarkStrike);
  EXPECT_EQ(s.energy, 3);
  EXPECT_EQ(s.player_block, 0);
  EXPECT_EQ(s.phase, Phase::kAtChanceDraw);
}

TEST(Transition, EndTurn_PreDrawResolution_RitualConvertsToStrengthOnSubsequentEndTurn) {
  sts2::game::Combat combat = MakeStarterCombat(0xC0FFEEULL);
  CompactState s = from_combat(combat);
  // Discard hand to isolate enemy mechanics.
  s.discard += s.hand;
  s.hand = CardCounts{};

  // Round 1 -> 2: Incantation acts (sets just_applied), then same-turn tick
  // clears the flag. Strength stays at 0; Ritual conversion is deferred to the
  // next end-of-turn tick (matches engine T-CMB-195/200).
  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);
  ASSERT_EQ(s.round, 2);
  ASSERT_FALSE(s.enemies[0].just_applied_ritual);
  ASSERT_FALSE(s.enemies[1].just_applied_ritual);
  ASSERT_EQ(s.enemies[0].strength, 0);
  ASSERT_EQ(s.enemies[1].strength, 0);
  ASSERT_EQ(s.enemies[0].current_move, MoveId::kDarkStrike);
  ASSERT_EQ(s.enemies[1].current_move, MoveId::kDarkStrike);

  // Drain newly-drawn hand again to keep next end_turn resolution clean.
  s.discard += s.hand;
  s.hand = CardCounts{};
  s.phase = Phase::kPlayerActing;
  const uint8_t hp_before_r2 = s.player_hp;

  // Round 2 -> 3: DarkStrike acts using OLD strength (0), then tick converts
  // Ritual -> Strength. Damage = Calcified(9) + Damp(1) = 10 (block reset).
  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);
  EXPECT_EQ(s.round, 3);
  EXPECT_FALSE(s.enemies[0].just_applied_ritual);
  EXPECT_FALSE(s.enemies[1].just_applied_ritual);
  EXPECT_EQ(s.enemies[0].strength, s.enemies[0].ritual_amount);
  EXPECT_EQ(s.enemies[1].strength, s.enemies[1].ritual_amount);
  EXPECT_EQ(static_cast<int>(hp_before_r2) - static_cast<int>(s.player_hp), 10);
}

TEST(Transition, EndTurn_PreDrawResolution_DarkStrikeAgainstPlayerBlock) {
  CompactState s = make_test_state();
  s.player_hp = 70;
  s.player_block = 20;
  s.round = 5;
  s.energy = 0;
  s.enemies[0].alive = true;
  s.enemies[0].hp = 30;
  s.enemies[0].strength = 10;
  s.enemies[0].dark_strike_base = 9;
  s.enemies[0].current_move = MoveId::kDarkStrike;
  s.enemies[0].performed_first_move = true;
  s.enemies[1].alive = true;
  s.enemies[1].hp = 30;
  s.enemies[1].strength = 10;
  s.enemies[1].dark_strike_base = 1;
  s.enemies[1].current_move = MoveId::kDarkStrike;
  s.enemies[1].performed_first_move = true;
  // Hand empty so end_player_turn is a no-op for piles.
  s.hand = CardCounts{};

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  // Calcified DarkStrike: 9+10 = 19; player_block 20 -> 1. hp untouched.
  // Damp DarkStrike: 1+10 = 11; player_block 1 -> 0, hp -= 10 -> 60.
  // Then start_player_turn: round becomes 6, player_block reset to 0.
  EXPECT_EQ(s.player_hp, 60);
  EXPECT_EQ(s.player_block, 0);
  EXPECT_EQ(s.round, 6);
}

TEST(Transition, EndTurn_PreDrawResolution_DarkStrikeKillsPlayer_StopsEarly) {
  CompactState s = make_test_state();
  s.player_hp = 5;
  s.player_block = 0;
  s.energy = 0;
  s.round = 4;
  s.hand = CardCounts{};
  s.enemies[0].alive = true;
  s.enemies[0].hp = 30;
  s.enemies[0].strength = 0;
  s.enemies[0].dark_strike_base = 9;
  s.enemies[0].current_move = MoveId::kDarkStrike;
  s.enemies[0].performed_first_move = true;
  // Enemy[1] should NOT act because combat ends mid-phase.
  s.enemies[1].alive = true;
  s.enemies[1].hp = 30;
  s.enemies[1].current_move = MoveId::kIncantation;
  s.enemies[1].performed_first_move = true;
  s.enemies[1].just_applied_ritual = false;

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.player_hp, 0);
  EXPECT_TRUE(is_terminal(s));
  // Enemy[1] act would have set just_applied_ritual; verify it didn't run.
  EXPECT_FALSE(s.enemies[1].just_applied_ritual);
  EXPECT_EQ(s.enemies[1].current_move, MoveId::kIncantation);
  // round NOT incremented because we returned before round_++ logic.
  EXPECT_EQ(s.round, 4);
}

TEST(Transition, EndTurn_PreDrawResolution_RoundIncrement_ResetsBlock) {
  CompactState s = make_test_state();
  s.round = 3;
  s.player_block = 15;
  s.energy = 0;
  s.hand = CardCounts{};
  // Make enemies harmless (Incantation, no damage).
  s.enemies[0].current_move = MoveId::kIncantation;
  s.enemies[0].performed_first_move = true;
  s.enemies[1].current_move = MoveId::kIncantation;
  s.enemies[1].performed_first_move = true;

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.round, 4);
  EXPECT_EQ(s.player_block, 0);
}

TEST(Transition, EndTurn_PreDrawResolution_EnergyRefilledToThree) {
  CompactState s = make_test_state();
  s.energy = 0;
  s.hand = CardCounts{};
  s.enemies[0].current_move = MoveId::kIncantation;
  s.enemies[0].performed_first_move = true;
  s.enemies[1].current_move = MoveId::kIncantation;
  s.enemies[1].performed_first_move = true;

  ASSERT_TRUE(apply_player_action(s, end_turn()));
  resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.energy, 3);
}

TEST(Transition, DrawCount_Round1Returns7) {
  CompactState s = make_test_state();
  s.round = 1;
  EXPECT_EQ(draw_count(s), 7);
}

TEST(Transition, DrawCount_Round2Returns5) {
  CompactState s = make_test_state();
  s.round = 2;
  EXPECT_EQ(draw_count(s), 5);
}

TEST(Transition, ApplyDraw_BasicDrain) {
  CompactState s = make_test_state();
  s.phase = Phase::kAtChanceDraw;
  s.hand = CardCounts{};
  s.draw = make_counts(2, 2, 0, 0);
  s.discard = make_counts(1, 1, 1, 0);

  apply_draw(s, make_counts(1, 1, 0, 0));

  EXPECT_EQ(s.hand, make_counts(1, 1, 0, 0));
  EXPECT_EQ(s.draw, make_counts(1, 1, 0, 0));
  EXPECT_EQ(s.discard, make_counts(1, 1, 1, 0));
  EXPECT_EQ(s.phase, Phase::kPlayerActing);
}

TEST(Transition, ApplyDraw_TriggersReshuffleWhenDrawShortOfRequest) {
  CompactState s = make_test_state();
  s.phase = Phase::kAtChanceDraw;
  s.hand = CardCounts{};
  s.draw = make_counts(1, 0, 0, 0);
  s.discard = make_counts(3, 2, 0, 0);

  apply_draw(s, make_counts(2, 1, 0, 0));

  EXPECT_EQ(s.hand, make_counts(2, 1, 0, 0));
  EXPECT_EQ(s.draw, make_counts(2, 1, 0, 0));
  EXPECT_EQ(s.discard, (CardCounts{}));
  EXPECT_EQ(s.phase, Phase::kPlayerActing);
}

TEST(Transition, IsTerminal_PlayerDead) {
  CompactState s = make_test_state();
  s.player_hp = 0;
  EXPECT_TRUE(is_terminal(s));
}

TEST(Transition, IsTerminal_AllEnemiesDead) {
  CompactState s = make_test_state();
  s.enemies[0].alive = false;
  s.enemies[0].hp = 0;
  s.enemies[1].alive = false;
  s.enemies[1].hp = 0;
  EXPECT_TRUE(is_terminal(s));
}

TEST(Transition, IsTerminal_OngoingFalse) {
  CompactState s = make_test_state();
  EXPECT_FALSE(is_terminal(s));
}

}  // namespace
