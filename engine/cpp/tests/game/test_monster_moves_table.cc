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

}  // namespace
