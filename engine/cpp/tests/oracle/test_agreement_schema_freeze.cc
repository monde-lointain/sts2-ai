#include <gtest/gtest.h>

// Schema-freeze gate for Q2-ADR-004 (FROZEN 2026-05-12). Reads the checked-in
// golden parquet at engine/cpp/tests/oracle/golden/<partition>/<file>.parquet
// via sts2::oracle::agreement::read_parquet and asserts the 15-column shape +
// 1-row content. If any future commit adds, removes, renames, reorders, or
// retypes a field in AgreementRow / sink.cc's Arrow schema, read_parquet
// throws schema-mismatch and this test fails loud.
//
// Schema bump procedure (Q2-ADR-004 schema-history):
//   1. Edit AgreementRow + sink.cc.
//   2. Run `<build>/<config>/sts2_oracle_golden_emit` to regenerate the
//      golden parquet.
//   3. Add a schema-history row to Q2-ADR-004 in 01-decisions-log.md.
//   4. Bump the expected-value constants below to match the new golden.
//   5. Atomic PR: code + golden + ADR + this file.
//
// Gated on STS2_BUILD_ORACLE_SINK because read_parquet only exists under that
// gate. In default OFF builds the schema freeze is unenforced; lead has
// accepted this tradeoff (Phase-1A schema enforcement runs at ON-config
// `make ci-slow` wave gate cadence).

#if defined(STS2_BUILD_ORACLE_SINK)

#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

#include "sts2/oracle/agreement/agreement_row.h"
#include "sts2/oracle/agreement/sink.h"

namespace {

constexpr std::int64_t kGoldenTimestampMs =
    1747008000000;  // 2025-05-12 00:00 UTC

std::filesystem::path golden_path() {
  const std::filesystem::path here(__FILE__);
  return here.parent_path() / "golden" / "year=2025" / "month=05" / "day=12" /
         "model=golden-v0-model-sha.parquet";
}

}  // namespace

TEST(AgreementSchemaFreeze, GoldenV0RoundTrips15Columns) {
  const auto path = golden_path();
  ASSERT_TRUE(std::filesystem::exists(path))
      << "schema-freeze golden missing: " << path
      << "  (regenerate via build/Debug/sts2_oracle_golden_emit)";

  const auto rows = sts2::oracle::agreement::read_parquet(path);
  ASSERT_EQ(rows.size(), 1U);
  const auto& r = rows[0];

  EXPECT_EQ(r.state_hash, std::string(64, 'a'));
  EXPECT_EQ(r.oracle_action_json,
            R"({"kind":"play_card","card_id":"Strike","target_idx":0})");
  EXPECT_DOUBLE_EQ(r.oracle_value_hp, 1.0);
  EXPECT_DOUBLE_EQ(r.oracle_value_rounds, 2.0);
  EXPECT_EQ(r.model_action_json, R"({"kind":"end_turn"})");
  EXPECT_DOUBLE_EQ(r.model_value_hp, 3.0);
  EXPECT_DOUBLE_EQ(r.model_value_rounds, 4.0);
  EXPECT_EQ(r.model_version, "golden-v0-model-sha");
  EXPECT_EQ(r.algorithm_sha, "golden-v0-algorithm-sha");
  EXPECT_EQ(r.registry_sha, "golden-v0-registry-sha");
  EXPECT_EQ(r.simulator_build_sha, "golden-v0-build-sha");
  EXPECT_TRUE(r.expansion_complete);
  EXPECT_TRUE(r.unsupported_reason.empty());
  EXPECT_TRUE(r.q1_divergence_diagnostic_json.empty());
  EXPECT_EQ(r.timestamp_ms, kGoldenTimestampMs);
}

#endif  // STS2_BUILD_ORACLE_SINK
