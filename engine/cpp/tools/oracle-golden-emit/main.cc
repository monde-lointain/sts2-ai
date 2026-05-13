// Schema-freeze golden emitter.
//
// Writes a single fully-populated AgreementRow to the partition-natural path
// under `engine/cpp/tests/oracle/golden/`. The resulting parquet file is the
// frozen schema artifact for Q2-ADR-004; `test_agreement_schema_freeze.cc`
// reads it back via `sts2::oracle::agreement::read_parquet` and asserts the
// 15-column shape + content. Any unintentional schema edit (column add,
// remove, type change, ordering) makes that test fail loud.
//
// Run only when intentionally bumping the schema:
//   1. Update AgreementRow + sink.cc to the new shape.
//   2. Update this main.cc to populate the new column.
//   3. Run `build/Debug/sts2_oracle_golden_emit`.
//   4. Add a schema-history row to Q2-ADR-004 in 01-decisions-log.md.
//   5. Commit (a) the AgreementRow/sink edits, (b) this main.cc bump, (c)
//      the new golden parquet, (d) the ADR entry — atomic schema-bump PR.

#include <array>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>

#include "sts2/oracle/agreement/agreement_row.h"
#include "sts2/oracle/agreement/sink.h"

namespace {

constexpr const char* kDefaultGoldenRootRel = "engine/cpp/tests/oracle/golden";

constexpr std::int64_t kGoldenTimestampMs =
    1747008000000;  // 2025-05-12 00:00:00 UTC. Deterministic.

constexpr const char* kGoldenModelVersion = "golden-v0-model-sha";

}  // namespace

int main(int argc, char** argv) {
  std::filesystem::path golden_root = kDefaultGoldenRootRel;
  for (int i = 1; i < argc; ++i) {
    const std::string arg = argv[i];
    if (arg == "--out-root" && i + 1 < argc) {
      golden_root = argv[++i];
    } else {
      std::cerr << "oracle-golden-emit: unknown argument: " << arg << "\n";
      return 2;
    }
  }

  sts2::oracle::agreement::AgreementRow row;
  // All 15 fields populated to non-default values so a downstream schema
  // check exercises every column. Strings, doubles (including the NaN-bearing
  // model_value_* columns set to real numbers here), int64, and bool all
  // round-trip.
  row.state_hash = std::string(64, 'a');
  row.oracle_action_json =
      R"({"kind":"play_card","card_id":"Strike","target_idx":0})";
  row.oracle_value_hp = 1.0;
  row.oracle_value_rounds = 2.0;
  row.model_action_json = R"({"kind":"end_turn"})";
  row.model_value_hp = 3.0;
  row.model_value_rounds = 4.0;
  row.model_version = kGoldenModelVersion;
  row.algorithm_sha = "golden-v0-algorithm-sha";
  row.registry_sha = "golden-v0-registry-sha";
  row.simulator_build_sha = "golden-v0-build-sha";
  row.expansion_complete = true;
  row.unsupported_reason = "";
  row.q1_divergence_diagnostic_json = "";
  row.timestamp_ms = kGoldenTimestampMs;

  const std::array<sts2::oracle::agreement::AgreementRow, 1> rows = {row};

  // Idempotency: write_parquet refuses to overwrite an existing partition
  // file (single-writer-per-day-per-model). Remove the existing golden
  // before re-emitting so this tool is re-runnable.
  const std::filesystem::path expected_file =
      golden_root / "year=2025" / "month=05" / "day=12" /
      "model=golden-v0-model-sha.parquet";
  std::filesystem::remove(expected_file);

  sts2::oracle::agreement::write_parquet(golden_root, rows);

  std::cout << "wrote: " << expected_file.string() << "\n";
  return 0;
}
