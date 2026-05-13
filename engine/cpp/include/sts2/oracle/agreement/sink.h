#pragma once

#include <filesystem>
#include <span>
#include <vector>

#include "sts2/oracle/agreement/agreement_row.h"

// Q2 oracle-agreement Parquet sink. Per Q2-ADR-004.
//
// Partition layout (under `root`):
//   <root>/year=YYYY/month=MM/day=DD/model=<sha>.parquet
//
// where year/month/day are derived UTC from each row's `timestamp_ms` and
// <sha> from each row's `model_version`. A single write() call writes one
// file — all rows must share the same (year, month, day, model_version)
// tuple. This is the natural row-group locality the ADR calls out for
// Q10 prioritized-replay sampling and matches the append-only,
// one-file-per-(model_version, day) discipline.
//
// Writer is single-writer-per-(day, model): if the target file already
// exists, write() throws rather than overwriting. Re-runs that produce
// the same partition must use a fresh root or rotate by timestamp_ms.
//
// All Arrow / Parquet failures are wrapped into std::runtime_error with a
// stable "oracle-agreement: <op>: <status>" prefix so callers can match
// without depending on Arrow types.

namespace sts2::oracle::agreement {

// Writes `rows` as a single Snappy-compressed Parquet 2.6 file to the
// partition derived from row[0].timestamp_ms + row[0].model_version.
//
// Preconditions:
//   - rows is non-empty (throws std::invalid_argument otherwise).
//   - All rows share the same (year, month, day, model_version)
//     partition tuple (throws std::invalid_argument otherwise).
//
// Throws:
//   - std::invalid_argument on empty input or multi-partition rows.
//   - std::runtime_error if the target file already exists
//     (single-writer-per-day-per-model).
//   - std::runtime_error wrapping any Arrow / Parquet I/O failure.
void write_parquet(const std::filesystem::path& root,
                   std::span<const AgreementRow> rows);

// Reads `file` and returns rows in file order.
//
// Throws:
//   - std::runtime_error if `file` does not exist.
//   - std::runtime_error if the schema does not match the Q2-ADR-004
//     column set (column missing or type wrong).
//   - std::runtime_error wrapping any Arrow / Parquet I/O failure.
[[nodiscard]] std::vector<AgreementRow> read_parquet(
    const std::filesystem::path& file);

}  // namespace sts2::oracle::agreement
