#include <gtest/gtest.h>

#include <algorithm>
#include <cstddef>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/damage_calc.h"
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

// ---------------------------------------------------------------------------
// SlimedCap — kAddStatusCard accumulation bound (wave-22-fix-4 / H.α)
//
// LeafSlimeM STICKY_SHOT fires kAddStatusCard with value=2 (move_index=1,
// kStrict follow-up → phase stays kAtChanceDraw; no kAtEnemyMoveRng detour).
// We call apply_player_action(end_turn) + resolve_end_turn_pre_draw per
// standard pattern. Enemy[1] is dead to avoid interference.
// ---------------------------------------------------------------------------

using sts2::game::MonsterKind;

// Helper: build a state with a LeafSlimeM at move_index=1 (STICKY_SHOT)
// and a specified discard[kSlimed] count. Hand is empty (no hand-to-discard
// noise); energy doesn't matter for the enemy-phase.
CompactState make_slimed_test_state(uint8_t initial_slimed) {
  // Slot 0: LeafSlimeM at STICKY_SHOT (move_index=1, performed_first_move=true
  //   so the initial-move-index branch is skipped in do_roll_next_move).
  const EnemyState slime = EnemyStateBuilder{}
                               .kind(MonsterKind::kLeafSlimeM)
                               .hp(sts2::game::Stat{32})
                               .alive(true)
                               .move_index(1)
                               .current_move(sts2::game::MoveId::kStickyShot)
                               .performed_first_move(true)
                               .build();
  // Slot 1: dead enemy — no interaction.
  const EnemyState dead =
      EnemyStateBuilder{}.hp(sts2::game::Stat{0}).alive(false).build();

  CardCounts discard_pile{};
  discard_pile[sts2::game::CardId::kSlimed] = initial_slimed;

  return CompactStateBuilder{}
      .player_hp(sts2::game::Stat{70})
      .player_block(sts2::game::Stat{0})
      .player_strength(sts2::game::Stat{0})
      .player_weak(sts2::game::Stat{0})
      .energy(sts2::game::Stat{3})
      .round(1)
      .phase(Phase::kPlayerActing)
      .hand(CardCounts{})
      .discard(discard_pile)
      .enemy(0, slime)
      .enemy(1, dead)
      .build();
}

TEST(Transition, SlimedCap_AccumulationBoundedAt8) {
  // Cap: 8 Slimed in discard + STICKY_SHOT(value=2) → still 8.
  CompactState s = make_slimed_test_state(8);
  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);
  EXPECT_EQ(s.get_discard()[sts2::game::CardId::kSlimed], 8)
      << "discard[kSlimed] must not exceed kMaxSlimedAccumulation";
}

TEST(Transition, SlimedCap_ClampedAtBoundaryWhenValueExceedsGap) {
  // Edge: 7 Slimed + STICKY_SHOT(value=2) → clamped to 8 (not 9).
  CompactState s = make_slimed_test_state(7);
  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);
  EXPECT_EQ(s.get_discard()[sts2::game::CardId::kSlimed], 8)
      << "discard[kSlimed] must clamp at kMaxSlimedAccumulation, not overflow";
}

TEST(Transition, SlimedCap_BelowCapAddsNormally) {
  // Sanity: 0 Slimed + STICKY_SHOT(value=2) → 2 (well below cap).
  CompactState s = make_slimed_test_state(0);
  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);
  EXPECT_EQ(s.get_discard()[sts2::game::CardId::kSlimed], 2)
      << "discard[kSlimed] below cap must accumulate normally";
}

// ---------------------------------------------------------------------------
// Wave-24/K.α — new MoveEffectKind dispatch + enemy block decay
//
// kBuffEnemy + kBlockSelf are not yet referenced by any monster_moves table
// (Nibbit lands in K.β), so we drive them via the test_internals seam in
// transition.h. The seam mirrors do_enemy_act_slime's dispatch switch
// (kept in lockstep manually). EnemyBlock_DecaysAtEndOfEnemyTurn drives the
// full do_enemy_act path through resolve_end_turn_pre_draw using LeafSlimeM
// (data-driven path), then asserts block decay.
// ---------------------------------------------------------------------------

