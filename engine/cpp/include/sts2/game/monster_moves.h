#pragma once

#include <array>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game::monster_moves {

constexpr uint8_t kMaxEffectsPerMove = 3;
constexpr uint8_t kMaxMovesPerMonster = 6;
constexpr uint8_t kMaxSpawnPowers = 3;
// Wave-21: kLeafSlimeS=3, kLeafSlimeM=4, kTwigSlimeS=5, kTwigSlimeM=6 appended.
constexpr std::size_t kMonsterKindCount =
    7;  // kCultistCalcified, kCultistDamp, kLouseProgenitor + 4 slimes

// Discriminates how kMonsterMoveTables resolves the next move.
//   kStrict: deterministic single follow-up via follow_up_index.
//   kRandomBranchCannotRepeat: RandomBranch across N branches, all
//   CannotRepeat. kWeightedRandomCannotRepeat: RandomBranch with per-branch
//   weights +
//                                per-branch CannotRepeat flags.
//
// CRITICAL: kStrict MUST be 0 so zero-init defaults correctly for existing
// cultist + LouseProgenitor entries (which use strict deterministic
// follow-ups).
enum class FollowUpRule : uint8_t {
  kStrict = 0,                      // existing semantics
  kRandomBranchCannotRepeat = 1,    // wave-22 LeafSlimeS
  kWeightedRandomCannotRepeat = 2,  // wave-22 TwigSlimeM
};

constexpr std::size_t kMaxFollowUps = 4;  // max RandomBranch options

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
  uint8_t follow_up_index = 0;  // used for kStrict follow-up
  std::array<MoveEffect, kMaxEffectsPerMove> effects = {};
  uint8_t effect_count = 0;
  uint8_t _pad = 0;
  // Wave-21 schema extension (wave-22.β consumes for slime RandomBranch):
  FollowUpRule follow_up_rule = FollowUpRule::kStrict;  // zero-init = kStrict
  std::array<uint8_t, kMaxFollowUps> branch_indices = {};     // zero-init OK
  std::array<uint8_t, kMaxFollowUps> branch_weights = {};     // zero-init OK
  std::array<bool, kMaxFollowUps> branch_cannot_repeat = {};  // zero-init OK
  uint8_t branch_count = 0;                                   // zero-init OK
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
