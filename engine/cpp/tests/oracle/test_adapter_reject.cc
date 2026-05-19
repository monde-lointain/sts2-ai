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

const std::array<RejectCase, 4>& reject_cases() {
  // Per engine/headless/test/fixtures/state-blobs/README.md canonical hash
  // table. KaiserCrabBoss spawn-power expectations per the same README
  // header note (Q2-ADR-005 unknown-power diagnostic).
  // NOTE: fixture #5 (LouseProgenitorNormal) is now a success path — it is
  // tested in test_louse_progenitor_projection.cc and test_adapter_facade.cc.
  static const std::array<RejectCase, 4> kCases = {{
      // Hashes updated wave-24/K.q1: all 8 fixtures regenerated post-Nibbit
      // catalog expansion (blob sizes grew due to catalog metadata append).
      {.dir = "02-fossil-stalker-elite-seed42",
       .expected_encounter_id = "FossilStalkerElite",
       .expected_canonical_hash =
           "15a433bec3f9fe4d8c9e6959430043ecbbd1a0d0e1cc261ae98deaa6fa9e155c",
       .expects_unknown_powers = false},
      {.dir = "03-fossil-stalker-elite-seed1337",
       .expected_encounter_id = "FossilStalkerElite",
       .expected_canonical_hash =
           "6ae16e3393efbeb79a845c95e367fbc2b76216bf42c10e47a0a0f7fd809d059e",
       .expects_unknown_powers = false},
      {.dir = "04-kaiser-crab-boss-seed42",
       .expected_encounter_id = "KaiserCrabBoss",
       .expected_canonical_hash =
           "4a5978304794c24864bab48c1676e94fce6284e02cd8af502d7539127cca44a2",
       .expects_unknown_powers = true},
      // wave-22.γ: encounter_map entries for SmallSlimes were corrected from
      // STS1 names {AcidSlimeS, SpikeSlimeS} to actual Q1 wire names
      // {LeafSlimeM/S, TwigSlimeM/S}. Fixture #6 still carries the STS1 names
      // (Q1 B.1-ε fixture port deferred), so detect_encounter_id returns
      // "<unknown>" for {AcidSlimeS, SpikeSlimeS} — no longer in the map.
      {.dir = "06-small-slimes-seed42",
       .expected_encounter_id = "<unknown>",
       .expected_canonical_hash =
           "cba34eaee17e246bcbf5b4942459bf6d39a853d343c197d9baab4b0661a668e7",
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
