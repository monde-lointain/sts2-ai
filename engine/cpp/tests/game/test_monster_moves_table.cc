// Tests for engine/cpp/include/sts2/game/monster_moves.h and
// engine/cpp/src/game/monster_moves.cc.
//
// Wave-16 foundation: kMonsterMoveTables mirrors cultist values from
// enemies.h (kCultistArchetypes) verbatim.
// Wave-18: LouseProgenitor table fully populated (WEB_CANNON=0,
// CURL_AND_GROW=1, POUNCE=2).

#include <gtest/gtest.h>

#include "sts2/game/monster_moves.h"

namespace {

using sts2::game::MonsterKind;
using sts2::game::MoveEffectKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using namespace sts2::game::monster_moves;

// -------------------------------------------------------------------------
// CalcifiedCultist DarkStrike base damage
// Source: enemies.h kCultistArchetypes[0].dark_strike_base = 9
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, CalcifiedCultist_DarkStrikeIndex_IsOne) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistCalcified, MoveId::kDarkStrike);
  ASSERT_NE(idx, 0xFF);
  EXPECT_EQ(idx, 1U);
}

TEST(MonsterMovesTable, CalcifiedCultist_DarkStrike_DamageValue) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistCalcified, MoveId::kDarkStrike);
  ASSERT_NE(idx, 0xFF);
  const MonsterMove& m = kMonsterMoveTables[static_cast<std::size_t>(
                                                MonsterKind::kCultistCalcified)]
                             .moves[idx];
  EXPECT_EQ(m.effect_count, 1U);
  // enemies.h kCultistArchetypes[0].dark_strike_base = 9
  EXPECT_EQ(m.effects[0].value, int32_t{9});
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
}

// -------------------------------------------------------------------------
// DampCultist DarkStrike base damage
// Source: enemies.h kCultistArchetypes[1].dark_strike_base = 1
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, DampCultist_DarkStrike_DamageValue) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistDamp, MoveId::kDarkStrike);
  ASSERT_NE(idx, 0xFF);
  const MonsterMove& m =
      kMonsterMoveTables[static_cast<std::size_t>(MonsterKind::kCultistDamp)]
          .moves[idx];
  EXPECT_EQ(m.effect_count, 1U);
  // enemies.h kCultistArchetypes[1].dark_strike_base = 1
  EXPECT_EQ(m.effects[0].value, int32_t{1});
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
}

// -------------------------------------------------------------------------
// CalcifiedCultist Incantation: BuffSelf + kRitual + ritual_amount=2
// Source: enemies.h kCultistArchetypes[0].ritual_amount = 2
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, CalcifiedCultist_IncantationIndex_IsZero) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistCalcified, MoveId::kIncantation);
  ASSERT_NE(idx, 0xFF);
  EXPECT_EQ(idx, 0U);
}

TEST(MonsterMovesTable, CalcifiedCultist_Incantation_RitualEffect) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistCalcified, MoveId::kIncantation);
  ASSERT_NE(idx, 0xFF);
  const MonsterMove& m = kMonsterMoveTables[static_cast<std::size_t>(
                                                MonsterKind::kCultistCalcified)]
                             .moves[idx];
  EXPECT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kBuffSelf);
  EXPECT_EQ(m.effects[0].power_kind, PowerKind::kRitual);
  // enemies.h kCultistArchetypes[0].ritual_amount = 2
  EXPECT_EQ(m.effects[0].value, int32_t{2});
}

// -------------------------------------------------------------------------
// DampCultist Incantation: ritual_amount=5
// Source: enemies.h kCultistArchetypes[1].ritual_amount = 5
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, DampCultist_Incantation_RitualEffect) {
  const uint8_t idx =
      find_move_index(MonsterKind::kCultistDamp, MoveId::kIncantation);
  ASSERT_NE(idx, 0xFF);
  const MonsterMove& m =
      kMonsterMoveTables[static_cast<std::size_t>(MonsterKind::kCultistDamp)]
          .moves[idx];
  EXPECT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kBuffSelf);
  EXPECT_EQ(m.effects[0].power_kind, PowerKind::kRitual);
  // enemies.h kCultistArchetypes[1].ritual_amount = 5
  EXPECT_EQ(m.effects[0].value, int32_t{5});
}

// -------------------------------------------------------------------------
// find_move_index: WebCannon not in Cultist tables
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, FindMoveIndex_CalcifiedCultist_WebCannon_NotFound) {
  EXPECT_EQ(find_move_index(MonsterKind::kCultistCalcified, MoveId::kWebCannon),
            uint8_t{0xFF});
}

