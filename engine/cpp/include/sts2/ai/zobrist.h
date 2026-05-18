#pragma once

#include <cstddef>
#include <cstdint>
#include <type_traits>

#include "sts2/ai/state.h"

namespace sts2::ai {

// ---------------------------------------------------------------------------
// ZobristKey — 128-bit composite hash; two independent 64-bit Zobrist halves
// XOR'd from disjoint random key tables. Collision probability ~10^-20 at
// 370M entries (Q2-ADR-010). Layout chosen for absl::flat_hash_map
// compatibility: trivially-copyable, standard-layout, no padding.
// ---------------------------------------------------------------------------
struct ZobristKey {
  uint64_t lo = 0;
  uint64_t hi = 0;

  constexpr bool operator==(const ZobristKey&) const noexcept = default;
};

static_assert(sizeof(ZobristKey) == 16, "ZobristKey must be exactly 16 bytes");
static_assert(std::is_trivially_copyable_v<ZobristKey>);
static_assert(std::is_standard_layout_v<ZobristKey>);

// Hash functor for absl::flat_hash_map<ZobristKey, ...>. `lo` is already a
// high-quality Zobrist hash; mixing `hi` improves bucket spread by avoiding
// degeneracy if the bottom bits of `lo` correlate with the user's load.
struct ZobristKeyHash {
  std::size_t operator()(const ZobristKey& k) const noexcept {
    // FNV/golden-ratio multiplier on the hi half, XOR'd into lo for spread.
    return static_cast<std::size_t>(k.lo ^ (k.hi * 0x9E3779B97F4A7C15ULL));
  }
};

// Compute the 128-bit Zobrist hash of a CompactState. Pure function,
// deterministic across processes/hosts (no random_device, no time-dependent
// state). Same CompactState always produces the same ZobristKey.
[[nodiscard]] ZobristKey zobrist_of(const CompactState& s) noexcept;

// Test-only exposure of committed seeds (algorithm input per Q2-ADR-005;
// rotates algorithm_sha if changed). Kept here so test_zobrist.cc can
// verify deterministic regeneration. New callers require Q2-ADR-010
// amendment.
constexpr uint64_t kZobristSeedLo = 0xC0FFEE12345678ULL;
constexpr uint64_t kZobristSeedHi = 0xDEADBEEF20260517ULL;

}  // namespace sts2::ai
