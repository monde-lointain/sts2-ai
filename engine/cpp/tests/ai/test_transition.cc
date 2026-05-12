#include <gtest/gtest.h>

#include <algorithm>
#include <cstddef>
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
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyState;
using sts2::ai::EnemyStateBuilder;
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
using sts2::game::Stat;
using sts2::tests::ai::make_counts;
using sts2::tests::helpers::make_starter_combat;

CompactState make_test_state() {
  const EnemyState enemy = EnemyStateBuilder{}.hp(Stat{10}).alive(true).build();
  return CompactStateBuilder{}
      .player_hp(Stat{70})
      .player_block(Stat{0})
      .player_strength(Stat{0})
      .player_weak(Stat{0})
      .energy(Stat{3})
      .round(1)
      .phase(Phase::kPlayerActing)
      .enemy(0, enemy)
      .enemy(1, enemy)
      .build();
}

template <typename Fn>
void update_state(CompactState& s, Fn fn) {
  CompactStateBuilder builder{s};
  fn(builder);
  s = builder.build();
}

template <typename Fn>
void update_enemy(CompactState& s, std::size_t index, Fn fn) {
  EnemyStateBuilder enemy{s.get_enemy(index)};
  fn(enemy);
  update_state(s, [&](CompactStateBuilder& builder) {
    builder.enemy(index, enemy.build());
  });
}

void drain_hand_to_discard(CompactState& s) {
  const CardCounts discard = s.get_discard() + s.get_hand();
  update_state(s, [&](CompactStateBuilder& builder) {
    builder.discard(discard).hand(CardCounts{});
  });
}

Action play(CardId id, int target = -1, CardId discard = CardId::kNone) {
  Action a;
  a.kind = ActionKind::kPlayCard;
  a.card_id = id;
  a.target_idx = sts2::game::EnemySlot{target};
  a.survivor_discard_id = discard;
  return a;
}

Action end_turn() {
  Action a;
  a.kind = ActionKind::kEndTurn;
  return a;
}

void apply_or_fail(CompactState& s, const Action& action) {
  auto next = apply_player_action(s, action);
  ASSERT_TRUE(next.has_value());
  s = *next;
}

TEST(Transition, Strike_PlayedAgainstAliveEnemy_DealsBaseDamage) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 0, 0, 0)).energy(Stat{1});
  });

  apply_or_fail(s, play(CardId::kStrike, 0));

  EXPECT_EQ(s.get_enemy(0).get_hp(), Stat{4});
  EXPECT_TRUE(s.get_enemy(0).get_alive());
  EXPECT_EQ(s.get_energy(), Stat{0});
  EXPECT_EQ(s.get_hand(), (CardCounts{}));
  EXPECT_EQ(s.get_discard(), make_counts(1, 0, 0, 0));
}

TEST(Transition, Strike_OnDeadEnemy_ReturnsFalseStateUnchanged) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 0, 0, 0)).energy(Stat{1});
  });
  update_enemy(
      s, 0, [](EnemyStateBuilder& enemy) { enemy.hp(Stat{0}).alive(false); });

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(s, play(CardId::kStrike, 0)).has_value());
  EXPECT_EQ(s, before);
}

TEST(Transition, Defend_AddsBlock) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(0, 1, 0, 0)).energy(Stat{1});
  });

  apply_or_fail(s, play(CardId::kDefend));

  EXPECT_EQ(s.get_player_block(), Stat{5});
  EXPECT_EQ(s.get_energy(), Stat{0});
  EXPECT_EQ(s.get_hand(), (CardCounts{}));
  EXPECT_EQ(s.get_discard(), make_counts(0, 1, 0, 0));
}

TEST(Transition, Neutralize_DealsDamageAndAppliesWeak) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(0, 0, 1, 0)).energy(Stat{2});
  });

  apply_or_fail(s, play(CardId::kNeutralize, 0));

  EXPECT_EQ(s.get_enemy(0).get_hp(), Stat{7});
  EXPECT_EQ(s.get_enemy(0).get_weak(), Stat{1});
  EXPECT_EQ(s.get_energy(), Stat{2});
  EXPECT_EQ(s.get_hand(), (CardCounts{}));
}

TEST(Transition, Neutralize_StackingWeak_OnEnemyAlreadyWeak_Increments) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(0, 0, 1, 0)).energy(Stat{2});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) { enemy.weak(Stat{2}); });

  apply_or_fail(s, play(CardId::kNeutralize, 0));

  EXPECT_EQ(s.get_enemy(0).get_weak(), Stat{3});
}

