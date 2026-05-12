#pragma once

#include <cstdint>
#include <span>
#include <stdexcept>
#include <string>
#include <vector>

// proto3 hand-parser for the StateBlobEnvelope outer frame
// (contracts/schemas/game-simulator/state_blob.proto v0.1, Q1-ADR-012).
//
// Phase-1A v0.1 envelope shape (7 fields, no nested messages):
//   1: uint32 schema_major        (wire-type 0, varint)
//   2: uint32 schema_minor        (wire-type 0, varint)
//   3: string game_version        (wire-type 2, length-prefixed)
//   4: string simulator_build_sha (wire-type 2, length-prefixed)
//   5: string registry_sha        (wire-type 2, length-prefixed)
//   6: bytes  payload             (wire-type 2, length-prefixed)
//   7: bytes  payload_sha256      (wire-type 2, length-prefixed)
//
// D3 fixture .blob files are M1-binary payload only, NOT proto-wrapped;
// this parser is exercised by synthetic test data in T2 and is forward-laid
// for the Q12 verify RPC (Q2-ADR-003) where envelopes do appear.
//
// Per Q2-ADR-001: hand-rolled, no protobuf runtime dependency. Unknown
// field numbers and unknown wire types are rejected loud (never silently
// skipped).

namespace sts2::oracle::adapter {

class EnvelopeError : public std::runtime_error {
 public:
  using std::runtime_error::runtime_error;
};
class EnvelopeUnknownField : public EnvelopeError {
 public:
  using EnvelopeError::EnvelopeError;
};
class EnvelopeWireTypeError : public EnvelopeError {
 public:
  using EnvelopeError::EnvelopeError;
};
class EnvelopePayloadShaMismatch : public EnvelopeError {
 public:
  using EnvelopeError::EnvelopeError;
};
class EnvelopeSchemaMismatch : public EnvelopeError {
 public:
  using EnvelopeError::EnvelopeError;
};

// Wire schema constants pinned to v0.1.
inline constexpr std::uint32_t kEnvelopeSchemaMajor = 0U;
inline constexpr std::uint32_t kEnvelopeSchemaMinor = 1U;

struct ParsedEnvelope {
  std::uint32_t schema_major = 0;
  std::uint32_t schema_minor = 0;
  std::string game_version;
  std::string simulator_build_sha;
  std::string registry_sha;
  std::vector<std::uint8_t> payload;
  std::vector<std::uint8_t> payload_sha256;  // 32 bytes (validated)
};

// clang-tidy off
// Parses a StateBlobEnvelope. Throws on:
//   - unknown field numbers (EnvelopeUnknownField)
//   - unknown wire types (EnvelopeWireTypeError)
//   - payload_sha256 length != 32 OR != sha256(payload) (EnvelopePayloadShaMismatch)
//   - schema_major/minor != (0, 1) (EnvelopeSchemaMismatch)
//   - truncated / malformed varints / length-prefixed reads (EnvelopeError)
// clang-tidy on
[[nodiscard]] ParsedEnvelope parse_envelope(
    std::span<const std::uint8_t> bytes);

}  // namespace sts2::oracle::adapter
