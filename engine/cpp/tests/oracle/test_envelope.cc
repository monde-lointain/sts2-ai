#include <gtest/gtest.h>

#include <array>
#include <cstdint>
#include <span>
#include <string>
#include <string_view>
#include <vector>

#include "sts2/oracle/adapter/envelope.h"

// Tests exercise the hand-rolled proto3 parser with synthetic byte streams.
// We construct envelopes byte-by-byte (no protobuf runtime), so the encoder
// is a tiny in-test helper. The encoder mirrors the protobuf wire spec
// (varint + length-prefixed) for the 7-field StateBlobEnvelope shape.

namespace {

using sts2::oracle::adapter::EnvelopePayloadShaMismatch;
using sts2::oracle::adapter::EnvelopeSchemaMismatch;
using sts2::oracle::adapter::EnvelopeUnknownField;
using sts2::oracle::adapter::EnvelopeWireTypeError;
using sts2::oracle::adapter::ParsedEnvelope;
using sts2::oracle::adapter::parse_envelope;

constexpr int kWireTypeVarint = 0;
constexpr int kWireTypeLengthDelimited = 2;

void put_varint(std::vector<std::uint8_t>& out, std::uint64_t v) {
  while (v >= 0x80U) {
    out.push_back(static_cast<std::uint8_t>((v & 0x7FU) | 0x80U));
    v >>= 7;
  }
  out.push_back(static_cast<std::uint8_t>(v));
}

void put_tag(std::vector<std::uint8_t>& out, std::uint32_t field_num,
             int wire_type) {
  put_varint(out, (static_cast<std::uint64_t>(field_num) << 3) |
                      static_cast<std::uint64_t>(wire_type));
}

void put_varint_field(std::vector<std::uint8_t>& out, std::uint32_t field_num,
                      std::uint64_t v) {
  put_tag(out, field_num, kWireTypeVarint);
  put_varint(out, v);
}

void put_lp_field(std::vector<std::uint8_t>& out, std::uint32_t field_num,
                  std::span<const std::uint8_t> bytes) {
  put_tag(out, field_num, kWireTypeLengthDelimited);
  put_varint(out, bytes.size());
  out.insert(out.end(), bytes.begin(), bytes.end());
}

void put_string_field(std::vector<std::uint8_t>& out, std::uint32_t field_num,
                      std::string_view s) {
  put_lp_field(out, field_num,
               std::span<const std::uint8_t>(
                   reinterpret_cast<const std::uint8_t*>(s.data()), s.size()));
}

// Local SHA-256 to compute payload_sha256 for the synthetic encoder. We use
// the same algorithm but a separate copy — the adapter's sha256_internal.h
// is private to the adapter target so we re-implement just enough here.
// Implementation: identical to detail::sha256 — copy in for test isolation.

constexpr std::uint32_t kK[64] = {
    0x428a2f98U, 0x71374491U, 0xb5c0fbcfU, 0xe9b5dba5U, 0x3956c25bU,
    0x59f111f1U, 0x923f82a4U, 0xab1c5ed5U, 0xd807aa98U, 0x12835b01U,
    0x243185beU, 0x550c7dc3U, 0x72be5d74U, 0x80deb1feU, 0x9bdc06a7U,
    0xc19bf174U, 0xe49b69c1U, 0xefbe4786U, 0x0fc19dc6U, 0x240ca1ccU,
    0x2de92c6fU, 0x4a7484aaU, 0x5cb0a9dcU, 0x76f988daU, 0x983e5152U,
    0xa831c66dU, 0xb00327c8U, 0xbf597fc7U, 0xc6e00bf3U, 0xd5a79147U,
    0x06ca6351U, 0x14292967U, 0x27b70a85U, 0x2e1b2138U, 0x4d2c6dfcU,
    0x53380d13U, 0x650a7354U, 0x766a0abbU, 0x81c2c92eU, 0x92722c85U,
    0xa2bfe8a1U, 0xa81a664bU, 0xc24b8b70U, 0xc76c51a3U, 0xd192e819U,
    0xd6990624U, 0xf40e3585U, 0x106aa070U, 0x19a4c116U, 0x1e376c08U,
    0x2748774cU, 0x34b0bcb5U, 0x391c0cb3U, 0x4ed8aa4aU, 0x5b9cca4fU,
    0x682e6ff3U, 0x748f82eeU, 0x78a5636fU, 0x84c87814U, 0x8cc70208U,
    0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U};

constexpr std::uint32_t kH0[8] = {
    0x6a09e667U, 0xbb67ae85U, 0x3c6ef372U, 0xa54ff53aU,
    0x510e527fU, 0x9b05688cU, 0x1f83d9abU, 0x5be0cd19U};

constexpr std::uint32_t rotr(std::uint32_t x, unsigned n) {
  return (x >> n) | (x << (32U - n));
}

std::array<std::uint8_t, 32> sha256(std::span<const std::uint8_t> data) {
  std::uint32_t st[8];
  for (unsigned i = 0; i < 8; ++i) st[i] = kH0[i];
  const std::uint64_t total_bits = static_cast<std::uint64_t>(data.size()) * 8U;
  auto compress = [&](const std::uint8_t* block) {
    std::uint32_t w[64];
    for (unsigned i = 0; i < 16; ++i) {
      w[i] = (static_cast<std::uint32_t>(block[4U * i]) << 24) |
             (static_cast<std::uint32_t>(block[4U * i + 1]) << 16) |
             (static_cast<std::uint32_t>(block[4U * i + 2]) << 8) |
             (static_cast<std::uint32_t>(block[4U * i + 3]));
    }
    for (unsigned i = 16; i < 64; ++i) {
      const std::uint32_t s0 =
          rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
      const std::uint32_t s1 =
          rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
      w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    std::uint32_t a = st[0], b = st[1], c2 = st[2], d = st[3];
    std::uint32_t e = st[4], f = st[5], g = st[6], h = st[7];
    for (unsigned i = 0; i < 64; ++i) {
      const std::uint32_t s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
      const std::uint32_t ch = (e & f) ^ (~e & g);
      const std::uint32_t t1 = h + s1 + ch + kK[i] + w[i];
      const std::uint32_t s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
      const std::uint32_t mj = (a & b) ^ (a & c2) ^ (b & c2);
      const std::uint32_t t2 = s0 + mj;
      h = g; g = f; f = e; e = d + t1;
      d = c2; c2 = b; b = a; a = t1 + t2;
    }
    st[0] += a; st[1] += b; st[2] += c2; st[3] += d;
    st[4] += e; st[5] += f; st[6] += g; st[7] += h;
  };
  std::size_t pos = 0;
  while (data.size() - pos >= 64) {
    compress(data.data() + pos);
    pos += 64;
  }
  std::uint8_t tail[128] = {};
  const std::size_t rem = data.size() - pos;
  for (std::size_t i = 0; i < rem; ++i) tail[i] = data[pos + i];
  tail[rem] = 0x80U;
  const std::size_t tail_blocks = (rem + 9U > 64U) ? 2U : 1U;
  const std::size_t total_tail_bytes = tail_blocks * 64U;
  for (unsigned i = 0; i < 8; ++i) {
    tail[total_tail_bytes - 1U - i] =
        static_cast<std::uint8_t>((total_bits >> (8U * i)) & 0xFFU);
  }
  for (std::size_t b = 0; b < tail_blocks; ++b) compress(tail + 64U * b);
  std::array<std::uint8_t, 32> out{};
  for (unsigned i = 0; i < 8; ++i) {
    out[4U * i + 0] = static_cast<std::uint8_t>((st[i] >> 24) & 0xFFU);
    out[4U * i + 1] = static_cast<std::uint8_t>((st[i] >> 16) & 0xFFU);
    out[4U * i + 2] = static_cast<std::uint8_t>((st[i] >> 8) & 0xFFU);
    out[4U * i + 3] = static_cast<std::uint8_t>(st[i] & 0xFFU);
  }
  return out;
}

struct EnvelopeFields {
  std::uint32_t schema_major = 0;
  std::uint32_t schema_minor = 1;
  std::string game_version = "Q1-test-2026-05-12";
  std::string simulator_build_sha = "abc123";
  std::string registry_sha = "0000000000000000000000000000000000000000000000000000000000000000";
  std::vector<std::uint8_t> payload = {0x01, 0x02, 0x03, 0x04, 0x05};
};

std::vector<std::uint8_t> encode_envelope(const EnvelopeFields& f) {
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(out, 6,
               std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  const auto sha = sha256(
      std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  put_lp_field(out, 7, std::span<const std::uint8_t>(sha.data(), sha.size()));
  return out;
}

TEST(EnvelopeParser, SyntheticRoundtrip) {
  EnvelopeFields f;
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
  EnvelopeFields f;
  auto bytes = encode_envelope(f);
  // Tamper: flip a payload byte by surgically editing the encoded buffer.
  // Easier: re-encode with a wrong sha by hand.
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(out, 6,
               std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  std::array<std::uint8_t, 32> wrong{};  // all-zero hash
  put_lp_field(out, 7, std::span<const std::uint8_t>(wrong.data(), wrong.size()));
  EXPECT_THROW(parse_envelope(out), EnvelopePayloadShaMismatch);
}

TEST(EnvelopeParser, PayloadShaWrongLength_Rejected) {
  EnvelopeFields f;
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(out, 6,
               std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  std::array<std::uint8_t, 16> short_hash{};  // wrong length
  put_lp_field(out, 7,
               std::span<const std::uint8_t>(short_hash.data(),
                                             short_hash.size()));
  EXPECT_THROW(parse_envelope(out), EnvelopePayloadShaMismatch);
}

TEST(EnvelopeParser, SchemaMismatch_MajorBump_Rejected) {
  EnvelopeFields f;
  f.schema_major = 1;
  f.schema_minor = 0;
  const auto bytes = encode_envelope(f);
  EXPECT_THROW(parse_envelope(bytes), EnvelopeSchemaMismatch);
}

TEST(EnvelopeParser, SchemaMismatch_MinorBump_Rejected) {
  EnvelopeFields f;
  f.schema_major = 0;
  f.schema_minor = 2;
  const auto bytes = encode_envelope(f);
  EXPECT_THROW(parse_envelope(bytes), EnvelopeSchemaMismatch);
}

TEST(EnvelopeParser, UnknownFieldNumber_Rejected) {
  EnvelopeFields f;
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
