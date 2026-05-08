#pragma once

#include <cstdint>

enum class CardType : int { Attack, Skill };
enum class TargetType : int { Self, AnyEnemy, NoTarget };
enum class CardPile : int { Draw, Hand, Discard, Exhaust };
enum class CardId : int { None, Strike, Defend, Neutralize, Survivor };
enum class PowerKind : int { Weak, Strength, Ritual };
enum class MoveId : int { Incantation, DarkStrike };
enum class CombatSide : int { Player, Enemy };
