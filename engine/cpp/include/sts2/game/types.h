#pragma once

#include <cstdint>

namespace sts2::game {

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
enum class PowerKind : int {
  kWeak = 0,
  kStrength = 1,
  kRitual = 2,
  kCurlUp = 3,      // reserved for wave-17
  kFrail = 4,       // reserved for wave-17
  kVulnerable = 5,  // reserved for future use
};

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
  kTackleMove = 5,   // LeafSlimeS + TwigSlimeS
  kGoopMove = 6,     // LeafSlimeS
  kClumpShot = 7,    // LeafSlimeM
  kStickyShot = 8,   // LeafSlimeM + TwigSlimeM
  kPokeyPounce = 9,  // TwigSlimeM
};

enum class MonsterKind : uint8_t {
  kCultistCalcified = 0,
  kCultistDamp = 1,
  kLouseProgenitor = 2,  // reserved for wave-17
  // Wave-21 appended (slime port; data populated in wave-22.β):
  kLeafSlimeS = 3,
  kLeafSlimeM = 4,
  kTwigSlimeS = 5,
  kTwigSlimeM = 6,
};

enum class HookPoint : uint8_t {
  kOnSpawn,
  kBeforeAttackDamage,
  kBeforeBlockGain,
  kAfterDamageReceived,
  kAfterCardPlayedFinished,
  kAtEnemyTurnStart,
  kAtEnemyTurnEnd,
  kAtPlayerTurnStart,
  kAtPlayerTurnEnd,
};

// Wave-21 schema for the slime port; data-driven enemy actions consume this
// enum. Existing cultist + LouseProgenitor code paths bypass it (handcoded
// dispatch). Wave-22.α APPENDS kAddStatusCard for slime GOOP / STICKY_SHOT
// moves which place a Slimed card in the player's discard pile.
enum class MoveEffectKind : uint8_t {
  kNone = 0,
  kAttack = 1,
  kDefend = 2,
  kBuffSelf = 3,
  kDebuffPlayer = 4,
  kAddStatusCard = 5,  // wave-22.α (LeafSlimeS GOOP, TwigSlime* STICKY_SHOT)
};

}  // namespace sts2::game
