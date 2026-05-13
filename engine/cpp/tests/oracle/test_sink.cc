// S3-T5 (Stream C): Parquet sink round-trip + partition + collision tests.
//
// Gated under STS2_BUILD_ORACLE_SINK because sts2::oracle_agreement links
// Apache Arrow + Parquet. Defensive #if guard mirrors the
// target_compile_definitions in tests/oracle/CMakeLists.txt — belt + suspenders.

#if defined(STS2_BUILD_ORACLE_SINK)

#include <gtest/gtest.h>

#include <unistd.h>

#include <array>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <limits>
#include <random>
#include <stdexcept>
#include <string>
#include <vector>

#include "sts2/oracle/agreement/agreement_row.h"
#include "sts2/oracle/agreement/sink.h"

namespace {

using sts2::oracle::agreement::AgreementRow;
using sts2::oracle::agreement::read_parquet;
using sts2::oracle::agreement::write_parquet;

// Deterministic timestamp_ms = 2025-05-12T00:00:00Z → year=2025/month=05/day=12.
// Aligned with the emit-sample-rows driver (S3-T6) so manual cross-inspection
// of the test corpus matches the on-disk repo data/oracle/agreement layout.
constexpr std::int64_t kFixedTimestampMs = 1747008000000;
constexpr const char* kModelSha = "abc123stub";

class SinkTest : public ::testing::Test {
 protected:
  void SetUp() override {
    // Per-test unique temp dir: PID + 64-bit random suffix. TearDown removes
    // the entire tree so concurrent test runs (gtest threads, parallel
    // ctest) never collide on filesystem state.
    std::random_device rd;
    std::uniform_int_distribution<std::uint64_t> dist;
    const std::string suffix = "sts2_sink_test_" +
                               std::to_string(::getpid()) + "_" +
                               std::to_string(dist(rd));
    root_ = std::filesystem::temp_directory_path() / suffix;
    std::filesystem::create_directories(root_);
  }

  void TearDown() override {
    std::error_code ec;
    std::filesystem::remove_all(root_, ec);
    // Best-effort cleanup; do not fail the test on missing-temp races.
  }

  // Helper: build a fully-populated row sharing the test partition.
  static AgreementRow make_row(const std::string& state_hash,
                               double oracle_hp, double oracle_rounds) {
    AgreementRow row;
    row.state_hash = state_hash;
    row.oracle_action_json = R"({"kind":"play_card","card_id":"Strike"})";
    row.oracle_value_hp = oracle_hp;
    row.oracle_value_rounds = oracle_rounds;
    row.model_action_json = "";
    row.model_value_hp = std::numeric_limits<double>::quiet_NaN();
    row.model_value_rounds = std::numeric_limits<double>::quiet_NaN();
    row.model_version = kModelSha;
    row.algorithm_sha = "algo-sha-test";
    row.registry_sha = "reg-sha-test";
    row.simulator_build_sha = "build-sha-test";
    row.expansion_complete = true;
    row.unsupported_reason = "";
    row.q1_divergence_diagnostic_json = "";
    row.timestamp_ms = kFixedTimestampMs;
    return row;
  }

  std::filesystem::path root_;
};

TEST_F(SinkTest, SingleRowRoundTrip) {
  const AgreementRow row = make_row("hash-A", 42.5, 7.25);
  const std::array rows{row};
  write_parquet(root_, rows);

  const std::filesystem::path file =
      root_ / "year=2025" / "month=05" / "day=12" /
      ("model=" + std::string(kModelSha) + ".parquet");
  const auto read = read_parquet(file);
  ASSERT_EQ(read.size(), 1U);

  const AgreementRow& got = read[0];
  EXPECT_EQ(got.state_hash, row.state_hash);
  EXPECT_EQ(got.oracle_action_json, row.oracle_action_json);
  EXPECT_DOUBLE_EQ(got.oracle_value_hp, row.oracle_value_hp);
  EXPECT_DOUBLE_EQ(got.oracle_value_rounds, row.oracle_value_rounds);
  EXPECT_EQ(got.model_action_json, row.model_action_json);
  EXPECT_TRUE(std::isnan(got.model_value_hp));
  EXPECT_TRUE(std::isnan(got.model_value_rounds));
  EXPECT_EQ(got.model_version, row.model_version);
  EXPECT_EQ(got.algorithm_sha, row.algorithm_sha);
  EXPECT_EQ(got.registry_sha, row.registry_sha);
  EXPECT_EQ(got.simulator_build_sha, row.simulator_build_sha);
  EXPECT_EQ(got.expansion_complete, row.expansion_complete);
  EXPECT_EQ(got.unsupported_reason, row.unsupported_reason);
  EXPECT_EQ(got.q1_divergence_diagnostic_json,
            row.q1_divergence_diagnostic_json);
  EXPECT_EQ(got.timestamp_ms, row.timestamp_ms);
}

TEST_F(SinkTest, MultiRowRoundTrip) {
  const AgreementRow row0 = make_row("hash-A", 42.5, 7.25);
  const AgreementRow row1 = make_row("hash-B", 38.125, 8.0);
  const std::array rows{row0, row1};
  write_parquet(root_, rows);

  const std::filesystem::path file =
      root_ / "year=2025" / "month=05" / "day=12" /
      ("model=" + std::string(kModelSha) + ".parquet");
  const auto read = read_parquet(file);
  ASSERT_EQ(read.size(), 2U);

  EXPECT_EQ(read[0].state_hash, "hash-A");
  EXPECT_EQ(read[1].state_hash, "hash-B");
  EXPECT_DOUBLE_EQ(read[0].oracle_value_hp, 42.5);
  EXPECT_DOUBLE_EQ(read[1].oracle_value_hp, 38.125);
  EXPECT_DOUBLE_EQ(read[0].oracle_value_rounds, 7.25);
  EXPECT_DOUBLE_EQ(read[1].oracle_value_rounds, 8.0);
}

TEST_F(SinkTest, PartitionPathLayout) {
  const AgreementRow row = make_row("hash-A", 1.0, 1.0);
  const std::array rows{row};
  write_parquet(root_, rows);

  // 2025-05-12T00:00:00Z + model_version=abc123stub → exact path below.
  const std::filesystem::path expected =
      root_ / "year=2025" / "month=05" / "day=12" /
      "model=abc123stub.parquet";
  EXPECT_TRUE(std::filesystem::exists(expected))
      << "expected partition path missing: " << expected;
  EXPECT_TRUE(std::filesystem::is_regular_file(expected));
  EXPECT_GT(std::filesystem::file_size(expected), 0U);
}

TEST_F(SinkTest, PartitionMismatchThrows) {
  AgreementRow row0 = make_row("hash-A", 1.0, 1.0);
  AgreementRow row1 = make_row("hash-B", 2.0, 2.0);
  row1.model_version = "other-model-sha";  // forces multi-partition input.
  const std::array rows{row0, row1};
  EXPECT_THROW(write_parquet(root_, rows), std::invalid_argument);
}

TEST_F(SinkTest, FileExistsThrows) {
  const AgreementRow row = make_row("hash-A", 1.0, 1.0);
  const std::array rows{row};
  write_parquet(root_, rows);

  // Same partition (same timestamp_ms + model_version) → second write must
  // fail by the single-writer-per-day-per-model rule.
  EXPECT_THROW(write_parquet(root_, rows), std::runtime_error);
}

}  // namespace

#endif  // STS2_BUILD_ORACLE_SINK
