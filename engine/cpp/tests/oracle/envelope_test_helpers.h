#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <span>
#include <string>
#include <string_view>
#include <vector>

// Shared synthetic StateBlobEnvelope encoder + reference SHA-256 used by
// both test_envelope.cc (the original site that proved the encoder against
// the parser) and test_verify_server.cc (which wraps fixture #1/#2 blob
// payloads in synthetic envelopes for the verify-server happy/reject paths).
//
// Per S4-A dispatch: factored out of test_envelope.cc inline helpers so the
// verify-server tests don't need to duplicate ~150 lines of varint + SHA
// scaffolding. The adapter's sha256_internal.h is PRIVATE to the adapter
// target; the SHA-256 here is a separate test-only reference impl that
// produces byte-identical output (verified by the existing test_envelope.cc
// SyntheticRoundtrip case).
//
// Wire shape (proto3 v0.1 StateBlobEnvelope, 7 fields):
//   1 varint   schema_major
//   2 varint   schema_minor
//   3 lp/utf8  game_version
//   4 lp/utf8  simulator_build_sha
//   5 lp/utf8  registry_sha
//   6 lp/bytes payload
//   7 lp/bytes payload_sha256 (32 bytes, validated by parser)

namespace sts2::oracle::adapter::tests::envelope {

constexpr int kWireTypeVarint = 0;
constexpr int kWireTypeLengthDelimited = 2;

inline void put_varint(std::vector<std::uint8_t>& out, std::uint64_t v) {
  while (v >= 0x80U) {
    out.push_back(static_cast<std::uint8_t>((v & 0x7FU) | 0x80U));
    v >>= 7;
  }
  out.push_back(static_cast<std::uint8_t>(v));
}

inline void put_tag(std::vector<std::uint8_t>& out, std::uint32_t field_num,
                    int wire_type) {
  put_varint(out, (static_cast<std::uint64_t>(field_num) << 3) |
                      static_cast<std::uint64_t>(wire_type));
}

inline void put_varint_field(std::vector<std::uint8_t>& out,
                             std::uint32_t field_num, std::uint64_t v) {
  put_tag(out, field_num, kWireTypeVarint);
  put_varint(out, v);
}

inline void put_lp_field(std::vector<std::uint8_t>& out,
                         std::uint32_t field_num,
                         std::span<const std::uint8_t> bytes) {
  put_tag(out, field_num, kWireTypeLengthDelimited);
  put_varint(out, bytes.size());
  out.insert(out.end(), bytes.begin(), bytes.end());
}

inline void put_string_field(std::vector<std::uint8_t>& out,
                             std::uint32_t field_num, std::string_view s) {
  put_lp_field(out, field_num,
               std::span<const std::uint8_t>(
                   reinterpret_cast<const std::uint8_t*>(s.data()), s.size()));
}

// --- Reference SHA-256 (FIPS 180-4) ---------------------------------------
// Independent of the adapter's private sha256_internal.h. Byte-identical
// output verified by the existing test_envelope.cc SyntheticRoundtrip case.

constexpr std::uint32_t kSha256K[64] = {
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

constexpr std::uint32_t kSha256H0[8] = {0x6a09e667U, 0xbb67ae85U, 0x3c6ef372U,
                                        0xa54ff53aU, 0x510e527fU, 0x9b05688cU,
                                        0x1f83d9abU, 0x5be0cd19U};

inline constexpr std::uint32_t rotr_sha(std::uint32_t x, unsigned n) {
  return (x >> n) | (x << (32U - n));
}

[[nodiscard]] inline std::array<std::uint8_t, 32> sha256(
    std::span<const std::uint8_t> data) {
  std::uint32_t st[8];
  for (unsigned i = 0; i < 8; ++i) {
    st[i] = kSha256H0[i];
  }
  const std::uint64_t total_bits = static_cast<std::uint64_t>(data.size()) * 8U;
  auto compress = [&](const std::uint8_t* block) {
    std::uint32_t w[64];
    for (unsigned i = 0; i < 16; ++i) {
      w[i] = (static_cast<std::uint32_t>(block[4U * i]) << 24) |
             (static_cast<std::uint32_t>(block[(4U * i) + 1]) << 16) |
             (static_cast<std::uint32_t>(block[(4U * i) + 2]) << 8) |
             (static_cast<std::uint32_t>(block[(4U * i) + 3]));
    }
    for (unsigned i = 16; i < 64; ++i) {
      const std::uint32_t s0 =
          rotr_sha(w[i - 15], 7) ^ rotr_sha(w[i - 15], 18) ^ (w[i - 15] >> 3);
      const std::uint32_t s1 =
          rotr_sha(w[i - 2], 17) ^ rotr_sha(w[i - 2], 19) ^ (w[i - 2] >> 10);
      w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    std::uint32_t a = st[0];
    std::uint32_t b = st[1];
    std::uint32_t c2 = st[2];
    std::uint32_t d = st[3];
    std::uint32_t e = st[4];
    std::uint32_t f = st[5];
    std::uint32_t g = st[6];
    std::uint32_t h = st[7];
    for (unsigned i = 0; i < 64; ++i) {
      const std::uint32_t s1 =
          rotr_sha(e, 6) ^ rotr_sha(e, 11) ^ rotr_sha(e, 25);
      const std::uint32_t ch = (e & f) ^ (~e & g);
      const std::uint32_t t1 = h + s1 + ch + kSha256K[i] + w[i];
      const std::uint32_t s0 =
          rotr_sha(a, 2) ^ rotr_sha(a, 13) ^ rotr_sha(a, 22);
      const std::uint32_t mj = (a & b) ^ (a & c2) ^ (b & c2);
      const std::uint32_t t2 = s0 + mj;
      h = g;
      g = f;
      f = e;
      e = d + t1;
      d = c2;
      c2 = b;
      b = a;
      a = t1 + t2;
    }
    st[0] += a;
    st[1] += b;
    st[2] += c2;
    st[3] += d;
    st[4] += e;
    st[5] += f;
    st[6] += g;
    st[7] += h;
  };
  std::size_t pos = 0;
  while (data.size() - pos >= 64) {
    compress(data.data() + pos);
    pos += 64;
  }
  std::uint8_t tail[128] = {};
  const std::size_t rem = data.size() - pos;
  for (std::size_t i = 0; i < rem; ++i) {
    tail[i] = data[pos + i];
  }
  tail[rem] = 0x80U;
  const std::size_t tail_blocks = (rem + 9U > 64U) ? 2U : 1U;
  const std::size_t total_tail_bytes = tail_blocks * 64U;
  for (unsigned i = 0; i < 8; ++i) {
    tail[total_tail_bytes - 1U - i] =
        static_cast<std::uint8_t>((total_bits >> (8U * i)) & 0xFFU);
  }
  for (std::size_t b = 0; b < tail_blocks; ++b) {
    compress(tail + (64U * b));
  }
  std::array<std::uint8_t, 32> out{};
  for (unsigned i = 0; i < 8; ++i) {
    out[(4U * i) + 0] = static_cast<std::uint8_t>((st[i] >> 24) & 0xFFU);
    out[(4U * i) + 1] = static_cast<std::uint8_t>((st[i] >> 16) & 0xFFU);
    out[(4U * i) + 2] = static_cast<std::uint8_t>((st[i] >> 8) & 0xFFU);
    out[(4U * i) + 3] = static_cast<std::uint8_t>(st[i] & 0xFFU);
  }
  return out;
}

// Fields used by the encode helpers below. Defaults mirror the v0.1 wire
// schema (schema_major=0, schema_minor=1). Real registry / build SHAs are
// 64-char lowercase-hex; defaults below produce a stable known-zero value.
struct EnvelopeFields {
  std::uint32_t schema_major = 0;
  std::uint32_t schema_minor = 1;
  std::string game_version = "Q1-test-2026-05-12";
  std::string simulator_build_sha = "abc123";
  std::string registry_sha =
      "0000000000000000000000000000000000000000000000000000000000000000";
  std::vector<std::uint8_t> payload;
};

[[nodiscard]] inline std::vector<std::uint8_t> encode_envelope(
    const EnvelopeFields& f) {
  std::vector<std::uint8_t> out;
  put_varint_field(out, 1, f.schema_major);
  put_varint_field(out, 2, f.schema_minor);
  put_string_field(out, 3, f.game_version);
  put_string_field(out, 4, f.simulator_build_sha);
  put_string_field(out, 5, f.registry_sha);
  put_lp_field(
      out, 6,
      std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  const auto sha =
      sha256(std::span<const std::uint8_t>(f.payload.data(), f.payload.size()));
  put_lp_field(out, 7, std::span<const std::uint8_t>(sha.data(), sha.size()));
  return out;
}

}  // namespace sts2::oracle::adapter::tests::envelope
