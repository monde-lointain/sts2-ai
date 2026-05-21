#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <string>
#include <vector>

#include "sts2/oracle/adapter/adapter.h"
#include "tests/oracle/adapter_fixtures.h"

// Parameterized reject-path tests against fixtures #2-#6 (non-cultist
// encounters). Per Q2-ADR-002: these hit the AdapterReject branch with
// reason="encounter_not_in_cpp_engine" and stamped algorithm manifest.
// Fixture #4 (KaiserCrabBoss) additionally exercises the Q2-ADR-005
// unknown-power diagnostic — 4 source-declared spawn powers are absent
// from the snapshot.

namespace {

using sts2::oracle::adapter::AdapterReject;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;

struct RejectCase {
  const char* dir;
  const char* expected_encounter_id;
  const char* expected_canonical_hash;
  bool expects_unknown_powers;
};

const std::array<RejectCase, 6>& reject_cases() {
  // Per engine/headless/test/fixtures/state-blobs/README.md canonical hash
  // table. KaiserCrabBoss spawn-power expectations per the same README
  // header note (Q2-ADR-005 unknown-power diagnostic).
  // NOTE: fixture #5 (LouseProgenitorNormal) is now a success path — it is
  // tested in test_louse_progenitor_projection.cc and test_adapter_facade.cc.
  // Wave-27/N.alpha: fixtures #8 (NibbitsNormal) + #9 (GremlinMercNormal)
  // moved from success path to reject path per Q2-ADR-017. Both encounters
  // hit kCapExceeded at 370M; pin-DEFERRED → adapter-REMOVED. Substrate
  // (monster tables, enum extensions, OnDeath helper) retained for future
  // re-attempts via G2-G5 amendment menu.
  static const std::array<RejectCase, 6> kCases = {{
      // Hashes refreshed wave-Z.0 (R13) post wave-38/B v4 fixture regen
      // (commit f9cf308). Canonical_hash strings re-read directly from each
      // fixture's metadata.json::expected_canonical_hash_hex field.
      {.dir = "02-fossil-stalker-elite-seed42",
       .expected_encounter_id = "FossilStalkerElite",
       .expected_canonical_hash =
           "bbd58eed6c1f93a56ac24c0e015258dc0f9ce94148c084945e12f15bf5b4090c",
       .expects_unknown_powers = false},
      {.dir = "03-fossil-stalker-elite-seed1337",
       .expected_encounter_id = "FossilStalkerElite",
       .expected_canonical_hash =
           "d0a0bcaa1ae544fae5d863639a694d39d1f322fe286675173d75f49ffabf5423",
       .expects_unknown_powers = false},
      {.dir = "04-kaiser-crab-boss-seed42",
       .expected_encounter_id = "KaiserCrabBoss",
       .expected_canonical_hash =
           "20ebbe1f8e79c4c7fbc5e0b898d1881a956e68e102a53a9322a72acf6cef7e3d",
       .expects_unknown_powers = true},
      // wave-22.γ: encounter_map entries for SmallSlimes were corrected from
      // STS1 names {AcidSlimeS, SpikeSlimeS} to actual Q1 wire names
      // {LeafSlimeM/S, TwigSlimeM/S}. Fixture #6 still carries the STS1 names
      // (Q1 B.1-ε fixture port deferred), so detect_encounter_id returns
      // "<unknown>" for {AcidSlimeS, SpikeSlimeS} — no longer in the map.
      {.dir = "06-small-slimes-seed42",
       .expected_encounter_id = "<unknown>",
       .expected_canonical_hash =
           "8fb9440f38df6f900c0c631797f2e5c30a9523a9859c76568d155b398c60cfeb",
       .expects_unknown_powers = false},
      // Wave-27/N.alpha: NibbitsNormal removed from encounter_map per
      // Q2-ADR-017. Fixture #8 now routes to "<unknown>".
      {.dir = "08-nibbits-normal-seed42",
       .expected_encounter_id = "<unknown>",
       .expected_canonical_hash =
           "268ab0c6f261f854793b9aa82f561a9590df5df2137c48e3ae9c6de83aa9918f",
       .expects_unknown_powers = false},
      // Wave-27/N.alpha: GremlinMercNormal removed from encounter_map per
      // Q2-ADR-017. Fixture #9 carries Surprise + Thievery powers; with
      // SurprisePower recognition removed from project_powers.h, both fall
      // through to the unknown-power silent-drop path (Q2-ADR-005). The
      // reject path does NOT surface an unknown-power diagnostic for
      // <unknown> encounters (the diagnostic is gated on a known encounter
      // with absent source-declared powers — see test_adapter_reject.cc
      // expects_unknown_powers logic for fixture #4 KaiserCrabBoss).
      {.dir = "09-gremlin-merc-normal-seed42",
       .expected_encounter_id = "<unknown>",
       .expected_canonical_hash =
           "57404cad0d7999f52fc19bba916254989bd78ca54037cce641ae75de23a39054",
       .expects_unknown_powers = false},
  }};
  return kCases;
}

class AdapterRejectParamTest : public ::testing::TestWithParam<RejectCase> {};

TEST_P(AdapterRejectParamTest, RejectsWithExpectedDiagnostic) {
  const RejectCase& tc = GetParam();
  const auto bytes = load_fixture_blob(tc.dir);
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 1U) << "expected AdapterReject for " << tc.dir;
  const auto& reject = std::get<AdapterReject>(r);