// -------------------------------------------------------------------------
// LouseProgenitor move table (wave-18)
// Source: engine/headless/.../Phase1Monsters.cs:157-190
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, LouseProgenitor_WebCannon_IsAtIndex0) {
  const uint8_t idx =
      find_move_index(MonsterKind::kLouseProgenitor, MoveId::kWebCannon);
  ASSERT_NE(idx, uint8_t{0xFF}) << "WEB_CANNON must be in the table";
  EXPECT_EQ(idx, 0U);
}

TEST(MonsterMovesTable, LouseProgenitor_CurlAndGrow_IsAtIndex1) {
  const uint8_t idx =
      find_move_index(MonsterKind::kLouseProgenitor, MoveId::kCurlAndGrow);
  ASSERT_NE(idx, uint8_t{0xFF}) << "CURL_AND_GROW must be in the table";
  EXPECT_EQ(idx, 1U);
}

TEST(MonsterMovesTable, LouseProgenitor_Pounce_IsAtIndex2) {
  const uint8_t idx =
      find_move_index(MonsterKind::kLouseProgenitor, MoveId::kPounce);
  ASSERT_NE(idx, uint8_t{0xFF}) << "POUNCE must be in the table";
  EXPECT_EQ(idx, 2U);
}

TEST(MonsterMovesTable, LouseProgenitor_WebCannon_AttackAndFrailEffects) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[0];
  EXPECT_EQ(m.id, MoveId::kWebCannon);
  ASSERT_EQ(m.effect_count, 2U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{9});
  EXPECT_EQ(m.effects[1].kind, MoveEffectKind::kDebuffPlayer);
  EXPECT_EQ(m.effects[1].power_kind, PowerKind::kFrail);
  EXPECT_EQ(m.effects[1].value, int32_t{2});
}

TEST(MonsterMovesTable, LouseProgenitor_CurlAndGrow_DefendAndStrengthEffects) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[1];
  EXPECT_EQ(m.id, MoveId::kCurlAndGrow);
  ASSERT_EQ(m.effect_count, 2U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kDefend);
  EXPECT_EQ(m.effects[0].value, int32_t{14});
  EXPECT_EQ(m.effects[1].kind, MoveEffectKind::kBuffSelf);
  EXPECT_EQ(m.effects[1].power_kind, PowerKind::kStrength);
  EXPECT_EQ(m.effects[1].value, int32_t{5});
}

TEST(MonsterMovesTable, LouseProgenitor_Pounce_AttackEffect) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[2];
  EXPECT_EQ(m.id, MoveId::kPounce);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{14});  // A0 baseline; wave-20.α fix
}

TEST(MonsterMovesTable, LouseProgenitor_InitialMove_IsWebCannon) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.initial_move_index, 0U);
  EXPECT_EQ(t.moves[t.initial_move_index].id, MoveId::kWebCannon);
}

TEST(MonsterMovesTable, LouseProgenitor_SpawnPower_CurlUp14) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  ASSERT_EQ(t.spawn_power_count, 1U);
  EXPECT_EQ(t.spawn_powers[0].kind, PowerKind::kCurlUp);
  EXPECT_EQ(t.spawn_powers[0].stacks, int32_t{14});
}

TEST(MonsterMovesTable, LouseProgenitor_HpRange_134_136) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, int32_t{134});
  EXPECT_EQ(t.max_hp, int32_t{136});
}

TEST(MonsterMovesTable, LouseProgenitor_MoveRotation_WebToGrowToPounce) {
  // follow_up_index chain:
  // WEB_CANNON(0)→CURL_AND_GROW(1)→POUNCE(2)→WEB_CANNON(0).
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.moves[0].follow_up_index,
            uint8_t{1});  // WEB_CANNON → CURL_AND_GROW
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{2});  // CURL_AND_GROW → POUNCE
  EXPECT_EQ(t.moves[2].follow_up_index, uint8_t{0});  // POUNCE → WEB_CANNON
}

// -------------------------------------------------------------------------
// Initial move index sanity: cultists start on kIncantation (index 0)
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, CalcifiedCultist_InitialMoveIndex_IsZero) {
  const MonsterMoveTable& t = kMonsterMoveTables[static_cast<std::size_t>(
      MonsterKind::kCultistCalcified)];
  EXPECT_EQ(t.initial_move_index, 0U);
  EXPECT_EQ(t.moves[0].id, MoveId::kIncantation);
}

TEST(MonsterMovesTable, DampCultist_InitialMoveIndex_IsZero) {
  const MonsterMoveTable& t =
      kMonsterMoveTables[static_cast<std::size_t>(MonsterKind::kCultistDamp)];
  EXPECT_EQ(t.initial_move_index, 0U);
  EXPECT_EQ(t.moves[0].id, MoveId::kIncantation);
}

// =========================================================================
// Wave-22.β: Slime move table data assertions
// Sources cited per field.
// =========================================================================

