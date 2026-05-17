#pragma once

#include <array>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game::monster_moves {

constexpr uint8_t kMaxEffectsPerMove = 3;
constexpr uint8_t kMaxMovesPerMonster = 6;
constexpr uint8_t kMaxSpawnPowers = 3;
constexpr std::size_t kMonsterKindCount =
    3;  // kCultistCalcified, kCultistDamp, kLouseProgenitor

struct MoveEffect {
  MoveEffectKind kind = MoveEffectKind::kNone;
  int16_t value = 0;
  // NOTE: PowerKind underlying type is int (4 bytes), not uint8_t; sizeof this
  // struct is larger than the 6-byte spec target. Wave-17 may migrate PowerKind
  // to uint8_t to hit the target layout.
  PowerKind power_kind = PowerKind::kWeak;
  uint8_t _pad = 0;
  bool operator==(const MoveEffect&) const = default;
};

struct MonsterMove {
  MoveId id = MoveId::kIncantation;
  uint8_t follow_up_index = 0;
  std::array<MoveEffect, kMaxEffectsPerMove> effects = {};
  uint8_t effect_count = 0;
  uint8_t _pad = 0;
  bool operator==(const MonsterMove&) const = default;
};

struct SpawnPowerEntry {
  // NOTE: PowerKind underlying type is int; sizeof is 8, not 4 as spec targets.
  PowerKind kind = PowerKind::kWeak;
  int16_t stacks = 0;
  uint8_t _pad = 0;
  bool operator==(const SpawnPowerEntry&) const = default;
};

struct MonsterMoveTable {
  std::array<MonsterMove, kMaxMovesPerMonster> moves = {};
  uint8_t move_count = 0;
  uint8_t initial_move_index = 0;
  uint8_t min_hp = 0;
  uint8_t max_hp = 0;
  std::array<SpawnPowerEntry, kMaxSpawnPowers> spawn_powers = {};
  uint8_t spawn_power_count = 0;
};

extern const std::array<MonsterMoveTable, kMonsterKindCount> kMonsterMoveTables;

// Find the index into kMonsterMoveTables[kind].moves[] for the given MoveId.
// Returns 0xFF if not found.
uint8_t find_move_index(MonsterKind kind, MoveId id) noexcept;

}  // namespace sts2::game::monster_moves
