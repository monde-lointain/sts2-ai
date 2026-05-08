#pragma once

#include <cstdint>

enum class CardType { Attack, Skill };
enum class TargetType { Self, AnyEnemy, NoTarget };
enum class CardPile { Draw, Hand, Discard, Exhaust };
enum class CardId { None, Strike, Defend, Neutralize, Survivor };
enum class PowerKind { Weak, Strength, Ritual };
enum class MoveId { Incantation, DarkStrike };
enum class CombatSide { Player, Enemy };