using sts2::game::monster_moves::FollowUpRule;

// -------------------------------------------------------------------------
// LeafSlimeS
// Source: LeafSlimeS.cs:20-39
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, LeafSlimeS_HpRange) {
  // LeafSlimeS.cs:20 (A0 min=11), :22 (A0 max=15)
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeS);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, int32_t{11});
  EXPECT_EQ(t.max_hp, int32_t{15});
}

TEST(MonsterMovesTable, LeafSlimeS_TackleAttack3) {
  // LeafSlimeS.cs:24 (TackleDamage A0=3), :31 (MoveState "TACKLE_MOVE")
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeS);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[0];
  EXPECT_EQ(m.id, MoveId::kTackleMove);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{3});
}

TEST(MonsterMovesTable, LeafSlimeS_GoopStatus1) {
  // LeafSlimeS.cs:32 ("GOOP_MOVE", StatusIntent(1)), :34-35
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeS);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[1];
  EXPECT_EQ(m.id, MoveId::kGoopMove);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAddStatusCard);
  EXPECT_EQ(m.effects[0].value, int32_t{1});
}

TEST(MonsterMovesTable, LeafSlimeS_RandomBranchAlternation) {
  // LeafSlimeS.cs:33-35: RandomBranchState, both AddBranch with CannotRepeat
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeS);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  ASSERT_EQ(t.move_count, 2U);
  // Both moves use kRandomBranchCannotRepeat
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kRandomBranchCannotRepeat);
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kRandomBranchCannotRepeat);
  // Both branches CannotRepeat
  EXPECT_EQ(t.moves[0].branch_count, 2U);
  EXPECT_TRUE(t.moves[0].branch_cannot_repeat[0]);
  EXPECT_TRUE(t.moves[0].branch_cannot_repeat[1]);
  // Uniform weights
  EXPECT_EQ(t.moves[0].branch_weights[0], uint8_t{1});
  EXPECT_EQ(t.moves[0].branch_weights[1], uint8_t{1});
}

TEST(MonsterMovesTable, LeafSlimeS_InitialMove_IsTackle) {
  // LeafSlimeS.cs:39: initial=randomBranchState; TACKLE is first in list
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeS);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.initial_move_index, 0U);
  EXPECT_EQ(t.moves[0].id, MoveId::kTackleMove);
}

// -------------------------------------------------------------------------
// LeafSlimeM
// Source: LeafSlimeM.cs:22-40
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, LeafSlimeM_HpRange) {
  // LeafSlimeM.cs:22 (A0 min=32), :24 (A0 max=35)
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, int32_t{32});
  EXPECT_EQ(t.max_hp, int32_t{35});
}

TEST(MonsterMovesTable, LeafSlimeM_ClumpShotAttack8) {
  // LeafSlimeM.cs:26 (ClumpDamage A0=8), :33 ("CLUMP_SHOT")
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeM);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[0];
  EXPECT_EQ(m.id, MoveId::kClumpShot);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{8});
}

TEST(MonsterMovesTable, LeafSlimeM_StickyShotStatus2) {
  // LeafSlimeM.cs:34 ("STICKY_SHOT", StatusIntent(2))
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeM);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[1];
  EXPECT_EQ(m.id, MoveId::kStickyShot);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAddStatusCard);
  EXPECT_EQ(m.effects[0].value, int32_t{2});
}

TEST(MonsterMovesTable, LeafSlimeM_StrictAlternation) {
  // LeafSlimeM.cs:34,36: FollowUpState chains CLUMP→STICKY→CLUMP
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  ASSERT_EQ(t.move_count, 2U);
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kStrict);
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{1});  // CLUMP → STICKY
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{0});  // STICKY → CLUMP
}

TEST(MonsterMovesTable, LeafSlimeM_InitialMove_IsStickyShot) {
  // LeafSlimeM.cs:40: MonsterMoveStateMachine(list, moveState2);
  // moveState2=STICKY_SHOT
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLeafSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.initial_move_index, 1U);
  EXPECT_EQ(t.moves[1].id, MoveId::kStickyShot);
}

// -------------------------------------------------------------------------
// TwigSlimeS
// Source: TwigSlimeS.cs:15-27
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, TwigSlimeS_HpRange) {
  // TwigSlimeS.cs:15 (A0 min=7), :17 (A0 max=11)
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeS);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, int32_t{7});
  EXPECT_EQ(t.max_hp, int32_t{11});
}

TEST(MonsterMovesTable, TwigSlimeS_TackleAttack4) {
  // TwigSlimeS.cs:19 (TackleDamage A0=4), :26 ("TACKLE_MOVE")
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeS);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[0];
  EXPECT_EQ(m.id, MoveId::kTackleMove);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{4});
}

