#pragma once

#include <array>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game::monster_moves {

constexpr uint8_t kMaxEffectsPerMove = 3;
constexpr uint8_t kMaxMovesPerMonster = 6;
constexpr uint8_t kMaxSpawnPowers = 3;
// Wave-21: kLeafSlimeS=3, kLeafSlimeM=4, kTwigSlimeS=5, kTwigSlimeM=6 appended.
// Wave-24/K.β: kNibbit=7 appended.
constexpr std::size_t kMonsterKindCount =
    8;  // kCultistCalcified, kCultistDamp, kLouseProgenitor + 4 slimes + Nibbit
static_assert(kMonsterKindCount == sts2::game::kMonsterKindCardinality,
              "kMonsterKindCount (monster-moves table size) must equal "
              "kMonsterKindCardinality (Zobrist table outer dim) — bump both "
              "together when adding a new MonsterKind");

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

// Wave-23/J.beta: value widened int16_t → int32_t to match upstream STS2's
// uniform int storage for move effect values (Q2-ADR-014). Field order is
// {value(int32_t), kind(1), power_kind(1), _pad(1), _pad2(1)} for natural
// 4-byte alignment: value(4) + kind(1) + power_kind(1) + _pad(1) + _pad2(1)
// = 8B, struct alignment 4 (from int32_t value). `_pad` retained as an
// explicit slot for reviewability; `_pad2` is alignment fill.
struct MoveEffect {
  int32_t value = 0;
  MoveEffectKind kind = MoveEffectKind::kNone;
  PowerKind power_kind = PowerKind::kWeak;
  uint8_t _pad = 0;
  uint8_t _pad2 = 0;
  bool operator==(const MoveEffect&) const = default;

  // Static factories — power_kind=kWeak (=0) for kinds that don't use it, to
  // preserve operator== invariance with existing aggregate-init convention.
  static constexpr MoveEffect attack(int32_t damage) noexcept {
    return {damage, MoveEffectKind::kAttack, PowerKind::kWeak, 0, 0};
  }
  static constexpr MoveEffect defend(int32_t block) noexcept {
    return {block, MoveEffectKind::kDefend, PowerKind::kWeak, 0, 0};
  }
  static constexpr MoveEffect buff_self(PowerKind k, int32_t v) noexcept {
    return {v, MoveEffectKind::kBuffSelf, k, 0, 0};
  }
  static constexpr MoveEffect buff_enemy(PowerKind k, int32_t v) noexcept {
    return {v, MoveEffectKind::kBuffEnemy, k, 0, 0};
  }
  static constexpr MoveEffect block_self(int32_t v) noexcept {
    return {v, MoveEffectKind::kBlockSelf, PowerKind::kWeak, 0, 0};
  }
  static constexpr MoveEffect debuff_player(PowerKind k, int32_t v) noexcept {
    return {v, MoveEffectKind::kDebuffPlayer, k, 0, 0};
  }
  static constexpr MoveEffect add_status_card(int32_t v) noexcept {
    return {v, MoveEffectKind::kAddStatusCard, PowerKind::kWeak, 0, 0};
  }
};
static_assert(
    sizeof(MoveEffect) == 8,
    "Wave-23/J.beta: MoveEffect must be 8 B (int32 value + 4 B bytes)");
static_assert(std::is_aggregate_v<MoveEffect>,
              "MoveEffect must remain an aggregate — factories must NOT add a "
              "user-declared ctor");

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

// Wave-23/J.beta: stacks widened int16_t → int32_t to match upstream's
// uniform int stat storage (Q2-ADR-014). Field order is
// {stacks(int32_t), kind(uint8_t), 3 B pad} for natural 4-byte alignment:
// stacks(4) + kind(1) + 3B compiler pad = 8B, struct alignment 4.
struct SpawnPowerEntry {
  int32_t stacks = 0;
  PowerKind kind = PowerKind::kWeak;
  bool operator==(const SpawnPowerEntry&) const = default;
};
static_assert(sizeof(SpawnPowerEntry) == 8,
              "Wave-23/J.beta: SpawnPowerEntry must be 8 B (int32 stacks + "
              "1 B kind + 3 B pad)");

// Wave-23/J.beta: min_hp / max_hp widened uint8_t → int32_t to match
// upstream's uniform int stat storage (Q2-ADR-014). SlimedBerserker (HP
// 261-281) already exceeded the uint8 bound; widening here surfaces the
// upstream contract directly. Field order kept readable (count + index
// first, then HP, then spawn-power data).
struct MonsterMoveTable {
  std::array<MonsterMove, kMaxMovesPerMonster> moves = {};
  uint8_t move_count = 0;
  uint8_t initial_move_index = 0;
  int32_t min_hp = 0;
  int32_t max_hp = 0;
  std::array<SpawnPowerEntry, kMaxSpawnPowers> spawn_powers = {};
  uint8_t spawn_power_count = 0;
};

extern const std::array<MonsterMoveTable, kMonsterKindCount> kMonsterMoveTables;

// Find the index into kMonsterMoveTables[kind].moves[] for the given MoveId.
// Returns 0xFF if not found.
uint8_t find_move_index(MonsterKind kind, MoveId id) noexcept;

}  // namespace sts2::game::monster_moves
