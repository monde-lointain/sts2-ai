#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/cultists_projection.h"
#include "sts2/oracle/adapter/state_blob.h"
#include "tests/oracle/adapter_fixtures.h"

namespace {

using sts2::ai::CompactState;
using sts2::ai::Phase;
using sts2::game::CardId;
using sts2::game::MoveId;
using sts2::game::Stat;
using sts2::oracle::adapter::is_cultists_normal;
using sts2::oracle::adapter::ParsedCombatState;
using sts2::oracle::adapter::ParsedCreature;
using sts2::oracle::adapter::project_cultists_normal;
using sts2::oracle::adapter::read_state_blob;
using sts2::oracle::adapter::tests::load_fixture_blob;

ParsedCombatState parse_fixture(const std::string& subdir) {
  const auto bytes = load_fixture_blob(subdir);
  return read_state_blob(bytes).combat_state;
}

TEST(CultistsDetection, Fixture1_IsCultistsNormal) {
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  EXPECT_TRUE(is_cultists_normal(combat));
}

TEST(CultistsDetection, Fixtures2Through6_AreNotCultistsNormal) {
  for (const auto& d : {
           "02-fossil-stalker-elite-seed42",
           "03-fossil-stalker-elite-seed1337",
           "04-kaiser-crab-boss-seed42",
           "05-louse-progenitor-normal-seed42",
           "06-small-slimes-seed42",
       }) {
    const auto combat = parse_fixture(d);
    EXPECT_FALSE(is_cultists_normal(combat)) << "fixture: " << d;
  }
}

TEST(CultistsDetection, Synthetic_DuplicatedCalcified_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 2;
  ParsedCreature e1;
  e1.name = "CalcifiedCultist";
  e1.current_hp = 40;
  c.enemies.push_back(e1);
  c.enemies.push_back(e1);
  EXPECT_FALSE(is_cultists_normal(c));
}

TEST(CultistsDetection, Synthetic_WrongArity_Rejected) {
  ParsedCombatState c;
  c.enemy_count = 1;
  ParsedCreature e1;
  e1.name = "CalcifiedCultist";
  e1.current_hp = 40;
  c.enemies.push_back(e1);
  EXPECT_FALSE(is_cultists_normal(c));
}

TEST(CultistsProjection, Fixture1_ProducesSaneCompactState) {
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  ASSERT_TRUE(is_cultists_normal(combat));

  const CompactState s = project_cultists_normal(combat);

  // Player at Silent starter: HP 70/70, no block, no debuffs.
  EXPECT_EQ(s.get_player_hp(), Stat{70});
  EXPECT_EQ(s.get_player_block(), Stat{0});
  EXPECT_EQ(s.get_player_strength(), Stat{0});
  EXPECT_EQ(s.get_player_weak(), Stat{0});

  // Silent has base energy 3.
  EXPECT_EQ(s.get_energy(), Stat{3});
  EXPECT_EQ(s.get_round(), 1U);
  EXPECT_EQ(s.get_phase(), Phase::kPlayerActing);

  // Both cultists alive, with cultist-specific params loaded.
  ASSERT_TRUE(s.get_enemy(0).get_alive());
  ASSERT_TRUE(s.get_enemy(1).get_alive());
  EXPECT_FALSE(s.get_enemy(0).get_performed_first_move());
  EXPECT_FALSE(s.get_enemy(1).get_performed_first_move());
  // Initial intent is INCANTATION (cultists buff first, attack after).
  EXPECT_EQ(s.get_enemy(0).get_current_move(), MoveId::kIncantation);
  EXPECT_EQ(s.get_enemy(1).get_current_move(), MoveId::kIncantation);

  // Card piles: Silent starter = 4 Strike + 4 Defend + Survivor + Neutralize
  // = 10 + Ring-of-the-Snake's first-turn 7-card draw means hand=7, the
  // remaining 5 (because Q1 also seeded the deck — fixture shows hand=7,
  // draw=5, discard=0, exhaust=0 = 12 total cards. Hand/draw split:
  // hand has 4 Strike + 2 Defend + 1 Survivor + 1 Neutralize = 8? Actually
  // observed dump: hand=7, with 3 Strike + 2 Defend + 1 Neutralize +
  // 1 Survivor.  Total deck 12: hand 7 + draw 5).
  EXPECT_EQ(
      s.get_hand().total() + s.get_draw().total() + s.get_discard().total(),
      12);
  // Hand size exactly 7 (Ring of the Snake on round 1).
  EXPECT_EQ(s.get_hand().total(), 7);
  // Neutralize + Survivor are 1-of in Silent's deck → at most 1 across all
  // piles.
  EXPECT_LE(s.get_hand()[CardId::kNeutralize] +
                s.get_draw()[CardId::kNeutralize] +
                s.get_discard()[CardId::kNeutralize],
            1);
  EXPECT_LE(s.get_hand()[CardId::kSurvivor] + s.get_draw()[CardId::kSurvivor] +
                s.get_discard()[CardId::kSurvivor],
            1);
}

TEST(CultistsProjection, Fixture1_EnemyParamsMatchCppPrototype) {
  // Cultist-specific cpp-prototype params must be applied from the name,
  // not from the wire's Powers (which are empty at smoke boot). Verify:
  //   CalcifiedCultist: dark_strike_base=9, ritual_amount=2
  //   DampCultist:      dark_strike_base=1, ritual_amount=5
  const auto combat = parse_fixture("01-cultists-normal-seed42");
  const CompactState s = project_cultists_normal(combat);

  // Find the slot order from the wire and assert per-name.
  for (std::size_t i = 0; i < 2; ++i) {
    const auto& wire = combat.enemies[i];
    const auto& projected = s.get_enemy(i);
    if (wire.name == "CalcifiedCultist") {
      EXPECT_EQ(projected.get_dark_strike_base(), Stat{9})
          << "slot " << i << " (Calcified)";
      EXPECT_EQ(projected.get_ritual_amount(), Stat{2})
          << "slot " << i << " (Calcified)";
    } else if (wire.name == "DampCultist") {
      EXPECT_EQ(projected.get_dark_strike_base(), Stat{1})
          << "slot " << i << " (Damp)";
      EXPECT_EQ(projected.get_ritual_amount(), Stat{5})
          << "slot " << i << " (Damp)";
    } else {
      FAIL() << "unexpected cultist name: " << wire.name;
    }
  }
}

}  // namespace
