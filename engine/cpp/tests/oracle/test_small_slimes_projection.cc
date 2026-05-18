// Tests for small_slimes_projection.h/.cc (wave-22.γ).
// Fixture #6: 06-small-slimes-seed42 (SmallSlimes-Weak, initial state).
// Covers detection (both medium variants), synthetic rejects, projection
// sanity with fixture #6, and adapter facade round-trip.
//
// NOTE (wave-22.γ): Fixture #6 still contains STS1 wire names
// {AcidSlimeS, SpikeSlimeS} with enemy_count=2 (Q1 B.1-ε fixture port
// deferred; metadata.json marks this as "Initial-state only; stresses Q2
// MissingUpstream path"). Fixture-dependent tests are prefixed DISABLED_ until
// Q1 ports the fixture with actual LeafSlime/TwigSlime wire names.
// RE-SURFACE: Q1 wave required to update fixture #6 before enabling these.

#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/adapter.h"
#include "sts2/oracle/adapter/small_slimes_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::CardId;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::oracle::adapter::AdapterReject;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::is_small_slimes;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::ParsedCreature;
using sts2::oracle::adapter::project_small_slimes;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

// Helper: make a 3-enemy ParsedCombatState from 3 wire names.
ParsedCombatState make_3enemy_state(std::string_view n0, std::string_view n1,
                                    std::string_view n2, int hp = 20) {
  ParsedCombatState c;
  c.enemy_count = 3;
  for (const auto& name : {n0, n1, n2}) {
    ParsedCreature e;
    e.name = std::string(name);
    e.current_hp = hp;
    c.enemies.push_back(e);
  }
  return c;
}

// Helper: make a synthetic 3-enemy Leaf-medium state with realistic fields,
// suitable for projection testing when fixture #6 is not yet ported.
ParsedCombatState make_leaf_medium_state() {
  ParsedCombatState c;
  c.enemy_count = 3;
  c.turn_counter = 1;
  c.energy = 3;

  // Silent player: HP 70.
  c.player.name = "Silent";
  c.player.current_hp = 70;
  c.player.is_player = true;

  // {LeafSlimeM, LeafSlimeS, TwigSlimeS}
  const struct {
    const char* name;
    int hp;
    const char* move_id;
  } enemies[3] = {
      {"LeafSlimeM", 28, "CLUMP_SHOT"},
      {"LeafSlimeS", 10, "TACKLE_MOVE"},
      {"TwigSlimeS", 10, "TACKLE_MOVE"},
  };
  for (const auto& spec : enemies) {
    ParsedCreature e;
    e.name = spec.name;
    e.current_hp = spec.hp;
    e.intent_present = true;
    e.intent.move_id = spec.move_id;
    c.enemies.push_back(e);
  }

  // Silent 7-card hand (simplified).
  for (int i = 0; i < 5; ++i) {
    sts2::oracle::adapter::ParsedCardInstance card;
    card.model_id = "StrikeSilent";
    c.hand_pile.push_back(card);
  }
  for (int i = 0; i < 2; ++i) {
    sts2::oracle::adapter::ParsedCardInstance card;
    card.model_id = "DefendSilent";
    c.hand_pile.push_back(card);
  }
  // Draw pile: 5 remaining cards.
  for (int i = 0; i < 3; ++i) {
    sts2::oracle::adapter::ParsedCardInstance card;
    card.model_id = "DefendSilent";
    c.draw_pile.push_back(card);
  }
  {
    sts2::oracle::adapter::ParsedCardInstance card;
    card.model_id = "Neutralize";
    c.draw_pile.push_back(card);
  }
  {
    sts2::oracle::adapter::ParsedCardInstance card;
    card.model_id = "Survivor";
    c.draw_pile.push_back(card);
  }
  return c;
}

// -------------------------------------------------------------------------
// Detection: happy path (both variants)
// -------------------------------------------------------------------------