TEST(MonsterMovesTable, TwigSlimeS_SelfLoopStrict) {
  // TwigSlimeS.cs:27: moveState.FollowUpState = moveState (self-loop)
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeS);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  ASSERT_EQ(t.move_count, 1U);
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{0});  // self-loop
}

// -------------------------------------------------------------------------
// TwigSlimeM
// Source: TwigSlimeM.cs:23-42
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, TwigSlimeM_HpRange) {
  // TwigSlimeM.cs:23 (A0 min=26), :25 (A0 max=28)
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, int32_t{26});
  EXPECT_EQ(t.max_hp, int32_t{28});
}

TEST(MonsterMovesTable, TwigSlimeM_PokeyPounceAttack11) {
  // TwigSlimeM.cs:27 (ClumpDamage A0=11), :34 ("POKEY_POUNCE_MOVE")
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[0];
  EXPECT_EQ(m.id, MoveId::kPokeyPounce);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int32_t{11});
}

TEST(MonsterMovesTable, TwigSlimeM_StickyShotStatus1) {
  // TwigSlimeM.cs:35 ("STICKY_SHOT_MOVE", StatusIntent(1))
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[1];
  EXPECT_EQ(m.id, MoveId::kStickyShot);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAddStatusCard);
  EXPECT_EQ(m.effects[0].value, int32_t{1});
}

TEST(MonsterMovesTable, TwigSlimeM_WeightedRandomBranch) {
  // TwigSlimeM.cs:37 (AddBranch(moveState, 2)), :38 (AddBranch(moveState2,
  // CannotRepeat))
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  ASSERT_EQ(t.move_count, 2U);
  EXPECT_EQ(t.moves[0].follow_up_rule,
            FollowUpRule::kWeightedRandomCannotRepeat);
  EXPECT_EQ(t.moves[1].follow_up_rule,
            FollowUpRule::kWeightedRandomCannotRepeat);
  // Branch count and weights
  EXPECT_EQ(t.moves[0].branch_count, 2U);
  EXPECT_EQ(t.moves[0].branch_weights[0], uint8_t{2});  // POKEY weight=2
  EXPECT_EQ(t.moves[0].branch_weights[1], uint8_t{1});  // STICKY weight=1
  // CannotRepeat flags: POKEY=false, STICKY=true
  EXPECT_FALSE(t.moves[0].branch_cannot_repeat[0]);
  EXPECT_TRUE(t.moves[0].branch_cannot_repeat[1]);
}

TEST(MonsterMovesTable, TwigSlimeM_InitialMove_IsStickyShot) {
  // TwigSlimeM.cs:42: MonsterMoveStateMachine(list, moveState2);
  // moveState2=STICKY_SHOT_MOVE
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.initial_move_index, 1U);
  EXPECT_EQ(t.moves[1].id, MoveId::kStickyShot);
}

TEST(MonsterMovesTable, TwigSlimeM_NoSpawnPowers) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kTwigSlimeM);
  EXPECT_EQ(kMonsterMoveTables[kind_idx].spawn_power_count, 0U);
}

// =========================================================================
// Wave-24/K.β: Nibbit move table assertions
// Sources cited per field from upstream Nibbit.cs (A0 baseline).
// All values from GetValueIfAscension(AscensionLevel, AscensionVal, A0Val) —
// the A0 (third) argument is used throughout for Q2 Phase-1A (A0 only).
// =========================================================================

// A0 upstream cross-check (Nibbit.cs lines cited inline):
//   MinInitialHp A0 = 42  (Nibbit.cs:26)
//   MaxInitialHp A0 = 46  (Nibbit.cs:28)
//   ButtDamage   A0 = 12  (Nibbit.cs:30)
//   SliceDamage  A0 = 6   (Nibbit.cs:34)
//   SliceBlock   A0 = 5   (Nibbit.cs:32)
//   HissStrGain  A0 = 2   (Nibbit.cs:36)
//   Move cycle: BUTT→SLICE→HISS→BUTT (Nibbit.cs:84-86 FollowUpState chains)
//   Wire names: "BUTT_MOVE","SLICE_MOVE","HISS_MOVE" (Nibbit.cs:71-73)

