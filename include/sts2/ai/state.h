#pragma once

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game {
class Combat;
}

namespace sts2::ai {

struct CardCounts {
  std::array<uint8_t, 4> counts{};

  static constexpr std::size_t to_index(sts2::game::CardId id) {
    assert(id != sts2::game::CardId::kNone);
    return static_cast<std::size_t>(id) - 1;
  }

  uint8_t& operator[](sts2::game::CardId id) { return counts[to_index(id)]; }
  uint8_t operator[](sts2::game::CardId id) const {
    return counts[to_index(id)];
  }

  CardCounts& operator+=(const CardCounts& o);
  CardCounts& operator-=(const CardCounts& o);
  friend CardCounts operator+(CardCounts a, const CardCounts& b) {
    return a += b;
  }

  [[nodiscard]] bool covers(const CardCounts& subset) const noexcept;
  [[nodiscard]] int total() const noexcept;
  bool operator==(const CardCounts&) const = default;
};

struct EnemyState {
  uint8_t hp = 0;
  uint8_t block = 0;
  uint8_t strength = 0;
  uint8_t weak = 0;
  uint8_t dark_strike_base = 0;
  uint8_t ritual_amount = 0;
  bool just_applied_ritual = false;
  bool performed_first_move = false;
  sts2::game::MoveId current_move = sts2::game::MoveId::kIncantation;
  bool alive = false;
  bool operator==(const EnemyState&) const = default;
};

enum class Phase : uint8_t { kPlayerActing, kAtChanceDraw };

struct CompactState {
  uint8_t player_hp = 0;
  uint8_t player_block = 0;
  uint8_t player_strength = 0;
  uint8_t player_weak = 0;
  uint8_t energy = 0;
  uint16_t round = 1;
  Phase phase = Phase::kPlayerActing;
  std::array<EnemyState, 2> enemies{};
  CardCounts hand{};
  CardCounts draw{};
  CardCounts discard{};
  bool operator==(const CompactState&) const = default;
};

CompactState from_combat(const sts2::game::Combat& combat);

}  // namespace sts2::ai
