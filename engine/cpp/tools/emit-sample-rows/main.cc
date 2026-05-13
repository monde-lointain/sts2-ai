// sts2_emit_sample_rows — S3-T6 (Stream C) driver.
//
// Emits a single 2-row Parquet file under data/oracle/agreement/ that
// pairs (a) an adapter-diagnostic row produced by the AdapterReject path
// for fixture #2 (FossilStalkerElite) with (b) a verified row hand-stamped
// from the S2-T3 within-CULTISTS_NORMAL pin constants. Both rows share the
// same (day, model_version) partition so the file exercises the sink's
// single-write-per-partition contract on a realistic mixed-outcome batch.
//
// CLI:
//   --fixture-blob <path>   default: <root>/engine/headless/test/fixtures/
//                                    state-blobs/02-fossil-stalker-elite-seed42
//                                    /state.blob
//   --out-root     <path>   default: <root>/data/oracle/agreement
//
// Output partition (UTC 2025-05-12, model_version=phase1a-stub-model-sha):
//   <out-root>/year=2025/month=05/day=12/model=phase1a-stub-model-sha.parquet
//
// Round-trip verified: writer immediately reads back the emitted file and
// asserts 2 rows survive before returning success.

#include <array>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <span>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <variant>
#include <vector>

#include "sts2/oracle/adapter/adapter.h"
#include "sts2/oracle/adapter/diagnostic.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/agreement/agreement_row.h"
#include "sts2/oracle/agreement/sink.h"
#include "sts2/oracle/registry/sha.h"