TEST(MonsterMoves, NibbitTable_MatchesUpstream) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kNibbit);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];

  // HP range — Nibbit.cs:26 (A0 min=42), :28 (A0 max=46)
  EXPECT_EQ(t.min_hp, int32_t{42});
  EXPECT_EQ(t.max_hp, int32_t{46});

  // move_count and no spawn powers
  EXPECT_EQ(t.move_count, uint8_t{3});
  EXPECT_EQ(t.spawn_power_count, uint8_t{0});

  // --- Move 0: BUTT_MOVE ---
  // Nibbit.cs:71: MoveState("BUTT_MOVE", ...); :30 ButtDamage A0=12
  // follow_up chain: BUTT→SLICE (Nibbit.cs:85
  // moveState.FollowUpState=moveState2)
  EXPECT_EQ(t.moves[0].id, MoveId::kButtMove);
  ASSERT_EQ(t.moves[0].effect_count, uint8_t{1});
  EXPECT_EQ(t.moves[0].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[0].effects[0].value, int32_t{12});
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{1});  // → SLICE_MOVE
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 1: SLICE_MOVE ---
  // Nibbit.cs:72: MoveState("SLICE_MOVE", ...); :34 SliceDamage A0=6;
  //               :32 SliceBlock A0=5.
  // follow_up chain: SLICE→HISS (Nibbit.cs:84
  // moveState2.FollowUpState=moveState3)
  EXPECT_EQ(t.moves[1].id, MoveId::kSliceMove);
  ASSERT_EQ(t.moves[1].effect_count, uint8_t{2});
  EXPECT_EQ(t.moves[1].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[1].effects[0].value, int32_t{6});  // SliceDamage A0
  EXPECT_EQ(t.moves[1].effects[1].kind, MoveEffectKind::kBlockSelf);
  EXPECT_EQ(t.moves[1].effects[1].value, int32_t{5});  // SliceBlock A0
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{2});   // → HISS_MOVE
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 2: HISS_MOVE ---
  // Nibbit.cs:73: MoveState("HISS_MOVE", ...); :36 HissStrengthGain A0=2.
  // follow_up chain: HISS→BUTT (Nibbit.cs:86
  // moveState3.FollowUpState=moveState)
  EXPECT_EQ(t.moves[2].id, MoveId::kHissMove);
  ASSERT_EQ(t.moves[2].effect_count, uint8_t{1});
  EXPECT_EQ(t.moves[2].effects[0].kind, MoveEffectKind::kBuffEnemy);
  EXPECT_EQ(t.moves[2].effects[0].value, int32_t{2});  // HissStrengthGain A0
  EXPECT_EQ(t.moves[2].effects[0].power_kind, PowerKind::kStrength);
  EXPECT_EQ(t.moves[2].follow_up_index, uint8_t{0});  // → BUTT_MOVE (cycle)
  EXPECT_EQ(t.moves[2].follow_up_rule, FollowUpRule::kStrict);

  // initial_move_index = 0 (BUTT; encounter-specific factories override
  // current_move but build_enemy_state resolves move_index via find_move_index)
  EXPECT_EQ(t.initial_move_index, uint8_t{0});
}

// =========================================================================
// Wave-26/M.β: GremlinMerc encounter move table assertions
// Sources cited per field from upstream {GremlinMerc, SneakyGremlin,
// FatGremlin, SurprisePower}.cs (A0 baseline). All values from
// GetValueIfAscension(AscensionLevel, AscensionVal, A0Val) — the A0 (third)
// argument is used throughout for Q2 Phase-1A (A0 only).
// =========================================================================

// A0 upstream cross-check (cite lines per value):
//   GremlinMerc.cs:28    MinInitialHp A0=47
//   GremlinMerc.cs:30    MaxInitialHp A0=49
//   GremlinMerc.cs:36    GimmeDamage A0=7
//   GremlinMerc.cs:38    GimmeRepeat=2
//   GremlinMerc.cs:40    DoubleSmashDamage A0=6
//   GremlinMerc.cs:42    DoubleSmashRepeat=2
//   GremlinMerc.cs:44    HeheDamage A0=8
//   GremlinMerc.cs:49    AfterAddedToRoom applies SurprisePower(1m)
//   GremlinMerc.cs:54    AfterAddedToRoom applies ThieveryPower(20m)
//                        — DROPPED at data layer (Q2 combat-only)
//   GremlinMerc.cs:61    MoveState("GIMME_MOVE", ...)
//   GremlinMerc.cs:62    MoveState("DOUBLE_SMASH_MOVE", ..., DebuffIntent())
//   GremlinMerc.cs:63    MoveState("HEHE_MOVE", ..., BuffIntent())
//   GremlinMerc.cs:64-66 FollowUpState chain GIMME→DOUBLE_SMASH→HEHE→GIMME
//   GremlinMerc.cs:70    initial state = GIMME
//   GremlinMerc.cs:109   DOUBLE_SMASH applies Weak(2m) AFTER both attacks
//   GremlinMerc.cs:122   HEHE applies Strength(2m) AFTER attack
//   SurprisePower.cs:22  OnDeath spawns SneakyGremlin "sneaky"
//   SurprisePower.cs:23  OnDeath spawns FatGremlin "fat"

