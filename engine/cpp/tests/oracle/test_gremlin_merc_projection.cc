// Tests for gremlin_merc_projection.h/.cc (wave-26/M.γ).
// Fixture #9: 09-gremlin-merc-normal-seed42.
// Covers detection (happy + 2 rejects) + projection sanity (6 checks).

#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/gremlin_merc_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using sts2::game::Stat;
using sts2::oracle::adapter::is_gremlin_merc_normal;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::project_gremlin_merc_normal;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

ParsedCombatState parse_fixture(const std::string& subdir) {
  const auto bytes = load_fixture_blob(subdir);
  return read_state_blob(bytes).combat_state;
}

// -------------------------------------------------------------------------
// Detection
// -------------------------------------------------------------------------

// Test 1: fixture 09 (GremlinMercNormal) → is_gremlin_merc_normal true.
TEST(GremlinMercDetection, Fixture9_IsGremlinMercNormal) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  EXPECT_TRUE(is_gremlin_merc_normal(combat));
}

// Test 2: fixture 01 (cultists) → is_gremlin_merc_normal false.
TEST(GremlinMercDetection, Fixture1_CultistBlob_IsNotGremlinMercNormal) {
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  EXPECT_FALSE(is_gremlin_merc_normal(combat));
}

// Test 3: fixture 05 (LouseProgenitor) → is_gremlin_merc_normal false.
TEST(GremlinMercDetection, Fixture5_LouseBlob_IsNotGremlinMercNormal) {
  const auto combat = parse_fixture("05-louse-progenitor-normal-seed42");
  EXPECT_FALSE(is_gremlin_merc_normal(combat));
}

// -------------------------------------------------------------------------
// Projection: fixture #9 happy path
// -------------------------------------------------------------------------

// Test 4: enemy_count == 1.
TEST(GremlinMercProjection, Fixture9_EnemyCount1) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  ASSERT_TRUE(is_gremlin_merc_normal(combat));
  const CompactState s = project_gremlin_merc_normal(combat);
  EXPECT_EQ(s.get_enemy_count(), 1);
  EXPECT_TRUE(s.get_enemy(0).get_alive());
}

// Test 5: enemy[0].kind == kGremlinMerc.
TEST(GremlinMercProjection, Fixture9_EnemyKindIsGremlinMerc) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  const CompactState s = project_gremlin_merc_normal(combat);
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kGremlinMerc);
}

// Test 6: initial move == kGimmeMove, move_index == 0.
TEST(GremlinMercProjection, Fixture9_InitialMoveIsGimme) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  const CompactState s = project_gremlin_merc_normal(combat);
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kGimmeMove);
  EXPECT_EQ(s.get_enemy(0).get_move_index(), uint8_t{0});
}

// Test 7: enemy[0].powers contains {kSurprise, stacks=1}.
TEST(GremlinMercProjection, Fixture9_IncludesSurprisePower) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  const CompactState s = project_gremlin_merc_normal(combat);
  const auto& e = s.get_enemy(0);
  const auto& powers = e.get_powers();
  const uint8_t count = e.get_power_count();
  bool found_surprise = false;
  for (uint8_t i = 0; i < count; ++i) {
    if (powers[i].kind == PowerKind::kSurprise) {
      EXPECT_EQ(powers[i].stacks, 1)
          << "SurprisePower stacks should be 1 (GremlinMerc.cs:49)";
      found_surprise = true;
    }
  }
  EXPECT_TRUE(found_surprise)
      << "kSurprise must be present in enemy powers after projection";
}

// Test 8: ThieveryPower silently dropped — only kSurprise in enemy powers.
// ThieveryPower wire id is UNRECOGNIZED per Q2-ADR-005 unknown-power
// infrastructure (no explicit kThievery branch in Q2 PowerKind). Verified by
// asserting power_count == 1 (only kSurprise projected; nothing else).
TEST(GremlinMercProjection, Fixture9_DropsThieverySilently) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  const CompactState s = project_gremlin_merc_normal(combat);
  const auto& e = s.get_enemy(0);
  const uint8_t count = e.get_power_count();
  // GremlinMerc has SurprisePower(1) + ThieveryPower(20) on wire.
  // Only SurprisePower → kSurprise is recognized; ThieveryPower is
  // UNRECOGNIZED → silent-drop. Exactly 1 power must remain.
  EXPECT_EQ(count, uint8_t{1})
      << "power_count must be 1 (kSurprise only); ThieveryPower must be "
         "silently dropped (Q2-ADR-005 unknown-power infrastructure)";
  if (count == 1) {
    EXPECT_EQ(e.get_powers()[0].kind, PowerKind::kSurprise)
        << "The single projected power must be kSurprise";
  }
}

// Test 9: Silent starter player state.
// HP=70, energy=3, deck=12 (5 Strike + 5 Defend + 1 Neutralize + 1 Survivor).
TEST(GremlinMercProjection, Fixture9_SilentStarterPlayerState) {
  const auto combat = parse_fixture("09-gremlin-merc-normal-seed42");
  const CompactState s = project_gremlin_merc_normal(combat);
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