TEST(SmallSlimesProjection, Detects_LeafMediumVariant) {
  // {LeafSlimeM, LeafSlimeS, TwigSlimeS} — any order; detection sorts
  // internally.
  auto c = make_3enemy_state("LeafSlimeS", "TwigSlimeS", "LeafSlimeM");
  EXPECT_TRUE(is_small_slimes(c));
}

TEST(SmallSlimesProjection, Detects_TwigMediumVariant) {
  // {LeafSlimeS, TwigSlimeM, TwigSlimeS} — any order.
  auto c = make_3enemy_state("TwigSlimeM", "LeafSlimeS", "TwigSlimeS");
  EXPECT_TRUE(is_small_slimes(c));
}

// -------------------------------------------------------------------------
// Detection: rejects
// -------------------------------------------------------------------------

TEST(SmallSlimesProjection, Rejects_2Enemies) {
  ParsedCombatState c;
  c.enemy_count = 2;
  ParsedCreature e;
  e.name = "LeafSlimeS";
  e.current_hp = 10;
  c.enemies.push_back(e);
  e.name = "TwigSlimeS";
  c.enemies.push_back(e);
  EXPECT_FALSE(is_small_slimes(c));
}

TEST(SmallSlimesProjection, Rejects_4Enemies) {
  ParsedCombatState c;
  c.enemy_count = 4;
  for (const auto& name :
       {"LeafSlimeM", "LeafSlimeS", "TwigSlimeS", "TwigSlimeM"}) {
    ParsedCreature e;
    e.name = name;
    e.current_hp = 10;
    c.enemies.push_back(e);
  }
  EXPECT_FALSE(is_small_slimes(c));
}

TEST(SmallSlimesProjection, Rejects_AllSmalls) {
  // 3 small slimes — no medium — is not a valid SlimesWeak variant.
  auto c = make_3enemy_state("LeafSlimeS", "TwigSlimeS", "TwigSlimeS");
  EXPECT_FALSE(is_small_slimes(c));
}

// -------------------------------------------------------------------------
// Projection: synthetic happy path (replaces fixture #6 until Q1 port)
// -------------------------------------------------------------------------

TEST(SmallSlimesProjection, Synthetic_LeafMedium_ProjectsCorrectly) {
  // Uses make_leaf_medium_state() synthetic fixture. Fixture #6 is DISABLED
  // until Q1 ports the B.1-ε slime fixture with LeafSlime/TwigSlime wire names.
  const auto combat = make_leaf_medium_state();
  ASSERT_TRUE(is_small_slimes(combat));

  const CompactState s = project_small_slimes(combat);

  // 3 alive enemies.
  EXPECT_EQ(s.get_enemy_count(), 3U);
  for (uint8_t i = 0; i < 3U; ++i) {
    EXPECT_TRUE(s.get_enemy(i).get_alive()) << "enemy slot " << i;
    EXPECT_GT(s.get_enemy(i).get_hp().value(), 0) << "enemy slot " << i;
  }

  // MonsterKind: each enemy must be one of the 4 slime kinds.
  for (uint8_t i = 0; i < 3U; ++i) {
    const MonsterKind k = s.get_enemy(i).get_kind();
    EXPECT_TRUE(k == MonsterKind::kLeafSlimeS ||
                k == MonsterKind::kLeafSlimeM ||
                k == MonsterKind::kTwigSlimeS || k == MonsterKind::kTwigSlimeM)
        << "enemy slot " << i << " has unexpected MonsterKind";
  }

  // Initial state: performed_first_move = false for all.
  for (uint8_t i = 0; i < 3U; ++i) {
    EXPECT_FALSE(s.get_enemy(i).get_performed_first_move())
        << "enemy slot " << i;
  }

  // Phase.
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);

  // Player: Silent starter HP 70, energy 3, round 1.
  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1U);

  // Initial state: Slimed count must be 0 in all piles (pre-first-action;
  // Slimed is only injected to discard on GOOP/STICKY_SHOT moves).
  const auto& hand = s.get_hand();
  const auto& draw = s.get_draw();
  const auto& discard = s.get_discard();
  EXPECT_EQ(hand[CardId::kSlimed], 0U);
  EXPECT_EQ(draw[CardId::kSlimed], 0U);
  EXPECT_EQ(discard[CardId::kSlimed], 0U);

  // Total deck: 7 hand + 5 draw + 0 discard = 12.
  EXPECT_EQ(hand.total() + draw.total() + discard.total(), 12U);
  EXPECT_EQ(hand.total(), 7U);

  // Move IDs match wire intents: slot 0 = LeafSlimeM → kClumpShot,
  // slot 1 = LeafSlimeS → kTackleMove, slot 2 = TwigSlimeS → kTackleMove.
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kClumpShot);
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kTackleMove);
  EXPECT_EQ(s.get_enemy(2).get_current_move(), MoveId::kTackleMove);

  // MonsterKind matches wire names: slot 0 = LeafSlimeM, 1 = LeafSlimeS,
  // 2 = TwigSlimeS (enemies preserved in wire order by projection).
  EXPECT_EQ(s.get_enemy(0).get_kind(), MonsterKind::kLeafSlimeM);
  EXPECT_EQ(s.get_enemy(1).get_kind(), MonsterKind::kLeafSlimeS);
  EXPECT_EQ(s.get_enemy(2).get_kind(), MonsterKind::kTwigSlimeS);
}

