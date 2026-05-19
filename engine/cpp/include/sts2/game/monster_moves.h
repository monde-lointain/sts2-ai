#pragma once

#include <array>
#include <cstdint>

#include "sts2/game/types.h"

namespace sts2::game::monster_moves {

constexpr uint8_t kMaxEffectsPerMove = 3;
constexpr uint8_t kMaxMovesPerMonster = 6;
constexpr uint8_t kMaxSpawnPowers = 3;
// Wave-26/M.α APPEND-ONLY: max OnDeath spawn entries per MonsterMoveTable.
// GremlinMerc spawns exactly 2 (SneakyGremlin + FatGremlin); sized to 3 for
// headroom (matches kMaxSpawnPowers convention).
constexpr uint8_t kMaxOnDeathSpawns = 3;
// Wave-21: kLeafSlimeS=3, kLeafSlimeM=4, kTwigSlimeS=5, kTwigSlimeM=6 appended.
// Wave-24/K.β: kNibbit=7 appended.
// Wave-26/M.β APPENDS kGremlinMerc=8, kSneakyGremlin=9, kFatGremlin=10 →
// kMonsterKindCount bump 8→11 (data populated in M.β). M.α defines the enum
// values + the SpawnEntry schema below (substrate-critical declarations);
// kMonsterKindCount stays 8 in M.α so the table size matches the M.α data.
constexpr std::size_t kMonsterKindCount =
    8;  // kCultistCalcified, kCultistDamp, kLouseProgenitor + 4 slimes + Nibbit

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
};
static_assert(
    sizeof(MoveEffect) == 8,
    "Wave-23/J.beta: MoveEffect must be 8 B (int32 value + 4 B bytes)");

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

// Wave-26/M.α APPEND-ONLY: SpawnEntry — OnDeath spawn descriptor.
// Read by transition.cc::do_surprise_spawn when a kSurprise-bearing enemy
// dies. Each entry describes a single spawned enemy:
//   kind                  — MonsterKind of the spawned enemy.
//   deterministic_hp      — B1 median HP (B1 mode is deterministic; M.β picks
//                           the median of each spawn's HP range to avoid an
//                           extra chance node).
//   initial_current_move  — starting MoveId; intended to be kSpawnedMove
//                           (effect_count=0 no-op) so the spawn does not act
//                           in the same enemy phase it spawned, then rolls
//                           into its real first move next turn.
// Field order places the 4-byte MoveId after deterministic_hp for natural
// 4-byte alignment without internal padding. M.α writes the SCHEMA; M.β fills
// the DATA in kMonsterMoveTables[kGremlinMerc].on_death_spawns.
struct SpawnEntry {
  int32_t deterministic_hp = 0;
  MoveId initial_current_move = MoveId::kIncantation;  // 4B (enum:int)
  MonsterKind kind = MonsterKind::kCultistCalcified;
  uint8_t _pad = 0;
  uint8_t _pad2 = 0;
  uint8_t _pad3 = 0;
  bool operator==(const SpawnEntry&) const = default;
};
static_assert(sizeof(SpawnEntry) == 12,
              "Wave-26/M.α: SpawnEntry must be 12 B "
              "(int32 hp 4B + MoveId 4B + MonsterKind 1B + 3B pad)");

// Wave-23/J.beta: min_hp / max_hp widened uint8_t → int32_t to match
// upstream's uniform int stat storage (Q2-ADR-014). SlimedBerserker (HP
// 261-281) already exceeded the uint8 bound; widening here surfaces the
// upstream contract directly. Field order kept readable (count + index
// first, then HP, then spawn-power data).
//
// Wave-26/M.α APPENDS on_death_spawns + on_death_spawn_count. Existing
// kMonsterMoveTables entries zero-init these fields (no OnDeath behavior;
// matches pre-M.α semantics for cultist/Louse/slime/Nibbit). M.β populates
// the GremlinMerc entry with [SneakyGremlin + FatGremlin].
struct MonsterMoveTable {
  std::array<MonsterMove, kMaxMovesPerMonster> moves = {};
  uint8_t move_count = 0;
  uint8_t initial_move_index = 0;
  int32_t min_hp = 0;
  int32_t max_hp = 0;
  std::array<SpawnPowerEntry, kMaxSpawnPowers> spawn_powers = {};
  uint8_t spawn_power_count = 0;
  // Wave-26/M.α APPEND-ONLY: OnDeath spawn schema. Read by
  // transition.cc::do_surprise_spawn after a kSurprise-bearing enemy's HP
  // drops to ≤ 0 via damage. on_death_spawn_count > 0 implies the carrier
  // should also be tagged with PowerKind::kSurprise so the trigger fires.
  std::array<SpawnEntry, kMaxOnDeathSpawns> on_death_spawns = {};
  uint8_t on_death_spawn_count = 0;
};

extern const std::array<MonsterMoveTable, kMonsterKindCount> kMonsterMoveTables;

// Find the index into kMonsterMoveTables[kind].moves[] for the given MoveId.
// Returns 0xFF if not found.
uint8_t find_move_index(MonsterKind kind, MoveId id) noexcept;

}  // namespace sts2::game::monster_moves
