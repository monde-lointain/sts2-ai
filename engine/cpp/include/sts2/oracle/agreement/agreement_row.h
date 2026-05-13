#pragma once

#include <cstdint>
#include <limits>
#include <string>

// Oracle-agreement row schema. Per Q2-ADR-004, every verify() outcome
// (success or not-verified) produces one row appended to a per-(day, model)
// Parquet file under `data/oracle/agreement/`. Column order + types in this
// struct match the ADR-004 table verbatim — sink.cc relies on the field
// order to build the Arrow schema column-for-column.
//
// Q2-ADR-005 stamping discipline: every row carries
// (algorithm_sha, registry_sha, simulator_build_sha) so downstream
// consumers (Q3 ingest, Q10 prioritized-sampling) can quarantine signals
// by manifest tuple without explicit Q2 coordination.
//
// Phase-1A scope: CULTISTS_NORMAL only (Q2-ADR-002). Rows for unsupported
// encounters populate `unsupported_reason` (and leave model_* columns at
// their defaults / NaNs); rows for verified states populate the full
// oracle_* and model_* payload.

namespace sts2::oracle::agreement {

struct AgreementRow {
  // M1 trailer SHA-256 (64 lowercase hex chars) of the input blob.
  std::string state_hash;

  // Serialized oracle (Q2 expectimax) outputs.
  std::string oracle_action_json;
  double oracle_value_hp = 0.0;
  double oracle_value_rounds = 0.0;

  // Serialized model proposal. model_value_* are NaN when the model
  // emits run-value only (no per-state expected_hp / expected_rounds).
  std::string model_action_json;
  double model_value_hp = std::numeric_limits<double>::quiet_NaN();
  double model_value_rounds = std::numeric_limits<double>::quiet_NaN();

  // Manifest stamping tuple (Q2-ADR-005).
  std::string model_version;         // Q5 artifact sha (also the Parquet partition tag).
  std::string algorithm_sha;         // Q2 algorithm manifest sha.
  std::string registry_sha;          // Q4 token-registry sha echoed from input blob.
  std::string simulator_build_sha;   // Q1 build sha echoed from input blob.

  // True iff the oracle fully expanded the state (no budget cap hit).
  bool expansion_complete = false;

  // Non-empty iff the verifier returned not-verified. Matches the
  // Q2-ADR-003 RPC `reason` enum string.
  std::string unsupported_reason;

  // Optional. Populated when Q2-ADR-005 unknown-power diagnostic fires.
  std::string q1_divergence_diagnostic_json;

  // Epoch millis of the verify call. Drives partition (year/month/day).
  std::int64_t timestamp_ms = 0;

  bool operator==(const AgreementRow&) const = default;
};

}  // namespace sts2::oracle::agreement