  EXPECT_EQ(reject.unsupported.encounter_id, tc.expected_encounter_id);
  EXPECT_EQ(reject.unsupported.reason, "encounter_not_in_cpp_engine");
  EXPECT_EQ(reject.unsupported.blob_canonical_hash, tc.expected_canonical_hash);
  EXPECT_FALSE(reject.unsupported.manifest.algorithm_sha.empty());
  EXPECT_FALSE(reject.unsupported.monster_ids.empty());

  if (tc.expects_unknown_powers) {
    ASSERT_TRUE(reject.unknown_powers.has_value())
        << "fixture " << tc.dir << " expected unknown-power diagnostic";
    const auto& diag = *reject.unknown_powers;
    EXPECT_EQ(diag.encounter_id, tc.expected_encounter_id);
    EXPECT_EQ(diag.blob_canonical_hash, tc.expected_canonical_hash);
    // KaiserCrabBoss source-declared spawn-powers per fixture #4 README
    // header: BackAttackLeft, BackAttackRight, CrabRage, Surrounded. The
    // wire's PowerInstance.ModelId set is empty (Q1 silent-drop at boot),
    // so all 4 are absent.
    std::vector<std::string> got =
        diag.source_declared_power_ids_absent_from_snapshot;
    std::sort(got.begin(), got.end());
    const std::vector<std::string> want = {
        "BackAttackLeftPower",
        "BackAttackRightPower",
        "CrabRagePower",
        "SurroundedPower",
    };
    EXPECT_EQ(got, want);
    EXPECT_FALSE(diag.source_simulator_build_sha.empty());
    EXPECT_FALSE(diag.manifest.algorithm_sha.empty());
  } else {
    EXPECT_FALSE(reject.unknown_powers.has_value())
        << "fixture " << tc.dir << " unexpected unknown-power diagnostic";
  }
}

INSTANTIATE_TEST_SUITE_P(NonCultistFixtures, AdapterRejectParamTest,
                         ::testing::ValuesIn(reject_cases()),
                         [](const ::testing::TestParamInfo<RejectCase>& info) {
                           // gtest names allow only alphanumeric + underscore.
                           std::string name = info.param.dir;
                           for (auto& c : name) {
                             if (!std::isalnum(static_cast<unsigned char>(c)))
                               c = '_';
                           }
                           return name;
                         });

}  // namespace
