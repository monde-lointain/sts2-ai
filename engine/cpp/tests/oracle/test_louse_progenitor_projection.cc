// Tests for louse_progenitor_projection.h/.cc (wave-18).
// Fixture #5: 05-louse-progenitor-normal-seed42.
// Covers detection, projection sanity, CurlUp synthesis, and adapter facade.

#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/louse_progenitor_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using sts2::game::Stat;
using sts2::oracle::adapter::is_louse_progenitor_normal;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::ParsedCreature;
using sts2::oracle::adapter::project_louse_progenitor_normal;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

ParsedCombatState parse_fixture5() {
  const auto bytes = load_fixture_blob("05-louse-progenitor-normal-seed42");
  return read_state_blob(bytes).combat_state;
}

// -------------------------------------------------------------------------
// Detection
// -------------------------------------------------------------------------

TEST(LouseProgenitorDetection, Fixture5_IsLouseProgenitorNormal) {
  const auto combat = parse_fixture5();
  EXPECT_TRUE(is_louse_progenitor_normal(combat));
}

TEST(LouseProgenitorDetection, Fixture1_IsNotLouseProgenitorNormal) {
  const auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  const auto combat = read_state_blob(bytes).combat_state;
  EXPECT_FALSE(is_louse_progenitor_normal(combat));
}

TEST(LouseProgenitorDetection, Synthetic_WrongName_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 1;
  ParsedCreature e;
  e.name = "FossilStalker";
  e.current_hp = 100;
  c.enemies.push_back(e);
  EXPECT_FALSE(is_louse_progenitor_normal(c));
}

TEST(LouseProgenitorDetection, Synthetic_Dead_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 1;
  ParsedCreature e;
  e.name = "LouseProgenitor";
  e.current_hp = 0;
  c.enemies.push_back(e);
  EXPECT_FALSE(is_louse_progenitor_normal(c));
}

TEST(LouseProgenitorDetection, Synthetic_TwoEnemies_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 2;
  ParsedCreature e;
  e.name = "LouseProgenitor";
  e.current_hp = 135;
  c.enemies.push_back(e);
  c.enemies.push_back(e);
  EXPECT_FALSE(is_louse_progenitor_normal(c));
}

// -------------------------------------------------------------------------
// Projection: fixture #5 happy path
// -------------------------------------------------------------------------

TEST(LouseProgenitorProjection, Fixture5_ProducesSaneCompactState) {
  const auto combat = parse_fixture5();
  ASSERT_TRUE(is_louse_progenitor_normal(combat));

  const CompactState s = project_louse_progenitor_normal(combat);

  // Player at Silent starter: HP 70/70, no block, no debuffs.
  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_player_strength(), Stat{0});
  EXPECT_EQ(s.get_player_weak(), Stat{0});

  // Silent base energy 3; round 1 pre-action.
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1U);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);

  // Exactly 1 alive enemy in slot 0.
  EXPECT_TRUE(s.get_enemy(0).get_alive());
}

TEST(LouseProgenitorProjection, Fixture5_EnemyKindIsLouseProgenitor) {
  const auto combat = parse_fixture5();
  const CompactState s = project_louse_progenitor_normal(combat);
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kLouseProgenitor);
}

TEST(LouseProgenitorProjection, Fixture5_InitialMoveIsWebCannon) {
  const auto combat = parse_fixture5();
  const CompactState s = project_louse_progenitor_normal(combat);
  // Smoke boot: intent may be absent; projection defaults to kWebCannon.
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kWebCannon);
  EXPECT_FALSE(s.get_enemy(0).get_performed_first_move());
}

TEST(LouseProgenitorProjection, Fixture5_CurlUpPower_PresentWithStacks14) {
  const auto combat = parse_fixture5();
  const CompactState s = project_louse_progenitor_normal(combat);
  // CurlUp(14) must be present: either from wire or synthesized per
  // Q2-ADR-005 silent-drop pattern. Stacks must equal 14 (A0 value).
  const auto& e = s.get_enemy(0);
  const auto& powers = e.get_powers();
  const uint8_t count = e.get_power_count();
  bool found = false;
  for (uint8_t i = 0; i < count; ++i) {
    if (powers[i].kind == PowerKind::kCurlUp) {
      EXPECT_EQ(powers[i].stacks, int32_t{14});
      found = true;
      break;
    }
  }
  EXPECT_TRUE(found) << "CurlUp power not found in projected enemy";
}

TEST(LouseProgenitorProjection, Fixture5_CardPileTotals12) {
  const auto combat = parse_fixture5();
  const CompactState s = project_louse_progenitor_normal(combat);
  // Silent 12-card starter deck: hand + draw + discard = 12.
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);
  // Ring-of-the-Snake: hand size = 7 on round 1.
  EXPECT_EQ(s.get_hand().total(), 7);
}

}  // namespace
