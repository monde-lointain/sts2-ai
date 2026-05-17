// Tests for engine/cpp/include/sts2/game/monster_moves.h and
// engine/cpp/src/game/monster_moves.cc.
//
// Wave-16 foundation: kMonsterMoveTables mirrors cultist values from
// enemies.h (kCultistArchetypes) verbatim. LouseProgenitor is a
// zero-initialized placeholder; wave-17 populates it.

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
// find_move_index: LouseProgenitor placeholder table has no moves
// -------------------------------------------------------------------------

TEST(MonsterMovesTable, FindMoveIndex_LouseProgenitor_WebCannon_NotFound) {
  // LouseProgenitor table is a zero-initialized placeholder; wave-17 populates.
  EXPECT_EQ(find_move_index(MonsterKind::kLouseProgenitor, MoveId::kWebCannon),
            uint8_t{0xFF});
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