TEST(MonsterMoves, GremlinMercTable_MatchesUpstream) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kGremlinMerc);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];

  // HP range — GremlinMerc.cs:28 (A0 min=47), :30 (A0 max=49)
  EXPECT_EQ(t.min_hp, int32_t{47});
  EXPECT_EQ(t.max_hp, int32_t{49});

  // move_count = 3 (GIMME, DOUBLE_SMASH, HEHE)
  EXPECT_EQ(t.move_count, uint8_t{3});

  // spawn_power_count = 1 (kSurprise(1); kThievery DROPPED at data layer)
  ASSERT_EQ(t.spawn_power_count, uint8_t{1});
  EXPECT_EQ(t.spawn_powers[0].kind, PowerKind::kSurprise);
  EXPECT_EQ(t.spawn_powers[0].stacks,
            int32_t{1});  // GremlinMerc.cs:49 SurprisePower(1m)

  // --- Move 0: GIMME_MOVE ---
  // GremlinMerc.cs:61 ("GIMME_MOVE"); :36 GimmeDamage A0=7;
  //   :38 GimmeRepeat=2 → 2 sequential kAttack effects (each 7 dmg).
  // follow_up: GIMME → DOUBLE_SMASH (GremlinMerc.cs:64
  //   moveState.FollowUpState=moveState2).
  EXPECT_EQ(t.moves[0].id, MoveId::kGimmeMove);
  ASSERT_EQ(t.moves[0].effect_count, uint8_t{2});
  EXPECT_EQ(t.moves[0].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[0].effects[0].value, int32_t{7});  // GimmeDamage A0
  EXPECT_EQ(t.moves[0].effects[1].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[0].effects[1].value, int32_t{7});
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{1});  // → DOUBLE_SMASH
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 1: DOUBLE_SMASH_MOVE ---
  // GremlinMerc.cs:62 ("DOUBLE_SMASH_MOVE"); :40 DoubleSmashDamage A0=6;
  //   :42 DoubleSmashRepeat=2 → 2 sequential kAttack effects.
  // :109 PowerCmd.Apply<WeakPower>(... 2m ...) AFTER damage resolves →
  //   kDebuffPlayer(kWeak, 2) at effects[2]. Order MATTERS.
  // follow_up: DOUBLE_SMASH → HEHE (GremlinMerc.cs:65
  //   moveState2.FollowUpState=moveState3).
  EXPECT_EQ(t.moves[1].id, MoveId::kDoubleSmashMove);
  ASSERT_EQ(t.moves[1].effect_count, uint8_t{3});
  EXPECT_EQ(t.moves[1].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[1].effects[0].value, int32_t{6});  // DoubleSmashDamage A0
  EXPECT_EQ(t.moves[1].effects[1].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[1].effects[1].value, int32_t{6});
  EXPECT_EQ(t.moves[1].effects[2].kind, MoveEffectKind::kDebuffPlayer);
  EXPECT_EQ(t.moves[1].effects[2].power_kind, PowerKind::kWeak);
  EXPECT_EQ(t.moves[1].effects[2].value,
            int32_t{2});  // WeakPower stacks (GremlinMerc.cs:109)
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{2});  // → HEHE
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 2: HEHE_MOVE ---
  // GremlinMerc.cs:63 ("HEHE_MOVE"); :44 HeheDamage A0=8; :122
  //   PowerCmd.Apply<StrengthPower>(... 2m ...) AFTER attack →
  //   kBuffEnemy(kStrength, 2) at effects[1]. Order MATTERS — this turn's
  //   attack uses PRE-buff Strength.
  // follow_up: HEHE → GIMME (GremlinMerc.cs:66
  //   moveState3.FollowUpState=moveState).
  EXPECT_EQ(t.moves[2].id, MoveId::kHeheMove);
  ASSERT_EQ(t.moves[2].effect_count, uint8_t{2});
  EXPECT_EQ(t.moves[2].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[2].effects[0].value, int32_t{8});  // HeheDamage A0
  EXPECT_EQ(t.moves[2].effects[1].kind, MoveEffectKind::kBuffEnemy);
  EXPECT_EQ(t.moves[2].effects[1].power_kind, PowerKind::kStrength);
  EXPECT_EQ(t.moves[2].effects[1].value,
            int32_t{2});  // StrengthPower stacks (GremlinMerc.cs:122)
  EXPECT_EQ(t.moves[2].follow_up_index, uint8_t{0});  // → GIMME (cycle)
  EXPECT_EQ(t.moves[2].follow_up_rule, FollowUpRule::kStrict);

  // initial_move_index = 0 (GIMME; GremlinMerc.cs:70 initial state)
  EXPECT_EQ(t.initial_move_index, uint8_t{0});

  // OnDeath spawn count = 2 (SneakyGremlin + FatGremlin)
  // Detailed spawn-table assertions live in SurpriseSpawnTable_MatchesUpstream.
  EXPECT_EQ(t.on_death_spawn_count, uint8_t{2});
}

