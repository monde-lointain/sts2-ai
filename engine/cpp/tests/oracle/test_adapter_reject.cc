#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <string>
#include <variant>
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

const std::array<RejectCase, 5>& reject_cases() {
  // Per engine/headless/test/fixtures/state-blobs/README.md canonical hash
  // table. KaiserCrabBoss spawn-power expectations per the same README
  // header note (Q2-ADR-005 unknown-power diagnostic).
  static const std::array<RejectCase, 5> kCases = {{
      {"02-fossil-stalker-elite-seed42", "FossilStalkerElite",
       "ef1b2a5630ef9ebd067ae13b0831d5f8d5c4dcff6df61939bf20c572f96a7d0f",
       false},
      {"03-fossil-stalker-elite-seed1337", "FossilStalkerElite",
       "92ebc2e91a62a521f055d791e624e293aed2ed51cbac17a963babf11dd295a45",
       false},
      {"04-kaiser-crab-boss-seed42", "KaiserCrabBoss",
       "9edb550ef2e4a99f9544b58516f64d8d803919acfff9db29be91938a0a9cef8e",
       true},
      {"05-louse-progenitor-normal-seed42", "LouseProgenitorNormal",
       "37e7517005a0a50c05240874a6e2969c490617711bdd4d2d04c3361eaaaab392",
       false},
      {"06-small-slimes-seed42", "SmallSlimes",
       "d33371738949b606df7713b1b19c5645fb2e4d8c822c72c6224a6ce7c8cf1fbd",
       false},
  }};
  return kCases;
}

class AdapterRejectParamTest
    : public ::testing::TestWithParam<RejectCase> {};

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

INSTANTIATE_TEST_SUITE_P(
    NonCultistFixtures, AdapterRejectParamTest,
    ::testing::ValuesIn(reject_cases()),
    [](const ::testing::TestParamInfo<RejectCase>& info) {
      // gtest names allow only alphanumeric + underscore.
      std::string name = info.param.dir;
      for (auto& c : name) {
        if (!std::isalnum(static_cast<unsigned char>(c))) c = '_';
      }
      return name;
    });

}  // namespace