TEST(Transition, Survivor_GainsBlockAndDiscardsChosenCard) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 1, 0, 1)).energy(Stat{1});
  });

  apply_or_fail(s, play(CardId::kSurvivor, -1, CardId::kStrike));

  EXPECT_EQ(s.get_player_block(), Stat{8});
  EXPECT_EQ(s.get_energy(), Stat{0});
  EXPECT_EQ(s.get_hand(), make_counts(0, 1, 0, 0));
  EXPECT_EQ(s.get_discard(), make_counts(1, 0, 0, 1));
}

TEST(Transition, Survivor_WhenLastCardInHand_NoOpsDiscard) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(0, 0, 0, 1)).energy(Stat{1});
  });

  apply_or_fail(s, play(CardId::kSurvivor, -1, CardId::kNone));

  EXPECT_EQ(s.get_player_block(), Stat{8});
  EXPECT_EQ(s.get_hand(), (CardCounts{}));
  EXPECT_EQ(s.get_discard(), make_counts(0, 0, 0, 1));
}

TEST(Transition, Survivor_NoneDiscardWithOtherCards_NoOpsDiscard) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 1, 0, 1)).energy(Stat{1});
  });

  apply_or_fail(s, play(CardId::kSurvivor, -1, CardId::kNone));

  EXPECT_EQ(s.get_player_block(), Stat{8});
  EXPECT_EQ(s.get_hand(), make_counts(1, 1, 0, 0));
  EXPECT_EQ(s.get_discard(), make_counts(0, 0, 0, 1));
}

TEST(Transition, PlayCard_InsufficientEnergy_ReturnsFalse) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 0, 0, 0)).energy(Stat{0});
  });

  const CompactState before = s;
  EXPECT_FALSE(apply_player_action(s, play(CardId::kStrike, 0)).has_value());
  EXPECT_EQ(s, before);
}

TEST(Transition, PlayCard_NotInHand_ReturnsFalse) {
  CompactState s = make_test_state();
  update_state(s,
               [](CompactStateBuilder& builder) { builder.energy(Stat{3}); });

  const CompactState before = s;
  EXPECT_FALSE(
      apply_player_action(s, play(CardId::kSurvivor, -1, CardId::kNone))
          .has_value());
  EXPECT_EQ(s, before);
}

TEST(Transition, EndTurn_TogglesPhase) {
  CompactState s = make_test_state();
  ASSERT_EQ(s.get_phase(), Phase::kPlayerActing);

  apply_or_fail(s, end_turn());
  EXPECT_EQ(s.get_phase(), Phase::kAtChanceDraw);
}

TEST(Transition, LegalActions_FullHandWithBothEnemies_EnumeratesCorrectly) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 1, 1, 1)).energy(Stat{3});
  });

  const auto actions = legal_actions(s);

  std::vector<Action> expected;
  expected.push_back(play(CardId::kStrike, 0));
  expected.push_back(play(CardId::kStrike, 1));
  expected.push_back(play(CardId::kDefend));
  expected.push_back(play(CardId::kNeutralize, 0));
  expected.push_back(play(CardId::kNeutralize, 1));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kStrike));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kDefend));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kNeutralize));
  expected.push_back(end_turn());

  ASSERT_EQ(actions.size(), expected.size()) << "expected 9 actions";
  for (const auto& want : expected) {
    EXPECT_NE(std::find(actions.begin(), actions.end(), want), actions.end())
        << "missing action with card_id=" << static_cast<int>(want.card_id)
        << " target=" << want.target_idx.raw()
        << " discard=" << static_cast<int>(want.survivor_discard_id);
  }
}

TEST(Transition, LegalActions_OneEnemyDead_OnlyAliveEnemyTargeted) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 1, 1, 1)).energy(Stat{3});
  });
  update_enemy(
      s, 1, [](EnemyStateBuilder& enemy) { enemy.hp(Stat{0}).alive(false); });

  const auto actions = legal_actions(s);

  std::vector<Action> expected;
  expected.push_back(play(CardId::kStrike, 0));
  expected.push_back(play(CardId::kDefend));
  expected.push_back(play(CardId::kNeutralize, 0));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kStrike));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kDefend));
  expected.push_back(play(CardId::kSurvivor, -1, CardId::kNeutralize));
  expected.push_back(end_turn());

  ASSERT_EQ(actions.size(), expected.size());
  for (const auto& want : expected) {
    EXPECT_NE(std::find(actions.begin(), actions.end(), want), actions.end());
  }
}

TEST(Transition, LegalActions_LowEnergy_OnlyFreeCardAvailable) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(1, 1, 1, 1)).energy(Stat{0});
  });

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
  update_state(s, [](CompactStateBuilder& builder) {
    builder.hand(make_counts(2, 1, 0, 0)).discard(CardCounts{});
  });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_hand(), (CardCounts{}));
  EXPECT_EQ(s.get_discard(), make_counts(2, 1, 0, 0));
}

