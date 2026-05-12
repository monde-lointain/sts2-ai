#pragma once

#include <array>
#include <cstdint>
#include <cstring>
#include <span>

// Clean-room SHA-256 (FIPS 180-4). Adapter-internal helper; not exposed in
// any public header per Q2-ADR-001 (no new external deps, hand-rolled crypto
// kept local). Single-call API only:
//
//   std::array<uint8_t, 32> h = sha256(bytes);
//
// Performance: irrelevant for adapter use (~5KiB blob -> microseconds).

namespace sts2::oracle::adapter::detail {

inline constexpr std::uint32_t kSha256K[64] = {
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
    0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U,
};

inline constexpr std::uint32_t kSha256H0[8] = {
    0x6a09e667U, 0xbb67ae85U, 0x3c6ef372U, 0xa54ff53aU,
    0x510e527fU, 0x9b05688cU, 0x1f83d9abU, 0x5be0cd19U,
};

inline constexpr std::uint32_t rotr32(std::uint32_t x, unsigned n) noexcept {
  return (x >> n) | (x << (32U - n));
}

inline void sha256_compress(std::uint32_t state[8],
                            const std::uint8_t block[64]) noexcept {
  std::uint32_t w[64];
  for (unsigned i = 0; i < 16; ++i) {
    w[i] = (static_cast<std::uint32_t>(block[4U * i]) << 24) |
           (static_cast<std::uint32_t>(block[4U * i + 1]) << 16) |
           (static_cast<std::uint32_t>(block[4U * i + 2]) << 8) |
           (static_cast<std::uint32_t>(block[4U * i + 3]));
  }
  for (unsigned i = 16; i < 64; ++i) {
    const std::uint32_t s0 =
        rotr32(w[i - 15], 7) ^ rotr32(w[i - 15], 18) ^ (w[i - 15] >> 3);
    const std::uint32_t s1 =
        rotr32(w[i - 2], 17) ^ rotr32(w[i - 2], 19) ^ (w[i - 2] >> 10);
    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
  }

  std::uint32_t a = state[0], b = state[1], c = state[2], d = state[3];
  std::uint32_t e = state[4], f = state[5], g = state[6], h = state[7];

  for (unsigned i = 0; i < 64; ++i) {
    const std::uint32_t s1 = rotr32(e, 6) ^ rotr32(e, 11) ^ rotr32(e, 25);
    const std::uint32_t ch = (e & f) ^ (~e & g);
    const std::uint32_t temp1 = h + s1 + ch + kSha256K[i] + w[i];
    const std::uint32_t s0 = rotr32(a, 2) ^ rotr32(a, 13) ^ rotr32(a, 22);
    const std::uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
    const std::uint32_t temp2 = s0 + maj;
    h = g;
    g = f;
    f = e;
    e = d + temp1;
    d = c;
    c = b;
    b = a;
    a = temp1 + temp2;
  }

  state[0] += a;
  state[1] += b;
  state[2] += c;
  state[3] += d;
  state[4] += e;
  state[5] += f;
  state[6] += g;
  state[7] += h;
}

[[nodiscard]] inline std::array<std::uint8_t, 32> sha256(
    std::span<const std::uint8_t> data) noexcept {
  std::uint32_t state[8];
  for (unsigned i = 0; i < 8; ++i) {
    state[i] = kSha256H0[i];
  }

  const std::uint64_t total_bits = static_cast<std::uint64_t>(data.size()) * 8U;

  // Process full 64-byte blocks.
  std::size_t pos = 0;
  while (data.size() - pos >= 64) {
    sha256_compress(state, data.data() + pos);
    pos += 64;
  }

  // Final block(s): the remainder + 0x80 + zeros + 8-byte big-endian length.
  std::uint8_t tail[128] = {};
  const std::size_t rem = data.size() - pos;
  if (rem > 0) {
    std::memcpy(tail, data.data() + pos, rem);
  }
  tail[rem] = 0x80U;
  // Length sits in the last 8 bytes of either a 64-byte or 128-byte block.
  const std::size_t tail_blocks = (rem + 9U > 64U) ? 2U : 1U;
  const std::size_t total_tail_bytes = tail_blocks * 64U;
  for (unsigned i = 0; i < 8; ++i) {
    tail[total_tail_bytes - 1U - i] =
        static_cast<std::uint8_t>((total_bits >> (8U * i)) & 0xFFU);
  }
  for (std::size_t b = 0; b < tail_blocks; ++b) {
    sha256_compress(state, tail + 64U * b);
  }

  std::array<std::uint8_t, 32> out{};
  for (unsigned i = 0; i < 8; ++i) {
    out[4U * i + 0] = static_cast<std::uint8_t>((state[i] >> 24) & 0xFFU);
    out[4U * i + 1] = static_cast<std::uint8_t>((state[i] >> 16) & 0xFFU);
    out[4U * i + 2] = static_cast<std::uint8_t>((state[i] >> 8) & 0xFFU);
    out[4U * i + 3] = static_cast<std::uint8_t>(state[i] & 0xFFU);
  }
  return out;
}

[[nodiscard]] inline std::string to_hex_lower(
    std::span<const std::uint8_t> bytes) {
  static constexpr char kHex[] = "0123456789abcdef";
  std::string out;
  out.resize(bytes.size() * 2);
  for (std::size_t i = 0; i < bytes.size(); ++i) {
    out[2U * i] = kHex[bytes[i] >> 4];
    out[2U * i + 1] = kHex[bytes[i] & 0x0FU];
  }
  return out;
}

}  // namespace sts2::oracle::adapter::detail
