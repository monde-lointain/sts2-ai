#include "sts2/oracle/adapter/envelope.h"

#include <array>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

#include "sha256_internal.h"

// proto3 hand-parser for StateBlobEnvelope v0.1. Wire format reference:
// https://protobuf.dev/programming-guides/encoding/ — only the subset used
// by this 7-field message is implemented (varint + length-prefixed).

namespace sts2::oracle::adapter {

namespace {

constexpr int kWireTypeVarint = 0;
constexpr int kWireTypeLengthDelimited = 2;

class Cursor {
 public:
  explicit Cursor(std::span<const std::uint8_t> bytes) noexcept
      : bytes_(bytes), pos_(0) {}

  [[nodiscard]] bool exhausted() const noexcept {
    return pos_ >= bytes_.size();
  }
  [[nodiscard]] std::size_t remaining() const noexcept {
    return bytes_.size() - pos_;
  }

  std::uint64_t read_varint(const char* what) {
    std::uint64_t v = 0;
    unsigned shift = 0;
    for (unsigned i = 0; i < 10; ++i) {  // varints capped at 10 bytes (u64)
      if (pos_ >= bytes_.size()) {
        throw EnvelopeError(std::string("truncated varint: ") + what);
      }
      const std::uint8_t b = bytes_[pos_++];
      v |= static_cast<std::uint64_t>(b & 0x7FU) << shift;
      if ((b & 0x80U) == 0U) {
        return v;
      }
      shift += 7;
    }
    throw EnvelopeError(std::string("varint too long: ") + what);
  }

  std::span<const std::uint8_t> read_length_delimited(const char* what) {
    const std::uint64_t len = read_varint(what);
    if (len > remaining()) {
      throw EnvelopeError(std::string("truncated length-delimited: ") + what);
    }
    auto s = bytes_.subspan(pos_, static_cast<std::size_t>(len));
    pos_ += static_cast<std::size_t>(len);
    return s;
  }

 private:
  std::span<const std::uint8_t> bytes_;
  std::size_t pos_;
};

void expect_wire_type(int got, int want, std::uint32_t field_num) {
  if (got != want) {
    throw EnvelopeWireTypeError("field " + std::to_string(field_num) +
                                " unexpected wire type " + std::to_string(got));
  }
}

std::string read_string(Cursor& c, std::uint32_t field_num, int wire_type,
                        const char* what) {
  expect_wire_type(wire_type, kWireTypeLengthDelimited, field_num);
  auto bytes = c.read_length_delimited(what);
  return std::string(reinterpret_cast<const char*>(bytes.data()), bytes.size());
}

std::vector<std::uint8_t> read_bytes(Cursor& c, std::uint32_t field_num,
                                     int wire_type, const char* what) {
  expect_wire_type(wire_type, kWireTypeLengthDelimited, field_num);
  auto span = c.read_length_delimited(what);
  return std::vector<std::uint8_t>(span.begin(), span.end());
}

std::uint32_t read_uint32(Cursor& c, std::uint32_t field_num, int wire_type,
                          const char* what) {
  expect_wire_type(wire_type, kWireTypeVarint, field_num);
  const std::uint64_t v = c.read_varint(what);
  if (v > 0xFFFFFFFFULL) {
    throw EnvelopeError(std::string("field ") + std::to_string(field_num) +
                        " uint32 overflow");
  }
  return static_cast<std::uint32_t>(v);
}

}  // namespace

ParsedEnvelope parse_envelope(std::span<const std::uint8_t> bytes) {
  Cursor c(bytes);
  ParsedEnvelope env;

  bool seen[8] = {};  // index by field_num 1..7
  while (!c.exhausted()) {
    const std::uint64_t tag = c.read_varint("field tag");
    const std::uint32_t field_num = static_cast<std::uint32_t>(tag >> 3);
    const int wire_type = static_cast<int>(tag & 0x07ULL);

    switch (field_num) {
      case 1:
        env.schema_major = read_uint32(c, 1, wire_type, "schema_major");
        seen[1] = true;
        break;
      case 2:
        env.schema_minor = read_uint32(c, 2, wire_type, "schema_minor");
        seen[2] = true;
        break;
      case 3:
        env.game_version = read_string(c, 3, wire_type, "game_version");
        seen[3] = true;
        break;
      case 4:
        env.simulator_build_sha =
            read_string(c, 4, wire_type, "simulator_build_sha");
        seen[4] = true;
        break;
      case 5:
        env.registry_sha = read_string(c, 5, wire_type, "registry_sha");
        seen[5] = true;
        break;
      case 6:
        env.payload = read_bytes(c, 6, wire_type, "payload");
        seen[6] = true;
        break;
      case 7:
        env.payload_sha256 = read_bytes(c, 7, wire_type, "payload_sha256");
        seen[7] = true;
        break;
      default:
        // Per Q2-ADR-001 + S1 spec: reject unknown field numbers loud.
        throw EnvelopeUnknownField("unknown field number " +
                                   std::to_string(field_num));
    }
  }

  // payload_sha256 must be 32 bytes AND equal sha256(payload).
  if (env.payload_sha256.size() != 32U) {
    throw EnvelopePayloadShaMismatch("payload_sha256 length != 32 (got " +
                                     std::to_string(env.payload_sha256.size()) +
                                     ")");
  }
  const auto computed = detail::sha256(
      std::span<const std::uint8_t>(env.payload.data(), env.payload.size()));
  for (std::size_t i = 0; i < 32U; ++i) {
    if (computed[i] != env.payload_sha256[i]) {
      throw EnvelopePayloadShaMismatch(
          "payload_sha256 does not match sha256(payload)");
    }
  }

  // Schema lock per Q1-ADR-012 / Q2-ADR-001: v0.1.
  if (env.schema_major != kEnvelopeSchemaMajor ||
      env.schema_minor != kEnvelopeSchemaMinor) {
    throw EnvelopeSchemaMismatch(
        "envelope schema mismatch: got (" + std::to_string(env.schema_major) +
        "." + std::to_string(env.schema_minor) + "), want (" +
        std::to_string(kEnvelopeSchemaMajor) + "." +
        std::to_string(kEnvelopeSchemaMinor) + ")");
  }

  // Note: proto3 allows fields to be absent (defaults). We don't enforce
  // presence of all 7 fields — only the schema lock + payload_sha256 are
  // load-bearing for Phase-1A. seen[*] is recorded for forward-laid use.
  (void)seen;

  return env;
}

}  // namespace sts2::oracle::adapter
