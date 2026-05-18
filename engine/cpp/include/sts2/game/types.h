#pragma once

#include <cstdint>

namespace sts2::game {

enum class CardType : int { kAttack, kSkill };
enum class TargetType : int { kSelf, kAnyEnemy, kNoTarget };
enum class CardId : int { kNone, kStrike, kDefend, kNeutralize, kSurvivor };

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

enum class MoveEffectKind : uint8_t {
  kNone = 0,
  kAttack,
  kDefend,
  kBuffSelf,
  kDebuffPlayer,
};

}  // namespace sts2::game