TEST(Transition, BuffEnemy_AddsStrengthStack) {
  // Synthesize a slime enemy with power_count=0 (no spawn powers). LeafSlimeM
  // kind is used purely as a non-cultist carrier; the test does not exercise
  // its move table.
  CompactState s = make_test_state();
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(sts2::game::Stat{30})
        .alive(true);
  });
  ASSERT_EQ(s.get_enemy(0).get_power_count(), 0U);

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect fx{
      .value = 2,
      .kind = sts2::game::MoveEffectKind::kBuffEnemy,
      .power_kind = sts2::game::PowerKind::kStrength,
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(s, e,
                                                                          fx);

  EXPECT_EQ(e.get_strength(), Stat{2});
  ASSERT_EQ(e.get_power_count(), 1U);
  EXPECT_EQ(e.get_powers()[0].kind, sts2::game::PowerKind::kStrength);
  EXPECT_EQ(e.get_powers()[0].stacks, 2);
}

TEST(Transition, BlockSelf_AccumulatesEnemyBlock) {
  CompactState s = make_test_state();
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(sts2::game::Stat{30})
        .block(Stat{0})
        .alive(true);
  });
  ASSERT_EQ(s.get_enemy(0).get_block(), Stat{0});

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect fx{
      .value = 5,
      .kind = sts2::game::MoveEffectKind::kBlockSelf,
      .power_kind = sts2::game::PowerKind::kWeak,  // unused for kBlockSelf
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(s, e,
                                                                          fx);

  EXPECT_EQ(e.get_block(), Stat{5});
}

TEST(Transition, EnemyBlock_DecaysAtEndOfEnemyTurn) {
  // Synthesize a LeafSlimeM enemy with residual block=10 from a prior turn.
  // Drive one full end-of-turn through resolve_end_turn_pre_draw; assert
  // block is 0 after the dispatch.
  //
  // Audit (wave-24/K.α): the upstream STS convention is "block decays at
  // START of each side's turn" — implemented in turn_flow.h:33 as
  // EndTurnOps::reset_enemy_block(slot) running BEFORE enemy_act(slot).
  // The test name uses "AtEndOfEnemyTurn" for spec lineage; the observable
  // semantic is identical (block is 0 by the time the next observation
  // point arrives). Nibbit's kBlockSelf will follow the same path: block
  // applied during do_enemy_act, persists across the player's turn, decays
  // at the NEXT enemy turn's pre-act reset.
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.energy(Stat{0}).hand(CardCounts{});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(sts2::game::Stat{30})
        .block(Stat{10})
        .alive(true)
        .move_index(1)  // STICKY_SHOT (kAddStatusCard; data-driven)
        .current_move(sts2::game::MoveId::kStickyShot)
        .performed_first_move(true);
  });
  // Slot 1: harmless cultist-default; not part of the assertion.
  update_enemy(
      s, 1, [](EnemyStateBuilder& enemy) { enemy.alive(false).hp(Stat{0}); });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_enemy(0).get_block(), Stat{0})
      << "enemy block must decay before next observation point "
         "(turn_flow.h reset_enemy_block runs pre-enemy-act)";
}

TEST(Transition, BuffEnemy_DoesNotTriggerRitualSideEffects) {
  // Synthesize a cultist with kRitual (just_applied=false, stacks=5) +
  // ritual_amount_=5. Apply kBuffEnemy(kStrength, +2) via test seam. Assert
  // just_applied flag NOT set on kRitual, ritual_amount_ unchanged, kStrength
  // stack accumulated to +2.
  CompactState s = make_test_state();
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kCultistCalcified)
        .hp(sts2::game::Stat{40})
        .alive(true)
        .ritual_amount(Stat{5})
        .add_power(sts2::game::PowerKind::kRitual, 5)
        .just_applied_ritual(false);
  });
  ASSERT_FALSE(s.get_enemy(0).get_just_applied_ritual());
  ASSERT_EQ(s.get_enemy(0).get_ritual_amount(), Stat{5});
  ASSERT_EQ(s.get_enemy(0).get_strength(), Stat{0});

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect fx{
      .value = 2,
      .kind = sts2::game::MoveEffectKind::kBuffEnemy,
      .power_kind = sts2::game::PowerKind::kStrength,
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(s, e,
                                                                          fx);

  // Strength stack accumulated.
  EXPECT_EQ(e.get_strength(), Stat{2});
  // Ritual side-effects NOT triggered.
  EXPECT_FALSE(e.get_just_applied_ritual())
      << "kBuffEnemy must not set kRitual.just_applied (no Ritual coupling)";
  EXPECT_EQ(e.get_ritual_amount(), Stat{5})
      << "kBuffEnemy must not mutate ritual_amount_";
  // Ritual stacks preserved.
  EXPECT_EQ(sts2::ai::powers::stacks_of(e.get_powers(), e.get_power_count(),
                                        sts2::game::PowerKind::kRitual),
            5);
}

