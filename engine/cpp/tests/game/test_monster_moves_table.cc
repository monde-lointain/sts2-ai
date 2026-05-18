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
  EXPECT_EQ(m.effects[0].value, int16_t{9});
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
  EXPECT_EQ(m.effects[0].value, int16_t{1});
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
  EXPECT_EQ(m.effects[0].value, int16_t{2});
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
  EXPECT_EQ(m.effects[0].value, int16_t{5});
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
  EXPECT_EQ(m.effects[0].value, int16_t{9});
  EXPECT_EQ(m.effects[1].kind, MoveEffectKind::kDebuffPlayer);
  EXPECT_EQ(m.effects[1].power_kind, PowerKind::kFrail);
  EXPECT_EQ(m.effects[1].value, int16_t{2});
}

TEST(MonsterMovesTable, LouseProgenitor_CurlAndGrow_DefendAndStrengthEffects) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[1];
  EXPECT_EQ(m.id, MoveId::kCurlAndGrow);
  ASSERT_EQ(m.effect_count, 2U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kDefend);
  EXPECT_EQ(m.effects[0].value, int16_t{14});
  EXPECT_EQ(m.effects[1].kind, MoveEffectKind::kBuffSelf);
  EXPECT_EQ(m.effects[1].power_kind, PowerKind::kStrength);
  EXPECT_EQ(m.effects[1].value, int16_t{5});
}

TEST(MonsterMovesTable, LouseProgenitor_Pounce_AttackEffect) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMove& m = kMonsterMoveTables[kind_idx].moves[2];
  EXPECT_EQ(m.id, MoveId::kPounce);
  ASSERT_EQ(m.effect_count, 1U);
  EXPECT_EQ(m.effects[0].kind, MoveEffectKind::kAttack);
  EXPECT_EQ(m.effects[0].value, int16_t{14});  // A0 baseline; wave-20.α fix
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
  EXPECT_EQ(t.spawn_powers[0].stacks, int16_t{14});
}

TEST(MonsterMovesTable, LouseProgenitor_HpRange_134_136) {
  const auto kind_idx = static_cast<std::size_t>(MonsterKind::kLouseProgenitor);
  const MonsterMoveTable& t = kMonsterMoveTables[kind_idx];
  EXPECT_EQ(t.min_hp, uint8_t{134});
  EXPECT_EQ(t.max_hp, uint8_t{136});
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

}  // namespace
