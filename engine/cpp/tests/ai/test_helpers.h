#pragma once

#include <cstdint>
#include <cstring>
#include <iomanip>
#include <sstream>
#include <string>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::tests::ai {

// Wave-23/J.beta: CardCounts.counts widened uint8_t → int32_t to match
// upstream STS2's uniform int storage (Q2-ADR-014). Parameter types widened
// to match.
inline sts2::ai::CardCounts make_counts(int32_t s, int32_t d, int32_t n,
                                        int32_t v) {
  sts2::ai::CardCounts c;
  c[sts2::game::CardId::kStrike] = s;
  c[sts2::game::CardId::kDefend] = d;
  c[sts2::game::CardId::kNeutralize] = n;
  c[sts2::game::CardId::kSurvivor] = v;
  return c;
}

}  // namespace sts2::tests::ai

// ---------------------------------------------------------------------------
// snapshot — field-wise hex serializer for EnemyState byte-parity tests.
//
// Serializes EnemyState public fields one-by-one (avoids compiler-padding
// non-portability of raw memcpy on EnemyState). PowerInstance IS pinned at
// 8 B by static_assert with explicit padding fields, so its layout is
// portable; each PowerInstance entry is copied directly as 8 bytes.
//
// Serialization order (load-bearing; matches plan specification):
//   1. get_hp().value()           — int32, 4 bytes little-endian
//   2. get_block().value()        — int32, 4 bytes
//   3. static_cast<uint8_t>(get_kind())
//   4. get_move_index()           — uint8
//   5. static_cast<uint8_t>(get_current_move())
//   6. get_alive() ? 1 : 0       — uint8
//   7. get_performed_first_move() ? 1 : 0 — uint8
//   8. get_power_count()          — uint8
//   9. for i in [0, get_power_count()): 8 bytes of get_powers()[i]
//
// Returns lowercase hex string with no separators.
// ---------------------------------------------------------------------------
namespace snapshot {

[[nodiscard]] inline std::string snapshot(const sts2::ai::EnemyState& e) {
  std::ostringstream out;
  out << std::hex << std::setfill('0');

  auto write_bytes = [&](const void* src, std::size_t n) {
    const auto* p = static_cast<const unsigned char*>(src);
    for (std::size_t i = 0; i < n; ++i) {
      out << std::setw(2) << static_cast<unsigned>(p[i]);
    }
  };

  // 1. hp (int32, little-endian)
  const int32_t hp = e.get_hp().value();
  write_bytes(&hp, 4);
  // 2. block (int32, little-endian)
  const int32_t block = e.get_block().value();
  write_bytes(&block, 4);
  // 3. kind (uint8)
  const uint8_t kind = static_cast<uint8_t>(e.get_kind());
  write_bytes(&kind, 1);
  // 4. move_index (uint8)
  const uint8_t move_idx = e.get_move_index();
  write_bytes(&move_idx, 1);
  // 5. current_move (uint8)
  const uint8_t cur_move = static_cast<uint8_t>(e.get_current_move());
  write_bytes(&cur_move, 1);
  // 6. alive (uint8)
  const uint8_t alive = e.get_alive() ? 1U : 0U;
  write_bytes(&alive, 1);
  // 7. performed_first_move (uint8)
  const uint8_t pfm = e.get_performed_first_move() ? 1U : 0U;
  write_bytes(&pfm, 1);
  // 8. power_count (uint8)
  const uint8_t pcount = e.get_power_count();
  write_bytes(&pcount, 1);
  // 9. power instances (8 bytes each — layout pinned by static_assert)
  const auto& powers = e.get_powers();
  for (uint8_t i = 0; i < pcount; ++i) {
    write_bytes(&powers[i], sizeof(sts2::ai::PowerInstance));
  }

  return out.str();
}

}  // namespace snapshot