namespace {

using sts2::oracle::adapter::AdapterReject;
using sts2::oracle::adapter::AdapterResult;
using sts2::oracle::adapter::current_manifest;
using sts2::oracle::adapter::from_blob_payload;
using sts2::oracle::agreement::AgreementRow;

// S2-T3 PINNED VALUES — CULTISTS_NORMAL @ seed kCombatTestSeed (0xC0FFEE).
// Captured 2026-05-12, Linux x86_64, GCC + libstdc++; cross-checked against
// tests/oracle/test_cultists_search_pins.cc which holds the same constants.
// Inline + hand-typed here (not via <tests/seeds/expected_values.h>) so the
// driver builds independently of the tests/seeds/ header.
constexpr double kS2T3CultistsNormalExpectedHp = 40.90829202578665;
constexpr double kS2T3CultistsNormalExpectedRounds = 6.4579809748486445;

// 2025-05-12T00:00:00Z. Fixed so reruns same day land on the same partition
// path — the sink rejects re-writes, so the tool's caller must clean the
// target file between invocations (or pass --out-root pointing elsewhere).
constexpr std::int64_t kFixedTimestampMs = 1747008000000;

constexpr const char* kModelVersionStub = "phase1a-stub-model-sha";

// Walks up from __FILE__ to the project root. main.cc lives at
//   <root>/engine/cpp/tools/emit-sample-rows/main.cc
// so parent_path() x 5 lands on <root>.
[[nodiscard]] std::filesystem::path project_root() {
  const std::filesystem::path here(__FILE__);
  return here.parent_path()  // emit-sample-rows/
      .parent_path()         // tools/
      .parent_path()         // cpp/
      .parent_path()         // engine/
      .parent_path();        // <root>
}

[[nodiscard]] std::vector<std::uint8_t> read_file_bytes(
    const std::filesystem::path& path) {
  std::ifstream f(path, std::ios::binary);
  if (!f) {
    throw std::runtime_error("emit-sample-rows: cannot open " + path.string());
  }
  f.seekg(0, std::ios::end);
  const std::streamsize sz = f.tellg();
  f.seekg(0, std::ios::beg);
  std::vector<std::uint8_t> bytes(static_cast<std::size_t>(sz));
  f.read(reinterpret_cast<char*>(bytes.data()), sz);
  if (!f) {
    throw std::runtime_error("emit-sample-rows: read failed " + path.string());
  }
  return bytes;
}

// Hand-rolled JSON formatter for the q1_divergence_diagnostic_json column.
// Q1 contract guarantees encounter_id + monster_ids are ASCII identifiers
// (no quotes, backslashes, control chars); a future contract break would
// surface as malformed output and is not handled here.
[[nodiscard]] std::string format_diagnostic_json(
    const std::string& encounter_id,
    const std::vector<std::string>& monster_ids, const std::string& reason) {
  std::ostringstream os;
  os << R"({"encounter_id":")" << encounter_id << R"(","monster_ids":[)";
  bool first = true;
  for (const auto& m : monster_ids) {
    if (!first) os << ',';
    os << '"' << m << '"';
    first = false;
  }
  os << R"(],"reason":")" << reason << R"("})";
  return os.str();
}

[[nodiscard]] AgreementRow build_diagnostic_row(
    const AdapterReject& reject, const std::string& registry_sha) {
  AgreementRow row;
  row.state_hash = reject.unsupported.blob_canonical_hash;
  // Override the oracle_value_* 0.0 defaults: the adapter rejected before any
  // expansion happened, so "no signal" is the truthful value. NaN is the
  // sink-wide sentinel for "field was not populated upstream".
  row.oracle_action_json = "";
  row.oracle_value_hp = std::numeric_limits<double>::quiet_NaN();
  row.oracle_value_rounds = std::numeric_limits<double>::quiet_NaN();
  row.model_action_json = "";
  row.model_value_hp = std::numeric_limits<double>::quiet_NaN();
  row.model_value_rounds = std::numeric_limits<double>::quiet_NaN();
  row.model_version = kModelVersionStub;
  row.algorithm_sha = reject.unsupported.manifest.algorithm_sha;
  row.registry_sha = registry_sha;
  row.simulator_build_sha = reject.unsupported.manifest.build_sha;
  row.expansion_complete = false;
  row.unsupported_reason = reject.unsupported.reason;
  row.q1_divergence_diagnostic_json = format_diagnostic_json(
      reject.unsupported.encounter_id, reject.unsupported.monster_ids,
      reject.unsupported.reason);
  row.timestamp_ms = kFixedTimestampMs;
  return row;
}

[[nodiscard]] AgreementRow build_verified_row(const std::string& algorithm_sha,
                                              const std::string& registry_sha,
                                              const std::string& build_sha) {
  AgreementRow row;
  // The S2-T3 pin doesn't flow through the adapter (it's built via
  // tests::helpers::make_starter_combat), so there is no real M1 trailer
  // hash. Use a deterministic synthetic ID so the row is locatable.
  row.state_hash = "S2_CULTISTS_NORMAL_seed_0xC0FFEE";
  row.oracle_action_json =
      R"({"kind":"play_card","card_id":"Strike","target_idx":0})";
  row.oracle_value_hp = kS2T3CultistsNormalExpectedHp;
  row.oracle_value_rounds = kS2T3CultistsNormalExpectedRounds;
  row.model_action_json = "";
  row.model_value_hp = std::numeric_limits<double>::quiet_NaN();
  row.model_value_rounds = std::numeric_limits<double>::quiet_NaN();
  row.model_version = kModelVersionStub;
  row.algorithm_sha = algorithm_sha;
  row.registry_sha = registry_sha;
  row.simulator_build_sha = build_sha;
  row.expansion_complete = true;
  row.unsupported_reason = "";
  row.q1_divergence_diagnostic_json = "";
  row.timestamp_ms = kFixedTimestampMs;
  return row;
}

// Resolves <out-root>/year=YYYY/month=MM/day=DD/model=<sha>.parquet for the
// fixed timestamp + stub model version. Mirrors the sink's partition rule.
[[nodiscard]] std::filesystem::path expected_partition_path(
    const std::filesystem::path& out_root) {
  return out_root / "year=2025" / "month=05" / "day=12" /
         (std::string("model=") + kModelVersionStub + ".parquet");
}

void print_row(std::ostream& os, const std::string& label,
               const AgreementRow& row) {
  // max_digits10 round-trip-preserves doubles in stdout so the pin values
  // (40.90829202578665 / 6.4579809748486445) print at full precision and
  // any future regression in the file is visible without re-reading the
  // Parquet bytes.
  const auto old_flags = os.flags();
  const auto old_precision = os.precision();
  os << std::setprecision(std::numeric_limits<double>::max_digits10);

  os << label << ":\n";
  os << "  state_hash: " << row.state_hash << '\n';
  os << "  oracle_action_json: " << row.oracle_action_json << '\n';
  os << "  oracle_value_hp: " << row.oracle_value_hp << '\n';
  os << "  oracle_value_rounds: " << row.oracle_value_rounds << '\n';
  os << "  model_action_json: " << row.model_action_json << '\n';
  os << "  model_value_hp: " << row.model_value_hp << '\n';
  os << "  model_value_rounds: " << row.model_value_rounds << '\n';
  os << "  model_version: " << row.model_version << '\n';
  os << "  algorithm_sha: " << row.algorithm_sha << '\n';
  os << "  registry_sha: " << row.registry_sha << '\n';
  os << "  simulator_build_sha: " << row.simulator_build_sha << '\n';
  os << "  expansion_complete: " << (row.expansion_complete ? "true" : "false")
     << '\n';
  os << "  unsupported_reason: " << row.unsupported_reason << '\n';
  os << "  q1_divergence_diagnostic_json: " << row.q1_divergence_diagnostic_json
     << '\n';
  os << "  timestamp_ms: " << row.timestamp_ms << '\n';

  os.flags(old_flags);
  os.precision(old_precision);
}

struct Args {
  std::filesystem::path fixture_blob;
  std::filesystem::path out_root;
};

[[nodiscard]] Args parse_args(int argc, char** argv) {
  const auto root = project_root();
  Args args{
      .fixture_blob = root / "engine" / "headless" / "test" / "fixtures" /
                      "state-blobs" / "02-fossil-stalker-elite-seed42" /
                      "state.blob",
      .out_root = root / "data" / "oracle" / "agreement",
  };

  for (int i = 1; i < argc; ++i) {
    const std::string_view arg{argv[i]};
    auto next_value = [&](const char* flag) -> std::string_view {
      if (i + 1 >= argc) {
        throw std::runtime_error(std::string("emit-sample-rows: ") + flag +
                                 " requires a value");
      }
      return argv[++i];
    };
    if (arg == "--fixture-blob") {
      args.fixture_blob = std::string(next_value("--fixture-blob"));
    } else if (arg == "--out-root") {
      args.out_root = std::string(next_value("--out-root"));
    } else {
      throw std::runtime_error("emit-sample-rows: unknown argument: " +
                               std::string(arg));
    }
  }
  return args;
}

int run(int argc, char** argv) {
  const Args args = parse_args(argc, argv);

  const auto bytes = read_file_bytes(args.fixture_blob);
  const AdapterResult result = from_blob_payload(bytes);
  if (result.index() != 1U) {
    std::cerr << "emit-sample-rows: expected AdapterReject for fixture "
              << args.fixture_blob.string()
              << ", got CompactState (variant index "
              << result.index() << ")\n";
    return 2;
  }
  const auto& reject = std::get<AdapterReject>(result);

  const std::string registry_sha =
      sts2::oracle::registry::current_phase1_registry_sha256();
  const auto manifest = current_manifest();

  const AgreementRow diag_row = build_diagnostic_row(reject, registry_sha);
  const AgreementRow verified_row = build_verified_row(
      manifest.algorithm_sha, registry_sha, manifest.build_sha);

  // Diagnostic first, verified second — preserves the natural "what came
  // off the wire" ordering for any human eyeballing the file.
  const std::array<AgreementRow, 2> rows{diag_row, verified_row};
  sts2::oracle::agreement::write_parquet(
      args.out_root, std::span<const AgreementRow>{rows});

  const auto emitted = expected_partition_path(args.out_root);
  const auto read = sts2::oracle::agreement::read_parquet(emitted);
  if (read.size() != 2U) {
    std::cerr << "emit-sample-rows: round-trip read returned " << read.size()
              << " rows, expected 2\n";
    return 3;
  }

  std::cout << "path=" << std::filesystem::absolute(emitted).string() << '\n';
  print_row(std::cout, "row[0] (diagnostic)", read[0]);
  print_row(std::cout, "row[1] (verified)", read[1]);
  return 0;
}

}  // namespace

int main(int argc, char** argv) {
  try {
    return run(argc, argv);
  } catch (const std::exception& e) {
    std::cerr << "emit-sample-rows: " << e.what() << '\n';
    return 1;
  }
}
