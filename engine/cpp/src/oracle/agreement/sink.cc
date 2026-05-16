#include "sts2/oracle/agreement/sink.h"

#include <arrow/api.h>
#include <arrow/io/file.h>
#include <arrow/result.h>
#include <arrow/status.h>
#include <arrow/table.h>
#include <parquet/arrow/reader.h>
#include <parquet/arrow/writer.h>
#include <parquet/properties.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <ctime>
#include <filesystem>
#include <memory>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

namespace sts2::oracle::agreement {

namespace {

// 15-column schema per Q2-ADR-004. Order MUST match AgreementRow field
// order — the writer / reader iterate by column index.
[[nodiscard]] std::shared_ptr<arrow::Schema> agreement_schema() {
  return arrow::schema({
      arrow::field("state_hash", arrow::utf8(), /*nullable=*/false),
      arrow::field("oracle_action_json", arrow::utf8(), false),
      arrow::field("oracle_value_hp", arrow::float64(), false),
      arrow::field("oracle_value_rounds", arrow::float64(), false),
      arrow::field("model_action_json", arrow::utf8(), false),
      arrow::field("model_value_hp", arrow::float64(), false),
      arrow::field("model_value_rounds", arrow::float64(), false),
      arrow::field("model_version", arrow::utf8(), false),
      arrow::field("algorithm_sha", arrow::utf8(), false),
      arrow::field("registry_sha", arrow::utf8(), false),
      arrow::field("simulator_build_sha", arrow::utf8(), false),
      arrow::field("expansion_complete", arrow::boolean(), false),
      arrow::field("unsupported_reason", arrow::utf8(), false),
      arrow::field("q1_divergence_diagnostic_json", arrow::utf8(), false),
      arrow::field("timestamp_ms", arrow::int64(), false),
  });
}

[[noreturn]] void throw_arrow(const std::string& op,
                              const arrow::Status& status) {
  throw std::runtime_error("oracle-agreement: " + op + ": " +
                           status.ToString());
}

// Unwrap arrow::Result<T> or throw a wrapped runtime_error.
template <typename T>
[[nodiscard]] T unwrap(const std::string& op, arrow::Result<T> result) {
  if (!result.ok()) {
    throw_arrow(op, result.status());
  }
  return std::move(result).ValueOrDie();
}

void check(const std::string& op, const arrow::Status& status) {
  if (!status.ok()) {
    throw_arrow(op, status);
  }
}

struct DateTuple {
  int year = 0;
  int month = 0;
  int day = 0;

