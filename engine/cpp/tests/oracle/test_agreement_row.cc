#include <cmath>
#include <cstdint>
#include <limits>
#include <string>

#include <gtest/gtest.h>

#include "sts2/oracle/agreement/agreement_row.h"

// S3-T4 (Stream C): header-only unit coverage of AgreementRow defaults +
// equality. The struct ships through agreement_row.h with no link-time
// dependencies, so these tests compile + run regardless of
// STS2_BUILD_ORACLE_SINK (the Arrow/Parquet sink lives in a sibling TU).
//
// Default-construction discipline (Q2-ADR-004): every column except the two
// model_value_* doubles defaults to a "no signal" sentinel:
//   strings           -> empty
//   oracle_value_*    -> 0.0   (real oracle outputs always overwrite)
//   model_value_*     -> NaN   (sentinel for "model emits run-value only")
//   expansion_complete-> false
//   timestamp_ms      -> 0
// Round-trips through the Parquet sink rely on these defaults to mean
// "field was not populated upstream"; codifying them here prevents drift.

namespace {

using sts2::oracle::agreement::AgreementRow;

TEST(AgreementRow, DefaultsMatchAdrContract) {
  const AgreementRow row;

  EXPECT_TRUE(row.state_hash.empty());
  EXPECT_TRUE(row.oracle_action_json.empty());
  EXPECT_DOUBLE_EQ(row.oracle_value_hp, 0.0);
  EXPECT_DOUBLE_EQ(row.oracle_value_rounds, 0.0);

  EXPECT_TRUE(row.model_action_json.empty());
  EXPECT_TRUE(std::isnan(row.model_value_hp))
      << "model_value_hp default must be NaN (run-value-only sentinel)";
  EXPECT_TRUE(std::isnan(row.model_value_rounds))
      << "model_value_rounds default must be NaN (run-value-only sentinel)";

  EXPECT_TRUE(row.model_version.empty());
  EXPECT_TRUE(row.algorithm_sha.empty());
  EXPECT_TRUE(row.registry_sha.empty());
  EXPECT_TRUE(row.simulator_build_sha.empty());

  EXPECT_FALSE(row.expansion_complete);
  EXPECT_TRUE(row.unsupported_reason.empty());
  EXPECT_TRUE(row.q1_divergence_diagnostic_json.empty());
  EXPECT_EQ(row.timestamp_ms, std::int64_t{0});
}

TEST(AgreementRow, EqualityComparesAllColumns) {
  AgreementRow a;
  a.state_hash = "deadbeef";
  a.oracle_action_json = R"({"kind":"play_card"})";
  a.oracle_value_hp = 42.5;
  a.oracle_value_rounds = 7.25;
  a.model_action_json = R"({"kind":"end_turn"})";
  a.model_value_hp = 41.0;
  a.model_value_rounds = 7.0;
  a.model_version = "model-sha";
  a.algorithm_sha = "algo-sha";
  a.registry_sha = "reg-sha";
  a.simulator_build_sha = "build-sha";
  a.expansion_complete = true;
  a.unsupported_reason = "";
  a.q1_divergence_diagnostic_json = "";
  a.timestamp_ms = 1747008000000;

  AgreementRow b = a;
  EXPECT_EQ(a, b);

  // Mutate each field in turn; equality must break for every column. A
  // single sentinel-string change is enough to detect schema drift if the
  // defaulted operator== ever loses a field.
  b = a;
  b.state_hash = "other";
  EXPECT_NE(a, b);
  b = a;
  b.oracle_value_hp = 99.0;
  EXPECT_NE(a, b);
  b = a;
  b.model_version = "other-model";
  EXPECT_NE(a, b);
  b = a;
  b.expansion_complete = false;
  EXPECT_NE(a, b);
  b = a;
  b.timestamp_ms = 0;
  EXPECT_NE(a, b);
}

TEST(AgreementRow, NaNDefaultsBreakSelfEquality) {
  // IEEE-754: NaN != NaN. A default-constructed AgreementRow therefore
  // never equals itself via the defaulted operator==. This is correct
  // behavior — callers who care about "no signal" equivalence must
  // compare via std::isnan, never operator==.
  const AgreementRow a;
  const AgreementRow b;
  EXPECT_FALSE(a == b)
      << "two default rows compare unequal because model_value_* are NaN";
}

}  // namespace
