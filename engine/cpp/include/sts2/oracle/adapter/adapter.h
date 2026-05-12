#pragma once

#include <cstdint>
#include <optional>
#include <span>
#include <variant>

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/diagnostic.h"

// Q2 engine->CompactState adapter facade. Per Q2-ADR-001, this is the
// single entry point downstream consumers (verify-server, oracle-agreement
// sink, fixture round-trip tests) link against.
//
// Input: raw M1 binary blob bytes (the `payload` field of a
// StateBlobEnvelope, or the contents of a D3 fixture's `state.blob`).
//
// Output: either a CompactState (CULTISTS_NORMAL encounter, projection
// succeeded) or an AdapterReject (everything else — unsupported encounter,
// unknown-power divergence, format errors not raised as exceptions).
//
// Note: format errors at the M1 reader / proto envelope layer still throw
// StateCodecError / EnvelopeError. AdapterReject is reserved for
// encounter-level "I cannot verify this" outcomes that are first-class
// adapter results.

namespace sts2::oracle::adapter {

struct AdapterReject {
  UnsupportedEncounter unsupported;
  std::optional<UnknownPowerDiagnostic> unknown_powers;
};

using AdapterResult = std::variant<sts2::ai::CompactState, AdapterReject>;

// Parses an M1 binary state-blob payload, detects the encounter, and
// either returns a CompactState (CULTISTS_NORMAL) or an AdapterReject
// stamped with the algorithm manifest.
//
// Throws StateCodecError on M1 binary format errors (magic / schema /
// trailer-sha / truncation). Encounter-level decisions never throw.
[[nodiscard]] AdapterResult from_blob_payload(
    std::span<const std::uint8_t> m1_payload);

}  // namespace sts2::oracle::adapter
