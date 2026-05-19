// Tests for nibbits_normal_projection.h/.cc (wave-24/K.γ_setup).
// Fixture #8: 08-nibbits-normal-seed42.
// Covers detection (happy + 5 rejects) + projection sanity (2-slot ordering).

#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/nibbits_normal_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::oracle::adapter::is_nibbits_normal;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::ParsedCreature;
using sts2::oracle::adapter::project_nibbits_normal;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

ParsedCombatState parse_fixture(const std::string& subdir) {
  const auto bytes = load_fixture_blob(subdir);
  return read_state_blob(bytes).combat_state;
}

// -------------------------------------------------------------------------
// Detection
// -------------------------------------------------------------------------

TEST(NibbitsNormalDetection, Fixture8_IsNibbitsNormal) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  EXPECT_TRUE(is_nibbits_normal(combat));
}

TEST(NibbitsNormalDetection, Fixture7_SingleNibbit_IsNotNibbitsNormal) {
  // NibbitsWeak (1 Nibbit) must not match NibbitsNormal (2 Nibbits).
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  EXPECT_FALSE(is_nibbits_normal(combat));
}

TEST(NibbitsNormalDetection, Fixture1_Cultists_IsNotNibbitsNormal) {
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  EXPECT_FALSE(is_nibbits_normal(combat));
}

TEST(NibbitsNormalDetection, Synthetic_ZeroEnemies_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 0;
  EXPECT_FALSE(is_nibbits_normal(c));
}

TEST(NibbitsNormalDetection, Synthetic_OneDeadNibbit_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 2;
  ParsedCreature e;
  e.name = "Nibbit";
  e.current_hp = 0;
  c.enemies.push_back(e);
  ParsedCreature e2;
  e2.name = "Nibbit";
  e2.current_hp = 44;
  c.enemies.push_back(e2);
  EXPECT_FALSE(is_nibbits_normal(c));
}

TEST(NibbitsNormalDetection, Synthetic_MixedNames_Rejected) {
  // 2 enemies but one isn't Nibbit → not NibbitsNormal.
  ParsedCombatState c;
  c.enemy_count = 2;
  ParsedCreature e1;
  e1.name = "Nibbit";
  e1.current_hp = 44;
  ParsedCreature e2;
  e2.name = "LouseProgenitor";
  e2.current_hp = 135;
  c.enemies.push_back(e1);
  c.enemies.push_back(e2);
  EXPECT_FALSE(is_nibbits_normal(c));
}

// -------------------------------------------------------------------------
// Projection: fixture #8 happy path — 2-slot ordering verification
// -------------------------------------------------------------------------

TEST(NibbitsNormalProjection, Fixture8_BothEnemiesAlive) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  ASSERT_TRUE(is_nibbits_normal(combat));
  const CompactState s = project_nibbits_normal(combat);
  EXPECT_TRUE(s.get_enemy(0).get_alive());
  EXPECT_TRUE(s.get_enemy(1).get_alive());
}

TEST(NibbitsNormalProjection, Fixture8_BothSlotsKindNibbit) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kNibbit);
  EXPECT_EQ(s.get_enemy(1).get_kind(), MonsterKind::kNibbit);
}

TEST(NibbitsNormalProjection, Fixture8_Slot0_InitialMoveSlice) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  // Q1 fixture 08: front Nibbit (slot 0) starts with SLICE_MOVE.
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kSliceMove);
  EXPECT_FALSE(s.get_enemy(0).get_performed_first_move());
}

TEST(NibbitsNormalProjection, Fixture8_Slot1_InitialMoveHiss) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  // Q1 fixture 08: back Nibbit (slot 1) starts with HISS_MOVE.
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kHissMove);
  EXPECT_FALSE(s.get_enemy(1).get_performed_first_move());
}

TEST(NibbitsNormalProjection, Fixture8_Slot0_MoveIndexIsSliceIndex) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  // SLICE_MOVE is index 1 in the Nibbit move table
  // (BUTT=0, SLICE=1, HISS=2).
  EXPECT_EQ(s.get_enemy(0).get_move_index(), uint8_t{1});
}

TEST(NibbitsNormalProjection, Fixture8_Slot1_MoveIndexIsHissIndex) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  // HISS_MOVE is index 2 in the Nibbit move table.
  EXPECT_EQ(s.get_enemy(1).get_move_index(), uint8_t{2});
}

TEST(NibbitsNormalProjection, Fixture8_SilentStarterPlayerState) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  const CompactState s = project_nibbits_normal(combat);
  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_player_strength(), Stat{0});
  EXPECT_EQ(s.get_player_weak(), Stat{0});
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1U);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);
  // Silent 12-card starter deck; Ring-of-the-Snake: hand=7 on round 1.
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);
  EXPECT_EQ(s.get_hand().total(), 7);
}

}  // namespace