  bool operator==(const DateTuple&) const = default;
};

// UTC year/month/day from epoch millis. Uses gmtime_r — std::chrono's
// time-zone API (chrono::utc_clock + chrono::year_month_day) would also
// work but pulls extra dependencies on some libstdc++ versions; gmtime_r
// is POSIX-portable and the cast is bounded by int64.
[[nodiscard]] DateTuple utc_date_from_ms(std::int64_t timestamp_ms) {
  const std::time_t seconds = static_cast<std::time_t>(timestamp_ms / 1000);
  std::tm tm_buf{};
  if (::gmtime_r(&seconds, &tm_buf) == nullptr) {
    throw std::runtime_error(
        "oracle-agreement: gmtime_r failed for timestamp_ms=" +
        std::to_string(timestamp_ms));
  }
  return DateTuple{
      .year = tm_buf.tm_year + 1900,
      .month = tm_buf.tm_mon + 1,
      .day = tm_buf.tm_mday,
  };
}

[[nodiscard]] std::string two_digit(int value) {
  std::array<char, 8> buf{};
  std::snprintf(buf.data(), buf.size(), "%02d", value);
  return std::string(buf.data());
}

[[nodiscard]] std::string four_digit(int value) {
  std::array<char, 8> buf{};
  std::snprintf(buf.data(), buf.size(), "%04d", value);
  return std::string(buf.data());
}

[[nodiscard]] std::filesystem::path partition_path(
    const std::filesystem::path& root, const DateTuple& date,
    const std::string& model_version) {
  return root / ("year=" + four_digit(date.year)) /
         ("month=" + two_digit(date.month)) / ("day=" + two_digit(date.day)) /
         ("model=" + model_version + ".parquet");
}

[[nodiscard]] std::shared_ptr<arrow::Table> rows_to_table(
    std::span<const AgreementRow> rows) {
  arrow::StringBuilder state_hash;
  arrow::StringBuilder oracle_action_json;
  arrow::DoubleBuilder oracle_value_hp;
  arrow::DoubleBuilder oracle_value_rounds;
  arrow::StringBuilder model_action_json;
  arrow::DoubleBuilder model_value_hp;
  arrow::DoubleBuilder model_value_rounds;
  arrow::StringBuilder model_version;
  arrow::StringBuilder algorithm_sha;
  arrow::StringBuilder registry_sha;
  arrow::StringBuilder simulator_build_sha;
  arrow::BooleanBuilder expansion_complete;
  arrow::StringBuilder unsupported_reason;
  arrow::StringBuilder q1_divergence_diagnostic_json;
  arrow::Int64Builder timestamp_ms;

  for (const auto& row : rows) {
    check("append state_hash", state_hash.Append(row.state_hash));
    check("append oracle_action_json",
          oracle_action_json.Append(row.oracle_action_json));
    check("append oracle_value_hp",
          oracle_value_hp.Append(row.oracle_value_hp));
    check("append oracle_value_rounds",
          oracle_value_rounds.Append(row.oracle_value_rounds));
    check("append model_action_json",
          model_action_json.Append(row.model_action_json));
    check("append model_value_hp", model_value_hp.Append(row.model_value_hp));
    check("append model_value_rounds",
          model_value_rounds.Append(row.model_value_rounds));
    check("append model_version", model_version.Append(row.model_version));
    check("append algorithm_sha", algorithm_sha.Append(row.algorithm_sha));
    check("append registry_sha", registry_sha.Append(row.registry_sha));
    check("append simulator_build_sha",
          simulator_build_sha.Append(row.simulator_build_sha));
    check("append expansion_complete",
          expansion_complete.Append(row.expansion_complete));
    check("append unsupported_reason",
          unsupported_reason.Append(row.unsupported_reason));
    check("append q1_divergence_diagnostic_json",
          q1_divergence_diagnostic_json.Append(
              row.q1_divergence_diagnostic_json));
    check("append timestamp_ms", timestamp_ms.Append(row.timestamp_ms));
  }

  std::vector<std::shared_ptr<arrow::Array>> arrays;
  arrays.reserve(15);
  auto finish = [&](auto& builder, const std::string& name) {
    std::shared_ptr<arrow::Array> array;
    check("finish " + name, builder.Finish(&array));
    arrays.push_back(std::move(array));
  };
  finish(state_hash, "state_hash");
  finish(oracle_action_json, "oracle_action_json");
  finish(oracle_value_hp, "oracle_value_hp");
  finish(oracle_value_rounds, "oracle_value_rounds");
  finish(model_action_json, "model_action_json");
  finish(model_value_hp, "model_value_hp");
  finish(model_value_rounds, "model_value_rounds");
  finish(model_version, "model_version");
  finish(algorithm_sha, "algorithm_sha");
  finish(registry_sha, "registry_sha");
  finish(simulator_build_sha, "simulator_build_sha");
  finish(expansion_complete, "expansion_complete");
  finish(unsupported_reason, "unsupported_reason");
  finish(q1_divergence_diagnostic_json, "q1_divergence_diagnostic_json");
  finish(timestamp_ms, "timestamp_ms");

  return arrow::Table::Make(agreement_schema(), arrays,
                            static_cast<std::int64_t>(rows.size()));
}

// Validates that the Parquet file's schema matches agreement_schema() by
// field name + Arrow type id. Returns empty string on success, otherwise
// a human-readable diagnostic describing the first mismatch.
[[nodiscard]] std::string schema_mismatch_diag(const arrow::Schema& actual) {
  const auto expected = agreement_schema();
  if (actual.num_fields() != expected->num_fields()) {
    return "column count " + std::to_string(actual.num_fields()) +
           " != expected " + std::to_string(expected->num_fields());
  }
  for (int i = 0; i < expected->num_fields(); ++i) {
    const auto& want = *expected->field(i);
    const auto& got = *actual.field(i);
    if (got.name() != want.name()) {
      return "column " + std::to_string(i) + " name '" + got.name() +
             "' != expected '" + want.name() + "'";
    }
    if (!got.type()->Equals(*want.type())) {
      return "column '" + want.name() + "' type " + got.type()->ToString() +
             " != expected " + want.type()->ToString();
    }
  }
  return {};
}

}  // namespace

void write_parquet(const std::filesystem::path& root,
                   std::span<const AgreementRow> rows) {
  if (rows.empty()) {
    throw std::invalid_argument("oracle-agreement: rows is empty");
  }

  const DateTuple date0 = utc_date_from_ms(rows.front().timestamp_ms);
  const std::string& model0 = rows.front().model_version;
  // cppcheck-suppress useStlAlgorithm -- loop throws on mismatch; std::any_of
  // cannot substitute a throwing loop body.
  for (const auto& row : rows) {
    if (row.model_version != model0 ||
        utc_date_from_ms(row.timestamp_ms) != date0) {
      throw std::invalid_argument(
          "oracle-agreement: rows span multiple (year, month, day, "
          "model_version) partitions — single-partition write only");
    }
  }

  const std::filesystem::path target = partition_path(root, date0, model0);
  if (std::filesystem::exists(target)) {
    throw std::runtime_error(
        "oracle-agreement: file already exists — "
        "single-writer-per-day-per-model: " +
        target.string());
  }
  std::filesystem::create_directories(target.parent_path());

  const auto table = rows_to_table(rows);

  auto out_file = unwrap("open output file",
                         arrow::io::FileOutputStream::Open(target.string()));

  // Snappy compression, Parquet 2.6 — per Q2-ADR-004.
  auto writer_props = parquet::WriterProperties::Builder()
                          .compression(parquet::Compression::SNAPPY)
                          ->version(parquet::ParquetVersion::PARQUET_2_6)
                          ->build();
  auto arrow_props = parquet::ArrowWriterProperties::Builder().build();

  check("write parquet table",
        parquet::arrow::WriteTable(*table, arrow::default_memory_pool(),
                                   out_file, /*chunk_size=*/table->num_rows(),
                                   writer_props, arrow_props));
  check("close output stream", out_file->Close());
}

std::vector<AgreementRow> read_parquet(const std::filesystem::path& file) {
  if (!std::filesystem::exists(file)) {
    throw std::runtime_error("oracle-agreement: file not found: " +
                             file.string());
  }

  auto in_file = unwrap("open input file",
                        arrow::io::ReadableFile::Open(
                            file.string(), arrow::default_memory_pool()));

  auto reader =
      unwrap("open parquet reader",
             parquet::arrow::OpenFile(in_file, arrow::default_memory_pool()));

  // Arrow 24: out-param ReadTable overloads are deprecated; the
  // arrow::Result<std::shared_ptr<arrow::Table>>-returning variant is the
  // supported replacement.
  auto table = unwrap("read table", reader->ReadTable());

  const auto diag = schema_mismatch_diag(*table->schema());
  if (!diag.empty()) {
    throw std::runtime_error("oracle-agreement: schema mismatch reading " +
                             file.string() + ": " + diag);
  }

  // Combine chunks so each column is a single contiguous array — simplest
  // index-based access.
  auto combined = unwrap("combine chunks", table->CombineChunks());

  const auto column = [&](int i) { return combined->column(i)->chunk(0); };
  const auto str_at = [&](int col, std::int64_t i) {
    const auto& arr = static_cast<const arrow::StringArray&>(*column(col));
    const auto view = arr.GetView(i);
    return std::string(view.data(), view.size());
  };
  const auto dbl_at = [&](int col, std::int64_t i) {
    return static_cast<const arrow::DoubleArray&>(*column(col)).Value(i);
  };
  const auto bool_at = [&](int col, std::int64_t i) {
    return static_cast<const arrow::BooleanArray&>(*column(col)).Value(i);
  };
  const auto i64_at = [&](int col, std::int64_t i) {
    return static_cast<const arrow::Int64Array&>(*column(col)).Value(i);
  };

  std::vector<AgreementRow> out;
  out.reserve(static_cast<std::size_t>(combined->num_rows()));
  for (std::int64_t i = 0; i < combined->num_rows(); ++i) {
    AgreementRow row;
    row.state_hash = str_at(0, i);
    row.oracle_action_json = str_at(1, i);
    row.oracle_value_hp = dbl_at(2, i);
    row.oracle_value_rounds = dbl_at(3, i);
    row.model_action_json = str_at(4, i);
    row.model_value_hp = dbl_at(5, i);
    row.model_value_rounds = dbl_at(6, i);
    row.model_version = str_at(7, i);
    row.algorithm_sha = str_at(8, i);
    row.registry_sha = str_at(9, i);
    row.simulator_build_sha = str_at(10, i);
    row.expansion_complete = bool_at(11, i);
    row.unsupported_reason = str_at(12, i);
    row.q1_divergence_diagnostic_json = str_at(13, i);
    row.timestamp_ms = i64_at(14, i);
    out.push_back(std::move(row));
  }
  return out;
}

}  // namespace sts2::oracle::agreement