TEST(Transition, EndTurn_PreDrawResolution_EnemyBlockResetAndAct) {
  sts2::game::Combat combat = make_starter_combat(0xC0FFEEULL);
  CompactState s = from_combat(combat);
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) { enemy.block(Stat{7}); });
  update_enemy(s, 1, [](EnemyStateBuilder& enemy) { enemy.block(Stat{4}); });
  // Drain hand to keep test focused on enemy phase.
  drain_hand_to_discard(s);
  const Stat hp_before = s.get_player_hp();

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_enemy(0).get_block(), Stat{0});
  EXPECT_EQ(s.get_enemy(1).get_block(), Stat{0});
  // Engine semantics (powers::tick_at_turn_end): Ritual::just_applied set by
  // act and cleared by the tick that runs in the same enemy_phase. Strength
  // gain is suppressed for that one cycle.
  EXPECT_FALSE(s.get_enemy(0).get_just_applied_ritual());
  EXPECT_FALSE(s.get_enemy(1).get_just_applied_ritual());
  EXPECT_EQ(s.get_enemy(0).get_strength(), Stat{0});
  EXPECT_EQ(s.get_enemy(1).get_strength(), Stat{0});
  EXPECT_EQ(s.get_player_hp(), hp_before);
  EXPECT_EQ(s.get_round(), 2);
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kDarkStrike);
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kDarkStrike);
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_phase(), Phase::kAtChanceDraw);
}

TEST(Transition,
     EndTurn_PreDrawResolution_RitualConvertsToStrengthOnSubsequentEndTurn) {
  sts2::game::Combat combat = make_starter_combat(0xC0FFEEULL);
  CompactState s = from_combat(combat);
  // Discard hand to isolate enemy mechanics.
  drain_hand_to_discard(s);

  // Round 1 -> 2: Incantation acts (sets just_applied), then same-turn tick
  // clears the flag. Strength stays at 0; Ritual conversion is deferred to the
  // next end-of-turn tick (matches engine T-CMB-195/200).
  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);
  ASSERT_EQ(s.get_round(), 2);
  ASSERT_FALSE(s.get_enemy(0).get_just_applied_ritual());
  ASSERT_FALSE(s.get_enemy(1).get_just_applied_ritual());
  ASSERT_EQ(s.get_enemy(0).get_strength(), Stat{0});
  ASSERT_EQ(s.get_enemy(1).get_strength(), Stat{0});
  ASSERT_EQ(s.get_enemy(0).get_current_move(), MoveId::kDarkStrike);
  ASSERT_EQ(s.get_enemy(1).get_current_move(), MoveId::kDarkStrike);

  // Drain newly-drawn hand again to keep next end_turn resolution clean.
  drain_hand_to_discard(s);
  update_state(s, [](CompactStateBuilder& builder) {
    builder.phase(Phase::kPlayerActing);
  });
  const Stat hp_before_r2 = s.get_player_hp();

  // Round 2 -> 3: DarkStrike acts using OLD strength (0), then tick converts
  // Ritual -> Strength. Damage = Calcified(9) + Damp(1) = 10 (block reset).
  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);
  EXPECT_EQ(s.get_round(), 3);
  EXPECT_FALSE(s.get_enemy(0).get_just_applied_ritual());
  EXPECT_FALSE(s.get_enemy(1).get_just_applied_ritual());
  EXPECT_EQ(s.get_enemy(0).get_strength(), s.get_enemy(0).get_ritual_amount());
  EXPECT_EQ(s.get_enemy(1).get_strength(), s.get_enemy(1).get_ritual_amount());
  EXPECT_EQ(hp_before_r2.value() - s.get_player_hp().value(), 10);
}

TEST(Transition, EndTurn_PreDrawResolution_DarkStrikeAgainstPlayerBlock) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{70}).player_block(Stat{20}).round(5).energy(Stat{0});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.alive(true)
        .hp(Stat{30})
        .strength(Stat{10})
        .dark_strike_base(Stat{9})
        .current_move(MoveId::kDarkStrike)
        .performed_first_move(true);
  });
  update_enemy(s, 1, [](EnemyStateBuilder& enemy) {
    enemy.alive(true)
        .hp(Stat{30})
        .strength(Stat{10})
        .dark_strike_base(Stat{1})
        .current_move(MoveId::kDarkStrike)
        .performed_first_move(true);
  });
  // Hand empty so end_player_turn is a no-op for piles.
  update_state(
      s, [](CompactStateBuilder& builder) { builder.hand(CardCounts{}); });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  // Calcified DarkStrike: 9+10 = 19; player_block 20 -> 1. hp untouched.
  // Damp DarkStrike: 1+10 = 11; player_block 1 -> 0, hp -= 10 -> 60.
  // Then start_player_turn: round becomes 6, player_block reset to 0.
  EXPECT_EQ(s.get_player_hp(), Stat{60});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_round(), 6);
}