// A0 upstream cross-check for SneakyGremlin:
//   SneakyGremlin.cs:21  MinInitialHp A0=10
//   SneakyGremlin.cs:23  MaxInitialHp A0=14
//   SneakyGremlin.cs:25  TackleDamage A0=9
//                        (GetValueIfAscension(DeadlyEnemies, 10, 9))
//   SneakyGremlin.cs:49  MoveState("SPAWNED_MOVE", ..., StunIntent())
//   SneakyGremlin.cs:50  MoveState("TACKLE_MOVE", ...,
//                        SingleAttackIntent(TackleDamage))
//   SneakyGremlin.cs:51  moveState2.FollowUpState = moveState2 (self-loop)
//   SneakyGremlin.cs:54  initial state = SPAWNED

TEST(MonsterMoves, SneakyGremlinTable_MatchesUpstream) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kSneakyGremlin);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];

  // HP range — SneakyGremlin.cs:21 (A0 min=10), :23 (A0 max=14)
  EXPECT_EQ(t.min_hp, int32_t{10});
  EXPECT_EQ(t.max_hp, int32_t{14});

  // move_count = 2 (SPAWNED, TACKLE); no spawn powers; no OnDeath
  EXPECT_EQ(t.move_count, uint8_t{2});
  EXPECT_EQ(t.spawn_power_count, uint8_t{0});
  EXPECT_EQ(t.on_death_spawn_count, uint8_t{0});

  // --- Move 0: SPAWNED_MOVE ---
  // SneakyGremlin.cs:49: StunIntent → effect_count=0 (no oracle semantics).
  // follow_up: SPAWNED → TACKLE (SneakyGremlin.cs:50, FollowUpState chain).
  EXPECT_EQ(t.moves[0].id, MoveId::kSpawnedMove);
  EXPECT_EQ(t.moves[0].effect_count, uint8_t{0});
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{1});  // → TACKLE
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 1: TACKLE_MOVE ---
  // SneakyGremlin.cs:25: TackleDamage A0=9 (3rd arg of
  //   GetValueIfAscension(DeadlyEnemies, 10, 9) — A0 NOT 10); :50
  //   SingleAttackIntent(TackleDamage); :51 self-loop.
  // MoveId::kTackleMove REUSED from wave-22 slime port — per-monster table
  //   stores damage value so slime TACKLE (3-4) and SneakyGremlin TACKLE (9)
  //   coexist on the same MoveId.
  EXPECT_EQ(t.moves[1].id, MoveId::kTackleMove);
  ASSERT_EQ(t.moves[1].effect_count, uint8_t{1});
  EXPECT_EQ(t.moves[1].effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(t.moves[1].effects[0].value, int32_t{9});  // TackleDamage A0
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{1});   // self-loop
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kStrict);

  // initial_move_index = 0 (SPAWNED; SneakyGremlin.cs:54)
  EXPECT_EQ(t.initial_move_index, uint8_t{0});
}

// A0 upstream cross-check for FatGremlin:
//   FatGremlin.cs:28  MinInitialHp A0=13
//   FatGremlin.cs:30  MaxInitialHp A0=17
//   FatGremlin.cs:52  MoveState("SPAWNED_MOVE", ..., StunIntent())
//   FatGremlin.cs:53  MoveState("FLEE_MOVE", ..., EscapeIntent())
//   FatGremlin.cs:54  moveState2.FollowUpState = moveState2 (self-loop)
//   FatGremlin.cs:57  initial state = SPAWNED
//   FatGremlin.cs:75  CreatureCmd.Escape(...) removes the creature from
//                     combat — modeled as MoveEffectKind::kFleeSelf in Q2

