#include <gtest/gtest.h>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/adapter.h"
#include "tests/oracle/adapter_fixtures.h"

// Unit-level coverage of the from_blob_payload facade: branch into either
// the CompactState success path (CULTISTS_NORMAL) or the AdapterReject
// path (everything else). Parameterized fixture-level coverage lives in
// test_adapter_roundtrip.cc (T5) and test_adapter_reject.cc (T6).

namespace {

using sts2::ai::CompactState;
using sts2::game::MonsterKind;
using sts2::oracle::adapter::AdapterReject;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::adapter::tests::load_fixture_blob;

TEST(AdapterFacade, Fixture1_ReturnsCompactState) {
  const auto bytes = load_fixture_blob("01-cultists-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U) << "expected CompactState variant on cultists";
  const auto& cs = std::get<CompactState>(r);
  EXPECT_TRUE(cs.get_enemy(0).get_alive());
  EXPECT_TRUE(cs.get_enemy(1).get_alive());
}

TEST(AdapterFacade, Fixture5_LouseProgenitor_ReturnsCompactState) {
  const auto bytes = load_fixture_blob("05-louse-progenitor-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U)
      << "expected CompactState variant on LouseProgenitor";
  const auto& cs = std::get<CompactState>(r);
  EXPECT_TRUE(cs.get_enemy(0).get_alive());
}

TEST(AdapterFacade, Fixture7_NibbitsWeak_ReturnsCompactState) {
  const auto bytes = load_fixture_blob("07-nibbits-weak-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U) << "expected CompactState variant on NibbitsWeak";
  const auto& cs = std::get<CompactState>(r);
  EXPECT_TRUE(cs.get_enemy(0).get_alive());
  EXPECT_EQ(cs.get_enemy(0).get_kind(), MonsterKind::kNibbit);
}

TEST(AdapterFacade, Fixture8_NibbitsNormal_ReturnsCompactState) {
  const auto bytes = load_fixture_blob("08-nibbits-normal-seed42");
  const AdapterResult r = from_blob_payload(bytes);
  ASSERT_EQ(r.index(), 0U) << "expected CompactState variant on NibbitsNormal";
  const auto& cs = std::get<CompactState>(r);
  EXPECT_TRUE(cs.get_enemy(0).get_alive());
  EXPECT_TRUE(cs.get_enemy(1).get_alive());
  EXPECT_EQ(cs.get_enemy(0).get_kind(), MonsterKind::kNibbit);
  EXPECT_EQ(cs.get_enemy(1).get_kind(), MonsterKind::kNibbit);
}

TEST(AdapterFacade, NonCultistFixtures_ReturnAdapterReject) {
  for (const auto& d : {
           "02-fossil-stalker-elite-seed42",
           "03-fossil-stalker-elite-seed1337",
           "04-kaiser-crab-boss-seed42",
           "06-small-slimes-seed42",
       }) {
    const auto bytes = load_fixture_blob(d);
    const AdapterResult r = from_blob_payload(bytes);
    ASSERT_EQ(r.index(), 1U) << "fixture: " << d;
    const auto& reject = std::get<AdapterReject>(r);
    EXPECT_EQ(reject.unsupported.reason, "encounter_not_in_cpp_engine")
        << "fixture: " << d;
    EXPECT_FALSE(reject.unsupported.blob_canonical_hash.empty())
        << "fixture: " << d;
    EXPECT_FALSE(reject.unsupported.manifest.algorithm_sha.empty())
        << "fixture: " << d;
    EXPECT_FALSE(reject.unsupported.monster_ids.empty()) << "fixture: " << d;
  }
}

}  // namespace