TEST(Transition, EndTurn_PreDrawResolution_DarkStrikeKillsPlayer_StopsEarly) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{5})
        .player_block(Stat{0})
        .energy(Stat{0})
        .round(4)
        .hand(CardCounts{});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.alive(true)
        .hp(Stat{30})
        .strength(Stat{0})
        .dark_strike_base(Stat{9})
        .current_move(MoveId::kDarkStrike)
        .performed_first_move(true);
  });
  // Enemy[1] should NOT act because combat ends mid-phase.
  update_enemy(s, 1, [](EnemyStateBuilder& enemy) {
    enemy.alive(true)
        .hp(Stat{30})
        .current_move(MoveId::kIncantation)
        .performed_first_move(true)
        .just_applied_ritual(false);
  });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_player_hp(), Stat{0});
  EXPECT_TRUE(is_terminal(s));
  // Enemy[1] act would have set just_applied_ritual; verify it didn't run.
  EXPECT_FALSE(s.get_enemy(1).get_just_applied_ritual());
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kIncantation);
  // round NOT incremented because we returned before round_++ logic.
  EXPECT_EQ(s.get_round(), 4);
}

TEST(Transition, EndTurn_PreDrawResolution_RoundIncrement_ResetsBlock) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.round(3).player_block(Stat{15}).energy(Stat{0}).hand(CardCounts{});
  });
  // Make enemies harmless (Incantation, no damage).
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.current_move(MoveId::kIncantation).performed_first_move(true);
  });
  update_enemy(s, 1, [](EnemyStateBuilder& enemy) {
    enemy.current_move(MoveId::kIncantation).performed_first_move(true);
  });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_round(), 4);
  EXPECT_EQ(s.get_player_block(), Stat{0});
}

TEST(Transition, EndTurn_PreDrawResolution_EnergyRefilledToThree) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.energy(Stat{0}).hand(CardCounts{});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.current_move(MoveId::kIncantation).performed_first_move(true);
  });
  update_enemy(s, 1, [](EnemyStateBuilder& enemy) {
    enemy.current_move(MoveId::kIncantation).performed_first_move(true);
  });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_energy(), Stat{3});
}

TEST(Transition, DrawCount_Round1Returns7) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) { builder.round(1); });
  EXPECT_EQ(draw_count(s), 7);
}

TEST(Transition, DrawCount_Round2Returns5) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) { builder.round(2); });
  EXPECT_EQ(draw_count(s), 5);
}

TEST(Transition, ApplyDraw_BasicDrain) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.phase(Phase::kAtChanceDraw)
        .hand(CardCounts{})
        .draw(make_counts(2, 2, 0, 0))
        .discard(make_counts(1, 1, 1, 0));
  });

  s = apply_draw(s, make_counts(1, 1, 0, 0));

  EXPECT_EQ(s.get_hand(), make_counts(1, 1, 0, 0));
  EXPECT_EQ(s.get_draw(), make_counts(1, 1, 0, 0));
  EXPECT_EQ(s.get_discard(), make_counts(1, 1, 1, 0));
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);
}

TEST(Transition, ApplyDraw_TriggersReshuffleWhenDrawShortOfRequest) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.phase(Phase::kAtChanceDraw)
        .hand(CardCounts{})
        .draw(make_counts(1, 0, 0, 0))
        .discard(make_counts(3, 2, 0, 0));
  });

  s = apply_draw(s, make_counts(2, 1, 0, 0));

  EXPECT_EQ(s.get_hand(), make_counts(2, 1, 0, 0));
  EXPECT_EQ(s.get_draw(), make_counts(2, 1, 0, 0));
  EXPECT_EQ(s.get_discard(), (CardCounts{}));
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);
}

TEST(Transition, IsTerminal_PlayerDead) {
  CompactState s = make_test_state();
  update_state(
      s, [](CompactStateBuilder& builder) { builder.player_hp(Stat{0}); });
  EXPECT_TRUE(is_terminal(s));
}

TEST(Transition, IsTerminal_AllEnemiesDead) {
  CompactState s = make_test_state();
  update_enemy(
      s, 0, [](EnemyStateBuilder& enemy) { enemy.alive(false).hp(Stat{0}); });
  update_enemy(
      s, 1, [](EnemyStateBuilder& enemy) { enemy.alive(false).hp(Stat{0}); });
  EXPECT_TRUE(is_terminal(s));
}

TEST(Transition, IsTerminal_OngoingFalse) {
  CompactState s = make_test_state();
  EXPECT_FALSE(is_terminal(s));
}

}  // namespace
