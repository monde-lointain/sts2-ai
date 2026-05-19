// Tests for nibbits_weak_projection.h/.cc (wave-24/K.γ_setup).
// Fixture #7: 07-nibbits-weak-seed42.
// Covers detection (happy + 4 rejects) + projection sanity (4 checks).

#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/nibbits_weak_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::oracle::adapter::is_nibbits_weak;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::ParsedCreature;
using sts2::oracle::adapter::project_nibbits_weak;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

ParsedCombatState parse_fixture(const std::string& subdir) {
  const auto bytes = load_fixture_blob(subdir);
  return read_state_blob(bytes).combat_state;
}

// -------------------------------------------------------------------------
// Detection
// -------------------------------------------------------------------------

TEST(NibbitsWeakDetection, Fixture7_IsNibbitsWeak) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  EXPECT_TRUE(is_nibbits_weak(combat));
}

TEST(NibbitsWeakDetection, Fixture1_CultistBlob_IsNotNibbitsWeak) {
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  EXPECT_FALSE(is_nibbits_weak(combat));
}

TEST(NibbitsWeakDetection, Fixture5_LouseBlob_IsNotNibbitsWeak) {
  const auto combat = parse_fixture("05-louse-progenitor-normal-seed42");
  EXPECT_FALSE(is_nibbits_weak(combat));
}

TEST(NibbitsWeakDetection, Fixture8_TwoNibbits_IsNotNibbitsWeak) {
  const auto combat = parse_fixture("08-nibbits-normal-seed42");
  EXPECT_FALSE(is_nibbits_weak(combat));
}

TEST(NibbitsWeakDetection, Synthetic_DeadNibbit_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 1;
  ParsedCreature e;
  e.name = "Nibbit";
  e.current_hp = 0;
  c.enemies.push_back(e);
  EXPECT_FALSE(is_nibbits_weak(c));
}

// -------------------------------------------------------------------------
// Projection: fixture #7 happy path
// -------------------------------------------------------------------------

TEST(NibbitsWeakProjection, Fixture7_EnemyCount1) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  ASSERT_TRUE(is_nibbits_weak(combat));
  const CompactState s = project_nibbits_weak(combat);
  EXPECT_TRUE(s.get_enemy(0).get_alive());
}

TEST(NibbitsWeakProjection, Fixture7_EnemyKindIsNibbit) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  const CompactState s = project_nibbits_weak(combat);
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kNibbit);
}

TEST(NibbitsWeakProjection, Fixture7_InitialMoveIsButtMove) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  const CompactState s = project_nibbits_weak(combat);
  // Q1 emits BUTT_MOVE for single-Nibbit (IsAlone=true, initial_move_index=0).
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kButtMove);
  EXPECT_FALSE(s.get_enemy(0).get_performed_first_move());
}

TEST(NibbitsWeakProjection, Fixture7_InitialMoveIndexZero) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  const CompactState s = project_nibbits_weak(combat);
  // BUTT_MOVE is at index 0 in the Nibbit move table.
  EXPECT_EQ(s.get_enemy(0).get_move_index(), uint8_t{0});
}

TEST(NibbitsWeakProjection, Fixture7_SilentStarterPlayerState) {
  const auto combat = parse_fixture("07-nibbits-weak-seed42");
  const CompactState s = project_nibbits_weak(combat);
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