// -------------------------------------------------------------------------
// Fixture #6: DISABLED until Q1 ports B.1-ε slime fixture
// (STS1 names {AcidSlimeS, SpikeSlimeS} still present; enemy_count=2)
// Re-enable when Q1 wave updates fixture #6 with LeafSlime/TwigSlime names.
// -------------------------------------------------------------------------

// DISABLED_SmallSlimesProjection.Fixture6_ProjectsCorrectly
// Blocked on Q1 B.1-ε fixture port. When Q1 fixes fixture #6:
//   1. Remove DISABLED_ prefix.
//   2. Verify is_small_slimes(combat) == true and enemy_count == 3.
//   3. Run and capture actual MonsterKind/MoveId from wire.

// -------------------------------------------------------------------------
// Adapter integration
// -------------------------------------------------------------------------

// DISABLED_SmallSlimesProjection.AdapterRoundtrip_NoLongerRejects
// Fixture #6 still has AcidSlimeS/SpikeSlimeS (enemy_count=2); adapter
// cannot match is_small_slimes(). Re-enable after Q1 fixture port.
// Confirm: from_blob_payload(fixture6) returns CompactState (index 0).

TEST(SmallSlimesProjection, AdapterRoundtrip_Fixture6_StillRejects) {
  // Confirms the current reality: fixture #6 STS1 names don't match the
  // new encounter_map entries; adapter correctly falls through to reject.
  // This test documents the gap until Q1 ports the fixture.
  const auto bytes = load_fixture_blob("06-small-slimes-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  // AcidSlimeS/SpikeSlimeS (2 enemies) don't match either SmallSlimes variant;
  // should still hit reject path.
  EXPECT_EQ(r.index(), 1U)
      << "fixture #6 (STS1 names) should remain in reject path until Q1 port";
  if (r.index() == 1U) {
    const auto& reject = std::get<sts2::oracle::adapter::AdapterReject>(r);
    // AcidSlimeS/SpikeSlimeS no longer match any encounter_map entry
    // (wave-22.γ replaced the STS1 entries with correct LeafSlime/TwigSlime
    // names); reject shows "<unknown>" until Q1 fixture port.
    EXPECT_EQ(reject.unsupported.reason, "encounter_not_in_cpp_engine");
    EXPECT_FALSE(reject.unsupported.blob_canonical_hash.empty());
  }
}

}  // namespace