TEST(Transition, EnemyStrength_AppliesToNonCultistAttack) {
  // Synthesize a non-cultist enemy (LeafSlimeM) with kStrength=5. Apply an
  // attack MoveEffect{kAttack, value=10} via test seam. Player has 0 block,
  // 70 hp. Expected: hp loss = 10 + 5 = 15 → player_hp = 55.
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{70}).player_block(Stat{0});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(sts2::game::Stat{30})
        .alive(true)
        .add_power(sts2::game::PowerKind::kStrength, 5);
  });
  ASSERT_EQ(s.get_enemy(0).get_strength(), Stat{5});

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect fx{
      .value = 10,
      .kind = sts2::game::MoveEffectKind::kAttack,
      .power_kind = sts2::game::PowerKind::kWeak,  // unused for kAttack
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(s, e,
                                                                          fx);

  EXPECT_EQ(s.get_player_hp(), Stat{55})
      << "enemy Strength must apply to non-cultist attack via "
         "compute_outgoing(base, strength, weak)";
}

// ---------------------------------------------------------------------------
// Wave-24/K.β-fix — Nibbit dispatch chain regression guard
//
// Nibbit (kind=kNibbit=7) previously fell through to the cultist default path
// in do_enemy_act because kind_is_slime() only covers the 4 slime kinds.
// act_on_intent's MoveId switch has no case for kButtMove/kSliceMove/kHissMove
// → silent no-op → Nibbit attacks never landed.
//
// Fix: kind_is_table_driven() now returns true for kNibbit, routing it
// through do_enemy_act_slime (table-driven dispatch).
//
// This test drives the full do_enemy_act path via resolve_end_turn_pre_draw
// (same pattern as EnemyBlock_DecaysAtEndOfEnemyTurn) with Nibbit at
// BUTT_MOVE (move_index=0, kAttack value=12). Player has 70 HP, 0 block.
// Expected player HP after Nibbit acts: 70 - 12 = 58.
// ---------------------------------------------------------------------------
TEST(Transition, NibbitBuff_ResolvesViaTableDispatch) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{70}).player_block(Stat{0}).energy(Stat{0}).hand(
        CardCounts{});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kNibbit)
        .hp(sts2::game::Stat{44})
        .block(Stat{0})
        .alive(true)
        .move_index(0)  // BUTT_MOVE (kAttack, value=12; Nibbit.cs:30)
        .current_move(sts2::game::MoveId::kButtMove)
        .performed_first_move(true);
  });
  // Slot 1: dead; not part of the assertion.
  update_enemy(
      s, 1, [](EnemyStateBuilder& enemy) { enemy.alive(false).hp(Stat{0}); });

  apply_or_fail(s, end_turn());
  s = resolve_end_turn_pre_draw(s);

  EXPECT_EQ(s.get_player_hp(), Stat{58})
      << "Nibbit BUTT_MOVE (kAttack,12) must land via table-driven dispatch "
         "(pre-fix: fell through to cultist default → silent no-op → HP=70)";
}

// 6. MultiHit_BlockDecrementsBetweenHits
// Validates the apply_to_defender mutation semantic: each hit consumes block
// first; excess spills to HP. Two sequential 7-dmg attacks on player block=5:
//   hit 1: 5 block absorbed + 2 hp loss; block→0, hp 70→68.
//   hit 2: block=0; 7 hp loss; hp 68→61.
TEST(Transition, MultiHit_BlockDecrementsBetweenHits) {
  sts2::game::Stat hp{70};
  sts2::game::Stat block{5};
  (void)sts2::damage::apply_to_defender(hp, block, 7);
  EXPECT_EQ(hp, Stat{68})
      << "hit 1: 5 block absorbs 5 dmg; 2 dmg spills to HP → 70-2 = 68";
  EXPECT_EQ(block, Stat{0}) << "hit 1: block fully consumed by 7-dmg attack";
  (void)sts2::damage::apply_to_defender(hp, block, 7);
  EXPECT_EQ(hp, Stat{61}) << "hit 2: no block, 7 dmg lands → 68-7 = 61";
  EXPECT_EQ(block, Stat{0});
}

