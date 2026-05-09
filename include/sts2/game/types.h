#pragma once

namespace sts2::game {

enum class CardType : int { kAttack, kSkill };
enum class TargetType : int { kSelf, kAnyEnemy, kNoTarget };
enum class CardPile : int { kDraw, kHand, kDiscard, kExhaust };
enum class CardId : int { kNone, kStrike, kDefend, kNeutralize, kSurvivor };
enum class PowerKind : int { kWeak, kStrength, kRitual };
enum class MoveId : int { kIncantation, kDarkStrike };
enum class CombatSide : int { kPlayer, kEnemy };

}  // namespace sts2::game
