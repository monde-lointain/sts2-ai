#pragma once

#include <array>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game {
class Combat;
}

namespace sts2::ai {

struct CardCounts {
  uint8_t strike = 0;
  uint8_t defend = 0;
  uint8_t neutralize = 0;
  uint8_t survivor = 0;
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