// 7. MultiHit_PlayerDeathMidHits_ClampsAtZero
// Sequential lethal hits with no block: player HP clamps at 0; is_terminal
// returns true after the first lethal hit.
TEST(Transition, MultiHit_PlayerDeathMidHits_ClampsAtZero) {
  sts2::game::Stat hp{5};
  sts2::game::Stat block{0};
  (void)sts2::damage::apply_to_defender(hp, block, 6);
  EXPECT_EQ(hp, Stat{0}) << "HP must clamp at 0 (not negative) post-lethal";
  // Build a CompactState to drive is_terminal; player_hp=0 → terminal=true.
  const CompactState s = CompactStateBuilder{}
                             .player_hp(Stat{0})
                             .energy(Stat{0})
                             .round(1)
                             .phase(Phase::kPlayerActing)
                             .build();
  EXPECT_TRUE(is_terminal(s)) << "is_terminal must report true at HP=0";
  // Second 6-dmg hit on a dead player: damage_calc clamps so HP stays 0.
  (void)sts2::damage::apply_to_defender(hp, block, 6);
  EXPECT_EQ(hp, Stat{0}) << "HP must remain clamped at 0 (no underflow)";
}

// 8. MultiHit_PartialBlockInteraction
// Player block=5; two separate 7-dmg attacks. Each hit's excess-over-block
// spills to HP. Net hp loss over both hits: 2 (hit 1 excess) + 7 (hit 2) = 9.
TEST(Transition, MultiHit_PartialBlockInteraction) {
  sts2::game::Stat hp{70};
  sts2::game::Stat block{5};
  (void)sts2::damage::apply_to_defender(hp, block, 7);
  EXPECT_EQ(hp, Stat{68}) << "hit 1: 5 block + 2 spill → 70 - 2 = 68";
  EXPECT_EQ(block, Stat{0});
  (void)sts2::damage::apply_to_defender(hp, block, 7);
  EXPECT_EQ(hp, Stat{61}) << "hit 2: full 7 dmg lands → 68 - 7 = 61";
}

// 9. MultiHit_StrengthAppliesPerHit
// 2-effect attack with attacker Strength=2: each kAttack(7) becomes 9 dmg.
// Player has 0 block, 70 hp. Net loss: 9+9 = 18 → hp = 52.
TEST(Transition, MultiHit_StrengthAppliesPerHit) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{70}).player_block(Stat{0});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(Stat{30})
        .alive(true)
        .add_power(sts2::game::PowerKind::kStrength, 2);
  });

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect attack1{
      .value = 7,
      .kind = sts2::game::MoveEffectKind::kAttack,
      .power_kind = sts2::game::PowerKind::kWeak,
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(
      s, e, attack1);
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(
      s, e, attack1);
  EXPECT_EQ(s.get_player_hp(), Stat{52})
      << "Strength must apply per hit: (7+2)*2 = 18 dmg → 70-18 = 52";
}

// 10. HeheAttack_UsesPreBuffStrength
// HEHE pattern: [kAttack 8, kBuffEnemy +2 kStrength]. Player has 0 block,
// 70 hp. The attack effect uses the strength CURRENT at the moment of effect
// processing — Strength=0 at hit time (kBuffEnemy comes AFTER in the effects
// array) → 8 dmg lands. Post-state: enemy strength=2 (ready for next turn).
TEST(Transition, HeheAttack_UsesPreBuffStrength) {
  CompactState s = make_test_state();
  update_state(s, [](CompactStateBuilder& builder) {
    builder.player_hp(Stat{70}).player_block(Stat{0});
  });
  update_enemy(s, 0, [](EnemyStateBuilder& enemy) {
    enemy.kind(sts2::game::MonsterKind::kLeafSlimeM)
        .hp(Stat{30})
        .alive(true)
        .add_power(sts2::game::PowerKind::kStrength, 0);
  });

  EnemyState e = s.get_enemy(0);
  const sts2::game::monster_moves::MoveEffect attack{
      .value = 8,
      .kind = sts2::game::MoveEffectKind::kAttack,
      .power_kind = sts2::game::PowerKind::kWeak,
      ._pad = 0,
      ._pad2 = 0,
  };
  const sts2::game::monster_moves::MoveEffect buff{
      .value = 2,
      .kind = sts2::game::MoveEffectKind::kBuffEnemy,
      .power_kind = sts2::game::PowerKind::kStrength,
      ._pad = 0,
      ._pad2 = 0,
  };
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(
      s, e, attack);
  sts2::ai::transition::test_internals::apply_single_move_effect_for_test(s, e,
                                                                          buff);
  EXPECT_EQ(s.get_player_hp(), Stat{62})
      << "kAttack uses Strength snapshot at hit-time (0), not post-buff (2)";
  EXPECT_EQ(e.get_strength(), Stat{2})
      << "post-effect strength reflects the buff for next turn";
}

}  // namespace
