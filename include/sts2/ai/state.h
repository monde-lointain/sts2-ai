#pragma once

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <iterator>

#include "sts2/game/card_effects.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"

namespace sts2::game {
class Combat;
}

namespace sts2::ai {

struct CardCounts {
  std::array<uint8_t, std::size(sts2::game::card_effects::kCountedCardIds)>
      counts{};
  static_assert(std::size(sts2::game::card_effects::kCountedCardIds) <= 8,
                "pack_counts uint64 packing limit (8 bits per slot)");

  // to_index() relies on kCountedCardIds being ordered to match CardId enum
  // layout (kNone=0 implicit; entry i is CardId{i+1}). Without this, indexing
  // silently misaligns when a new CardId is added at the wrong enum position.
  static_assert(
      [] {
        for (std::size_t i = 0;
             i < std::size(sts2::game::card_effects::kCountedCardIds); ++i) {
          if (static_cast<int>(
                  sts2::game::card_effects::kCountedCardIds[i]) !=
              static_cast<int>(i) + 1) {
            return false;
          }
        }
        return true;
      }(),
      "kCountedCardIds order must match CardId enum (kNone=0, kStrike=1, ...)");

  // CardId is 1-indexed; kNone=0 is an assert-only sentinel, so we offset by 1
  // to map kStrike..kSurvivor onto array indices 0..3.
  static constexpr std::size_t to_index(sts2::game::CardId id) noexcept {
    assert(id != sts2::game::CardId::kNone);
    return static_cast<std::size_t>(id) - 1;
  }

  uint8_t& operator[](sts2::game::CardId id) noexcept {
    return counts[to_index(id)];
  }
  uint8_t operator[](sts2::game::CardId id) const noexcept {
    return counts[to_index(id)];
  }

  CardCounts& operator+=(const CardCounts& o) noexcept;
  CardCounts& operator-=(const CardCounts& o) noexcept;
  friend CardCounts operator+(CardCounts a, const CardCounts& b) noexcept {
    return a += b;
  }

  [[nodiscard]] bool covers(const CardCounts& subset) const noexcept;
  [[nodiscard]] int total() const noexcept;
  bool operator==(const CardCounts&) const = default;
};

struct EnemyState {
  sts2::game::Stat hp;
  sts2::game::Stat block;
  sts2::game::Stat strength;
  sts2::game::Stat weak;
  sts2::game::Stat dark_strike_base;
  sts2::game::Stat ritual_amount;
  bool just_applied_ritual = false;
  bool performed_first_move = false;
  sts2::game::MoveId current_move = sts2::game::MoveId::kIncantation;
  bool alive = false;
  bool operator==(const EnemyState&) const = default;
};

enum class Phase : uint8_t { kPlayerActing, kAtChanceDraw };

struct CompactState {
  sts2::game::Stat player_hp;
  sts2::game::Stat player_block;
  sts2::game::Stat player_strength;
  sts2::game::Stat player_weak;
  sts2::game::Stat energy;
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