TEST(MonsterMoves, FatGremlinTable_MatchesUpstream) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kFatGremlin);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];

  // HP range — FatGremlin.cs:28 (A0 min=13), :30 (A0 max=17)
  EXPECT_EQ(t.min_hp, int32_t{13});
  EXPECT_EQ(t.max_hp, int32_t{17});

  // move_count = 2 (SPAWNED, FLEE); no spawn powers; no OnDeath
  EXPECT_EQ(t.move_count, uint8_t{2});
  EXPECT_EQ(t.spawn_power_count, uint8_t{0});
  EXPECT_EQ(t.on_death_spawn_count, uint8_t{0});

  // --- Move 0: SPAWNED_MOVE ---
  // FatGremlin.cs:52: StunIntent → effect_count=0 (no oracle semantics).
  // follow_up: SPAWNED → FLEE (FatGremlin.cs:53, FollowUpState chain).
  EXPECT_EQ(t.moves[0].id, MoveId::kSpawnedMove);
  EXPECT_EQ(t.moves[0].effect_count, uint8_t{0});
  EXPECT_EQ(t.moves[0].follow_up_index, uint8_t{1});  // → FLEE
  EXPECT_EQ(t.moves[0].follow_up_rule, FollowUpRule::kStrict);

  // --- Move 1: FLEE_MOVE ---
  // FatGremlin.cs:53 ("FLEE_MOVE", EscapeIntent()); :54 self-loop; :75
  //   CreatureCmd.Escape removes the carrier. Modeled as
  //   MoveEffectKind::kFleeSelf — dispatch in transition.cc sets
  //   M::alive(e)=false directly without routing through the OnDeath helper.
  EXPECT_EQ(t.moves[1].id, MoveId::kFleeMove);
  ASSERT_EQ(t.moves[1].effect_count, uint8_t{1});
  EXPECT_EQ(t.moves[1].effects[0].kind, MoveEffectKind::kFleeSelf);
  EXPECT_EQ(t.moves[1].follow_up_index, uint8_t{1});  // self-loop
  EXPECT_EQ(t.moves[1].follow_up_rule, FollowUpRule::kStrict);

  // initial_move_index = 0 (SPAWNED; FatGremlin.cs:57)
  EXPECT_EQ(t.initial_move_index, uint8_t{0});
}

// SurpriseSpawnTable_MatchesUpstream (BAKED H round-2):
//   Asserts kGremlinMerc.on_death_spawns content matches
//   SurprisePower.cs:22-23 (Sneaky + Fat at B1 deterministic median HPs);
//   asserts every OTHER MonsterKind has on_death_spawn_count=0 so a future
//   typo in monster_moves.cc can't accidentally cross-contaminate spawn
//   tables across kinds.

TEST(MonsterMoves, SurpriseSpawnTable_MatchesUpstream) {
  // kGremlinMerc: 2 spawns per SurprisePower.cs:22-23
  const auto merc_idx = static_cast<std::size_t>(MonsterKind::kGremlinMerc);
  const MonsterMoveTable& merc = kMonsterMoveTables[merc_idx];
  ASSERT_EQ(merc.on_death_spawn_count, uint8_t{2});

  // Spawn 0: SneakyGremlin "sneaky" (SurprisePower.cs:22). HP=12 = B1 median
  //   of [10,14] (SneakyGremlin.cs:21,23). Initial move = SPAWNED_MOVE
  //   (SneakyGremlin.cs:54 initial state).
  EXPECT_EQ(merc.on_death_spawns[0].kind, MonsterKind::kSneakyGremlin);
  EXPECT_EQ(merc.on_death_spawns[0].deterministic_hp, int32_t{12});
  EXPECT_EQ(merc.on_death_spawns[0].initial_current_move, MoveId::kSpawnedMove);

  // Spawn 1: FatGremlin "fat" (SurprisePower.cs:23). HP=15 = B1 median of
  //   [13,17] (FatGremlin.cs:28,30). Initial move = SPAWNED_MOVE
  //   (FatGremlin.cs:57).
  EXPECT_EQ(merc.on_death_spawns[1].kind, MonsterKind::kFatGremlin);
  EXPECT_EQ(merc.on_death_spawns[1].deterministic_hp, int32_t{15});
  EXPECT_EQ(merc.on_death_spawns[1].initial_current_move, MoveId::kSpawnedMove);

  // All other MonsterKinds: on_death_spawn_count = 0 (prevents accidental
  //   cross-kind spawn-table contamination). Iterate the catalog
  //   ex-kGremlinMerc and assert.
  constexpr std::array<MonsterKind, 10> kOtherKinds = {
      MonsterKind::kCultistCalcified, MonsterKind::kCultistDamp,
      MonsterKind::kLouseProgenitor,  MonsterKind::kLeafSlimeS,
      MonsterKind::kLeafSlimeM,       MonsterKind::kTwigSlimeS,
      MonsterKind::kTwigSlimeM,       MonsterKind::kNibbit,
      MonsterKind::kSneakyGremlin,    MonsterKind::kFatGremlin,
  };
  for (const MonsterKind k : kOtherKinds) {
    const auto idx = static_cast<std::size_t>(k);
    const MonsterMoveTable& tk = kMonsterMoveTables[idx];
    EXPECT_EQ(tk.on_death_spawn_count, uint8_t{0})
        << "on_death_spawn_count must be 0 for non-kGremlinMerc kind "
        << static_cast<int>(k);
  }
}

}  // namespace
