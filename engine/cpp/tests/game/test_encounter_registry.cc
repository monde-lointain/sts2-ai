// Tests for engine/cpp/include/sts2/game/encounter_registry.h.
// Spec: encounter_registry is the single source of truth for
// {encounter_id, sorted_monster_ids, spawn_fn, in_adapter_map}. Both the
// scenario loader (find_by_id) and the adapter (find_by_monsters) consume it.

#include <gtest/gtest.h>

#include <string>
#include <string_view>
#include <vector>

#include "sts2/game/combat.h"
#include "sts2/game/encounter_registry.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"

namespace {

namespace reg = sts2::game::encounter_registry;

TEST(EncounterRegistry, T_REG_005_AllContainsKnownEncounters) {
  const auto& all = reg::all();
  EXPECT_GE(all.size(), 6U);
  bool found_cultists = false, found_lp = false, found_kaiser = false;
  for (const auto& e : all) {
    if (e.encounter_id == "CultistsNormal") found_cultists = true;
    if (e.encounter_id == "LouseProgenitorNormal") found_lp = true;
    if (e.encounter_id == "KaiserCrabBoss") found_kaiser = true;
  }
  EXPECT_TRUE(found_cultists);
  EXPECT_TRUE(found_lp);
  EXPECT_TRUE(found_kaiser);
}

TEST(EncounterRegistry, T_REG_010_FindByIdHit) {
  const auto* spec = reg::find_by_id("CultistsNormal");
  ASSERT_NE(spec, nullptr);
  EXPECT_EQ(spec->encounter_id, "CultistsNormal");
  EXPECT_NE(spec->spawn, nullptr);
  ASSERT_EQ(spec->sorted_monster_ids.size(), 2U);
  EXPECT_EQ(spec->sorted_monster_ids[0], "CalcifiedCultist");
  EXPECT_EQ(spec->sorted_monster_ids[1], "DampCultist");
}

TEST(EncounterRegistry, T_REG_015_FindByIdMiss) {
  EXPECT_EQ(reg::find_by_id("NoSuchEncounter"), nullptr);
}

TEST(EncounterRegistry, T_REG_020_FindByMonstersHit) {
  const std::vector<std::string_view> ids{"LouseProgenitor"};
  const auto* spec = reg::find_by_monsters(ids);
  ASSERT_NE(spec, nullptr);
  EXPECT_EQ(spec->encounter_id, "LouseProgenitorNormal");
}

TEST(EncounterRegistry, T_REG_025_AdapterDetectionOnlyHasNullSpawn) {
  // KaiserCrabBoss is in the adapter's diagnostic map (so reject diagnostics
  // can name it) but has no simulator-side factory.
  const auto* spec = reg::find_by_id("KaiserCrabBoss");
  ASSERT_NE(spec, nullptr);
  EXPECT_EQ(spec->spawn, nullptr);
  EXPECT_TRUE(spec->in_adapter_map);
}

TEST(EncounterRegistry, T_REG_030_SpawnFnPopulatesCombat) {
  const auto* spec = reg::find_by_id("CultistsNormal");
  ASSERT_NE(spec, nullptr);
  ASSERT_NE(spec->spawn, nullptr);
  sts2::game::Combat c{42};
  sts2::game::Rng rng{42};
  spec->spawn(c, rng);
  EXPECT_EQ(c.enemies().size(), 2U);
  EXPECT_EQ(c.enemies()[0].kind, sts2::game::MonsterKind::kCultistCalcified);
  EXPECT_EQ(c.enemies()[1].kind, sts2::game::MonsterKind::kCultistDamp);
}

TEST(EncounterRegistry, T_REG_035_CultistsNormalNotInAdapterMap) {
  // CultistsNormal is the happy-path branch in the adapter, not a reject
  // diagnostic. find_by_monsters must NOT return it for {CalcifiedCultist,
  // DampCultist} — preserves the adapter's wave-1 behavior.
  const std::vector<std::string_view> ids{"CalcifiedCultist", "DampCultist"};
  EXPECT_EQ(reg::find_by_monsters(ids), nullptr);
}

TEST(EncounterRegistry, T_REG_040_NibbitsNormalNotInAdapterMap) {
  // NibbitsNormal removed from adapter in wave-27/N.alpha (Q2-ADR-017).
  // Registry includes it for scenario-loader debug use, but adapter lookup
  // must miss to preserve fixture-08 reject behavior.
  const std::vector<std::string_view> ids{"Nibbit", "Nibbit"};
  EXPECT_EQ(reg::find_by_monsters(ids), nullptr);
}

}  // namespace
