// Tests for engine/cpp/src/app/scenario/scenario.cc.
// Covers JSON load + Combat-build paths for the --scenario flag (see plan
// docs/superpowers/plans/... for the v1 schema).

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <atomic>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

#include "sts2/app/scenario.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"

namespace {

using ::testing::HasSubstr;

// Counter-unique temp file path. sts2_simulator_tests runs as a single
// process, so an atomic counter alone is enough — no PID (POSIX-only). Each
// test passes a distinct `tag` for readability.
std::filesystem::path write_tmp(std::string_view tag,
                                const std::string& content) {
  static std::atomic<int> counter{0};
  const int n = counter.fetch_add(1);
  auto p =
      std::filesystem::temp_directory_path() /
      ("sts2-scn-test-" + std::string(tag) + "-" + std::to_string(n) + ".json");
  std::ofstream out(p);
  out << content;
  return p;
}

// RAII guard: removes the file on scope exit. Survives EXPECT_THROW failures.
struct TmpFile {
  std::filesystem::path path;
  ~TmpFile() {
    std::error_code ec;
    std::filesystem::remove(path, ec);
  }
  std::string string() const { return path.string(); }
};

// ---------------------------------------------------------------------------
// load_scenario — parser + schema validation
// ---------------------------------------------------------------------------

TEST(ScenarioLoader, T_SCN_005_CultistsNormalMinimal) {
  TmpFile f{write_tmp("scn005", R"({"encounter":"CultistsNormal"})")};
  const auto s = sts2::app::load_scenario(f.string());
  EXPECT_EQ(s.encounter, "CultistsNormal");
  EXPECT_EQ(s.schema_version, 1);
  EXPECT_FALSE(s.seed.has_value());
  EXPECT_FALSE(s.player.hp.has_value());
  EXPECT_FALSE(s.player.max_hp.has_value());
  EXPECT_FALSE(s.player.deck.has_value());
}

TEST(ScenarioLoader, T_SCN_010_FullySpecified) {
  TmpFile f{write_tmp("scn010", R"({
    "schema_version": 1,
    "seed": 12345,
    "encounter": "LouseProgenitorNormal",
    "player": { "hp": 50, "max_hp": 80,
                "deck": ["Strike","Strike","Defend","Neutralize"] }
  })")};
  const auto s = sts2::app::load_scenario(f.string());
  EXPECT_EQ(s.schema_version, 1);
  EXPECT_EQ(s.seed, 12345U);
  EXPECT_EQ(s.encounter, "LouseProgenitorNormal");
  EXPECT_EQ(s.player.hp, 50);
  EXPECT_EQ(s.player.max_hp, 80);
  ASSERT_TRUE(s.player.deck.has_value());
  EXPECT_EQ(s.player.deck->size(), 4U);
}

TEST(ScenarioLoader, T_SCN_015_MissingEncounterErrors) {
  TmpFile f{write_tmp("scn015", R"({"seed": 1})")};
  EXPECT_THROW(
      { (void)sts2::app::load_scenario(f.string()); }, std::runtime_error);
}

TEST(ScenarioLoader, T_SCN_020_UnknownEncounterErrors) {
  TmpFile f{write_tmp("scn020", R"({"encounter":"BogusEncounterNormal"})")};
  try {
    (void)sts2::app::load_scenario(f.string());
    FAIL() << "expected throw";
  } catch (const std::runtime_error& e) {
    EXPECT_THAT(std::string(e.what()), HasSubstr("BogusEncounterNormal"));
  }
}

TEST(ScenarioLoader, T_SCN_025_UnknownCardIdErrors) {
  TmpFile f{write_tmp("scn025", R"({
    "encounter":"CultistsNormal",
    "player": {"deck":["Strike","NotARealCard"]}
  })")};
  try {
    (void)sts2::app::load_scenario(f.string());
    FAIL() << "expected throw";
  } catch (const std::runtime_error& e) {
    EXPECT_THAT(std::string(e.what()), HasSubstr("NotARealCard"));
  }
}

TEST(ScenarioLoader, T_SCN_030_FileNotFoundErrors) {
  EXPECT_THROW(
      { (void)sts2::app::load_scenario("/nonexistent/path.json"); },
      std::runtime_error);
}

TEST(ScenarioLoader, T_SCN_035_InvalidJsonErrors) {
  TmpFile f{write_tmp("scn035", R"({encounter:bad})")};
  EXPECT_THROW(
      { (void)sts2::app::load_scenario(f.string()); }, std::runtime_error);
}

TEST(ScenarioLoader, T_SCN_040_PlayerHpExceedsMaxHpErrors) {
  TmpFile f{write_tmp("scn040", R"({
    "encounter":"CultistsNormal",
    "player":{"hp":80,"max_hp":70}
  })")};
  EXPECT_THROW(
      { (void)sts2::app::load_scenario(f.string()); }, std::runtime_error);
}

TEST(ScenarioLoader, T_SCN_045_UnknownTopLevelKeyErrors) {
  TmpFile f{write_tmp("scn045", R"({
    "encounter":"CultistsNormal",
    "rogue_key":"x"
  })")};
  EXPECT_THROW(
      { (void)sts2::app::load_scenario(f.string()); }, std::runtime_error);
}

TEST(ScenarioLoader, T_SCN_050_SchemaVersionExplicitV1Accepted) {
  TmpFile f{write_tmp("scn050", R"({
    "schema_version": 1,
    "encounter": "CultistsNormal"
  })")};
  const auto s = sts2::app::load_scenario(f.string());
  EXPECT_EQ(s.schema_version, 1);
}

TEST(ScenarioLoader, T_SCN_055_SchemaVersionAbsentDefaultsToV1) {
  TmpFile f{write_tmp("scn055", R"({"encounter":"CultistsNormal"})")};
  const auto s = sts2::app::load_scenario(f.string());
  EXPECT_EQ(s.schema_version, 1);
}

TEST(ScenarioLoader, T_SCN_060_UnknownSchemaVersionErrors) {
  TmpFile f{write_tmp("scn060", R"({
    "schema_version": 2,
    "encounter": "CultistsNormal"
  })")};
  try {
    (void)sts2::app::load_scenario(f.string());
    FAIL() << "expected throw";
  } catch (const std::runtime_error& e) {
    EXPECT_THAT(std::string(e.what()), HasSubstr("schema_version"));
    EXPECT_THAT(std::string(e.what()), HasSubstr("2"));
  }
}

TEST(ScenarioLoader, T_SCN_065_AdapterOnlyEncounterRejected) {
  // KaiserCrabBoss is in encounter_registry (for adapter diagnostics) but its
  // spawn function is nullptr — cannot be built from name alone. Loader must
  // give a clear error rather than silently spawning nothing.
  TmpFile f{write_tmp("scn065", R"({"encounter":"KaiserCrabBoss"})")};
  try {
    (void)sts2::app::load_scenario(f.string());
    FAIL() << "expected throw";
  } catch (const std::runtime_error& e) {
    EXPECT_THAT(std::string(e.what()), HasSubstr("KaiserCrabBoss"));
  }
}

// ---------------------------------------------------------------------------
// build_combat — translates Scenario into a not-yet-started Combat + deck
// ---------------------------------------------------------------------------

TEST(BuildCombat, T_SCN_100_CultistsNormalSpawnsTwoCultists) {
  sts2::app::Scenario s;
  s.encounter = "CultistsNormal";
  s.seed = 42;
  auto bc = sts2::app::build_combat(s, std::nullopt);
  EXPECT_EQ(bc.combat.enemies().size(), 2U);
  EXPECT_EQ(bc.combat.enemies()[0].kind,
            sts2::game::MonsterKind::kCultistCalcified);
  EXPECT_EQ(bc.combat.enemies()[1].kind, sts2::game::MonsterKind::kCultistDamp);
}

TEST(BuildCombat, T_SCN_105_LouseProgenitorNormalSingleLP) {
  sts2::app::Scenario s;
  s.encounter = "LouseProgenitorNormal";
  s.seed = 42;
  auto bc = sts2::app::build_combat(s, std::nullopt);
  ASSERT_EQ(bc.combat.enemies().size(), 1U);
  EXPECT_EQ(bc.combat.enemies()[0].kind,
            sts2::game::MonsterKind::kLouseProgenitor);
  EXPECT_EQ(bc.combat.enemies()[0].current_move,
            sts2::game::MoveId::kWebCannon);
}

TEST(BuildCombat, T_SCN_110_NibbitsWeakSingleAlone) {
  sts2::app::Scenario s;
  s.encounter = "NibbitsWeak";
  s.seed = 42;
  auto bc = sts2::app::build_combat(s, std::nullopt);
  ASSERT_EQ(bc.combat.enemies().size(), 1U);
  EXPECT_EQ(bc.combat.enemies()[0].kind, sts2::game::MonsterKind::kNibbit);
  EXPECT_EQ(bc.combat.enemies()[0].current_move, sts2::game::MoveId::kButtMove);
}

TEST(BuildCombat, T_SCN_115_NibbitsNormalFrontAndBack) {
  sts2::app::Scenario s;
  s.encounter = "NibbitsNormal";
  s.seed = 42;
  auto bc = sts2::app::build_combat(s, std::nullopt);
  ASSERT_EQ(bc.combat.enemies().size(), 2U);
  EXPECT_EQ(bc.combat.enemies()[0].kind, sts2::game::MonsterKind::kNibbit);
  EXPECT_EQ(bc.combat.enemies()[1].kind, sts2::game::MonsterKind::kNibbit);
  EXPECT_EQ(bc.combat.enemies()[0].current_move,
            sts2::game::MoveId::kSliceMove);
  EXPECT_EQ(bc.combat.enemies()[1].current_move, sts2::game::MoveId::kHissMove);
}

TEST(BuildCombat, T_SCN_120_PlayerVitalsOverrideApplied) {
  sts2::app::Scenario s;
  s.encounter = "CultistsNormal";
  s.player.hp = 30;
  s.player.max_hp = 60;
  auto bc = sts2::app::build_combat(s, /*seed_override=*/123U);
  EXPECT_EQ(bc.combat.player().vitals.hp, sts2::game::Stat{30});
  EXPECT_EQ(bc.combat.player().vitals.max_hp, sts2::game::Stat{60});
}

TEST(BuildCombat, T_SCN_125_PlayerHpDefaultsToMaxHp) {
  sts2::app::Scenario s;
  s.encounter = "CultistsNormal";
  s.player.max_hp = 65;
  auto bc = sts2::app::build_combat(s, 1U);
  EXPECT_EQ(bc.combat.player().vitals.max_hp, sts2::game::Stat{65});
  EXPECT_EQ(bc.combat.player().vitals.hp, sts2::game::Stat{65});
}

TEST(BuildCombat, T_SCN_130_PlayerDeckOverrideRespectedAfterStart) {
  // 7-card deck so start() can fill base hand draw (5) + Ring of the Snake
  // (+2) without underrun. After start: hand=7, deck.total_size=0.
  sts2::app::Scenario s;
  s.encounter = "CultistsNormal";
  s.player.deck = std::vector<sts2::game::CardId>{
      sts2::game::CardId::kStrike,    sts2::game::CardId::kStrike,
      sts2::game::CardId::kStrike,    sts2::game::CardId::kDefend,
      sts2::game::CardId::kDefend,    sts2::game::CardId::kDefend,
      sts2::game::CardId::kNeutralize};
  auto bc = sts2::app::build_combat(s, 1U);
  bc.combat.start(std::move(bc.deck));
  EXPECT_EQ(
      bc.combat.player().hand.size() + bc.combat.player().deck.total_size(),
      7U);
}

TEST(BuildCombat, T_SCN_135_SeedOverrideAndFallbackPath) {
  // Provided seed + override both work.
  sts2::app::Scenario s;
  s.encounter = "CultistsNormal";
  s.seed = 1;
  auto c1 = sts2::app::build_combat(s, std::nullopt);
  auto c2 = sts2::app::build_combat(s, /*override=*/9999U);
  EXPECT_EQ(c1.combat.enemies().size(), 2U);
  EXPECT_EQ(c2.combat.enemies().size(), 2U);

  // Seed-fallback path: neither override nor scenario.seed — should fall back
  // to random_seed() and still build successfully (non-deterministic but
  // structurally valid).
  sts2::app::Scenario unseeded;
  unseeded.encounter = "CultistsNormal";
  auto c3 = sts2::app::build_combat(unseeded, std::nullopt);
  EXPECT_EQ(c3.combat.enemies().size(), 2U);
}

}  // namespace
