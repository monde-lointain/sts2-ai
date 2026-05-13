#include <gtest/gtest.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

#include "sts2/oracle/adapter/envelope.h"
#include "tests/oracle/envelope_test_helpers.h"

// Tests exercise the hand-rolled proto3 parser with synthetic byte streams.
// We construct envelopes byte-by-byte (no protobuf runtime) using the shared
// helpers in envelope_test_helpers.h. The encoder mirrors the protobuf wire
// spec (varint + length-prefixed) for the 7-field StateBlobEnvelope shape.

namespace {

using sts2::oracle::adapter::EnvelopePayloadShaMismatch;
using sts2::oracle::adapter::EnvelopeSchemaMismatch;
using sts2::oracle::adapter::EnvelopeUnknownField;
using sts2::oracle::adapter::EnvelopeWireTypeError;
using sts2::oracle::adapter::parse_envelope;
using sts2::oracle::adapter::ParsedEnvelope;
using sts2::oracle::adapter::tests::envelope::encode_envelope;
using sts2::oracle::adapter::tests::envelope::EnvelopeFields;
using sts2::oracle::adapter::tests::envelope::put_lp_field;
using sts2::oracle::adapter::tests::envelope::put_string_field;
using sts2::oracle::adapter::tests::envelope::put_varint_field;

[[nodiscard]] EnvelopeFields default_fields() {
  EnvelopeFields f;
  f.payload = {0x01, 0x02, 0x03, 0x04, 0x05};
  return f;
}

TEST(EnvelopeParser, SyntheticRoundtrip) {
  const auto f = default_fields();
  const auto bytes = encode_envelope(f);
  const ParsedEnvelope env = parse_envelope(bytes);
  EXPECT_EQ(env.schema_major, 0U);
  EXPECT_EQ(env.schema_minor, 1U);
  EXPECT_EQ(env.game_version, f.game_version);
  EXPECT_EQ(env.simulator_build_sha, f.simulator_build_sha);
  EXPECT_EQ(env.registry_sha, f.registry_sha);
  EXPECT_EQ(env.payload, f.payload);
  EXPECT_EQ(env.payload_sha256.size(), 32U);
}

TEST(EnvelopeParser, PayloadShaMismatch_Rejected) {
  const auto f = default_fields();
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(
      out, 6,
      std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  std::array<std::uint8_t, 32> wrong{};  // all-zero hash
  put_lp_field(out, 7,
               std::span<const std::uint8_t>(wrong.data(), wrong.size()));
  EXPECT_THROW(parse_envelope(out), EnvelopePayloadShaMismatch);
}

TEST(EnvelopeParser, PayloadShaWrongLength_Rejected) {
  const auto f = default_fields();
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(
      out, 6,
      std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  std::array<std::uint8_t, 16> short_hash{};  // wrong length
  put_lp_field(
      out, 7,
      std::span<const std::uint8_t>(short_hash.data(), short_hash.size()));
  EXPECT_THROW(parse_envelope(out), EnvelopePayloadShaMismatch);
}

TEST(EnvelopeParser, SchemaMismatch_MajorBump_Rejected) {
  auto f = default_fields();
  f.schema_major = 1;
  f.schema_minor = 0;
  const auto bytes = encode_envelope(f);
  EXPECT_THROW(parse_envelope(bytes), EnvelopeSchemaMismatch);
}

TEST(EnvelopeParser, SchemaMismatch_MinorBump_Rejected) {
  auto f = default_fields();
  f.schema_major = 0;
  f.schema_minor = 2;
  const auto bytes = encode_envelope(f);
  EXPECT_THROW(parse_envelope(bytes), EnvelopeSchemaMismatch);
}

TEST(EnvelopeParser, UnknownFieldNumber_Rejected) {
  const auto f = default_fields();
  auto bytes = encode_envelope(f);
  // Append a field-99 varint to the end. Field 99 is not in v0.1.
  put_varint_field(bytes, 99, 42U);
  EXPECT_THROW(parse_envelope(bytes), EnvelopeUnknownField);
}

TEST(EnvelopeParser, UnknownWireType_Rejected) {
  // Use wire-type 1 (fixed64), which the parser doesn't accept for any
  // known field. Tag = (field<<3)|wire. Field 1 with wire 1: tag = 9.
  std::vector<std::uint8_t> bytes = {0x09U, 0, 0, 0, 0, 0, 0, 0, 0};
  EXPECT_THROW(parse_envelope(bytes), EnvelopeWireTypeError);
}

}  // namespace
