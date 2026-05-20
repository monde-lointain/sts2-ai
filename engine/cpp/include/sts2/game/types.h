#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <type_traits>

namespace sts2::game {

namespace detail {

// Returns true iff the values in arr are exactly 0, 1, 2, …, N-1 in order.
// Used to static_assert that kAll<Enum>s arrays are dense and contiguous so
// that array[static_cast<std::size_t>(e)] indexing is always safe.
template <typename Enum, std::size_t N>
constexpr bool enum_is_contiguous(const std::array<Enum, N>& arr) {
  for (std::size_t i = 0; i < N; ++i) {
    if (static_cast<std::underlying_type_t<Enum>>(arr[i]) !=
        static_cast<std::underlying_type_t<Enum>>(i)) {
      return false;
    }
  }
  return true;
}

}  // namespace detail

// CardType: kAttack=0, kSkill=1 preserved from original.
// Wave-22 APPENDED kStatus=2 for the Slimed status card port (must NOT
// renumber — render.cc treats kAttack specially via ternary; new values
// fall into the default "non-attack" rendering, which is the desired
// behavior for status cards.).
enum class CardType : int { kAttack = 0, kSkill = 1, kStatus = 2 };
enum class TargetType : int { kSelf, kAnyEnemy, kNoTarget };
// CardId: kNone=0..kSurvivor=4 preserved from original.
// Wave-22 APPENDED kSlimed=5 for the Slimed status card port. CardCounts
// indexing depends on kCountedCardIds ordering matching CardId-1 (see
// state.h::CardCounts::to_index static_assert).
enum class CardId : int {
  kNone = 0,
  kStrike = 1,
  kDefend = 2,
  kNeutralize = 3,
  kSurvivor = 4,
  kSlimed = 5,  // wave-22.α
};

// Stable order: existing values fixed; new values append. NEVER reorder.
// kWeak=0, kStrength=1, kRitual=2 preserved from original definition.
//
// Wave-22-fix-4/H.gamma: backing type int → uint8_t. Shrinks
// SpawnPowerEntry 8B → 4B (kind 1 + 1B compiler pad + stacks 2, naturally
// aligned; explicit `_pad` field removed). PowerInstance stays 8B because its
// `_pad` byte is load-bearing (stores CurlUp card-stamp in transition.cc; see
// {get,set}_curl_up_stored_card in src/ai/transition.cc).
// Q2-ADR-013 Amendment 4 §Compression. Enum tail (kVulnerable=5) and all six
// values fit in uint8_t [0,255].
enum class PowerKind : uint8_t {
  kWeak = 0,
  kStrength = 1,
  kRitual = 2,
  kCurlUp = 3,      // reserved for wave-17
  kFrail = 4,       // reserved for wave-17
  kVulnerable = 5,  // reserved for future use
};

inline constexpr std::array<PowerKind, 6> kAllPowerKinds = {
    PowerKind::kWeak,   PowerKind::kStrength, PowerKind::kRitual,
    PowerKind::kCurlUp, PowerKind::kFrail,    PowerKind::kVulnerable,
};
static_assert(detail::enum_is_contiguous(kAllPowerKinds));

inline constexpr std::size_t kPowerKindCardinality = kAllPowerKinds.size();

// kIncantation=0, kDarkStrike=1 preserved from original definition.
// kWebCannon, kCurlAndGrow, kPounce reserved for wave-17 (LouseProgenitor).
// Wave-21: slime moves appended (data populated in wave-22.β).
enum class MoveId : int {
  kIncantation = 0,
  kDarkStrike = 1,
  kWebCannon = 2,    // reserved for wave-17
  kCurlAndGrow = 3,  // reserved for wave-17
  kPounce = 4,       // reserved for wave-17
  // Wave-21 appended (slime moves; data populated in wave-22.β):
  kTackleMove = 5,   // LeafSlimeS + TwigSlimeS (REUSED for SneakyGremlin TACKLE
                     // in wave-26/M.β; per-monster move table stores damage.)
  kGoopMove = 6,     // LeafSlimeS
  kClumpShot = 7,    // LeafSlimeM
  kStickyShot = 8,   // LeafSlimeM + TwigSlimeM
  kPokeyPounce = 9,  // TwigSlimeM
  // Wave-24/K.β APPEND-ONLY: Nibbit moves.
  kButtMove = 10,   // Nibbit BUTT_MOVE
  kSliceMove = 11,  // Nibbit SLICE_MOVE
  kHissMove = 12,   // Nibbit HISS_MOVE
};

// Enumerated list of every MoveId value. C++ has no built-in enum reflection;
// this array is the source-of-truth for round-trip helpers (move_calc.h)
// and for any code that needs to iterate every MoveId value.
inline constexpr std::array<MoveId, 13> kAllMoveIds = {
    MoveId::kIncantation, MoveId::kDarkStrike, MoveId::kWebCannon,
    MoveId::kCurlAndGrow, MoveId::kPounce,     MoveId::kTackleMove,
    MoveId::kGoopMove,    MoveId::kClumpShot,  MoveId::kStickyShot,
    MoveId::kPokeyPounce, MoveId::kButtMove,   MoveId::kSliceMove,
    MoveId::kHissMove,
};
static_assert(detail::enum_is_contiguous(kAllMoveIds));

// Cardinality of the MoveId enum, derived from the source-of-truth array.
// Downstream consumers (Zobrist key-table dimensions; move_calc constexpr
// table sizes) use this constant; adding a MoveId entry to kAllMoveIds above
// automatically updates it.
inline constexpr std::size_t kMoveIdCardinality = kAllMoveIds.size();

enum class MonsterKind : uint8_t {
  kCultistCalcified = 0,
  kCultistDamp = 1,
  kLouseProgenitor = 2,  // reserved for wave-17
  // Wave-21 appended (slime port; data populated in wave-22.β):
  kLeafSlimeS = 3,
  kLeafSlimeM = 4,
  kTwigSlimeS = 5,
  kTwigSlimeM = 6,
  kNibbit = 7,  // wave-24/K.β APPEND-ONLY
};

inline constexpr std::array<MonsterKind, 8> kAllMonsterKinds = {
    MonsterKind::kCultistCalcified, MonsterKind::kCultistDamp,
    MonsterKind::kLouseProgenitor,  MonsterKind::kLeafSlimeS,
    MonsterKind::kLeafSlimeM,       MonsterKind::kTwigSlimeS,
    MonsterKind::kTwigSlimeM,       MonsterKind::kNibbit,
};
static_assert(detail::enum_is_contiguous(kAllMonsterKinds));

// Cardinality constants for Zobrist key-table outer dimensions. Derived from
// source-of-truth arrays (wave-33/A.α); APPEND-ONLY fill-order contract in
// zobrist.cc must be respected when bumping either value.
inline constexpr std::size_t kMonsterKindCardinality = kAllMonsterKinds.size();

// Wave-21 schema for the slime port; data-driven enemy actions consume this
// enum. Existing cultist + LouseProgenitor code paths bypass it (handcoded
// dispatch). Wave-22.α APPENDS kAddStatusCard for slime GOOP / STICKY_SHOT
// moves which place a Slimed card in the player's discard pile.
// Wave-24/K.α APPENDS kBuffEnemy (Nibbit HISS) + kBlockSelf (Nibbit SLICE):
//   kBuffEnemy: applies stacks of MoveEffect.power_kind to the acting enemy
//     at MoveEffect.value. Targets SELF; buff-other-enemy needs a separate
//     kind (not in scope). Dispatch uses generic powers::add_power — no
//     Ritual side-effects (just_applied flag untouched).
//   kBlockSelf: applies MoveEffect.value block to the acting enemy. Enemy
//     block decays at end of each enemy's individual turn (added in
//     do_enemy_act). NOT Zobrist-hashed (MoveEffectKind is a behavior tag,
//     not state — see zobrist.cc cardinality audit).
enum class MoveEffectKind : uint8_t {
  kNone = 0,
  kAttack = 1,
  kDefend = 2,
  kBuffSelf = 3,
  kDebuffPlayer = 4,
  kAddStatusCard = 5,  // wave-22.α (LeafSlimeS GOOP, TwigSlime* STICKY_SHOT)
  kBuffEnemy = 6,      // wave-24/K.α (Nibbit HISS — Strength self-buff)
  kBlockSelf = 7,      // wave-24/K.α (Nibbit SLICE — block self)
};

inline constexpr std::array<MoveEffectKind, 8> kAllMoveEffectKinds = {
    MoveEffectKind::kNone,         MoveEffectKind::kAttack,
    MoveEffectKind::kDefend,       MoveEffectKind::kBuffSelf,
    MoveEffectKind::kDebuffPlayer, MoveEffectKind::kAddStatusCard,
    MoveEffectKind::kBuffEnemy,    MoveEffectKind::kBlockSelf,
};
static_assert(detail::enum_is_contiguous(kAllMoveEffectKinds));

inline constexpr std::size_t kMoveEffectKindCardinality =
    kAllMoveEffectKinds.size();

}  // namespace sts2::game
