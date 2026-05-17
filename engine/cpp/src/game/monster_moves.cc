// Monster move tables for the data-driven framework (wave-16 foundation).
// Cultist values are mirrored verbatim from the source-of-truth definitions in
// engine/cpp/include/sts2/game/enemies.h (kCultistArchetypes).
//
// Source citation (enemies.h:25-38, trimmed):
//   kCultistArchetypes = {{
//     {.dark_strike_base = 9, .ritual_amount = 2, .hp_min = 38, .hp_max = 41},
//     // CalcifiedCultist
//     {.dark_strike_base = 1, .ritual_amount = 5, .hp_min = 51, .hp_max = 53},
//     // DampCultist
//   }};
// Move sequence (enemies.cc + move_calc.h::next_move):
//   kIncantation (first move) → kDarkStrike (repeated indefinitely).
//   Initial move index = 0 (kIncantation).

#include "sts2/game/monster_moves.h"

namespace sts2::game::monster_moves {

namespace {

// Build the Incantation move for a cultist: buffs self with Ritual stacks.
constexpr MonsterMove make_incantation(int16_t ritual_amount) {
  MonsterMove m;
  m.id = MoveId::kIncantation;
  m.follow_up_index = 1;  // next move is DarkStrike at index 1
  m.effects[0] = MoveEffect{
      .kind = MoveEffectKind::kBuffSelf,
      .value = ritual_amount,
      .power_kind = PowerKind::kRitual,
      ._pad = 0,
  };
  m.effect_count = 1;
  return m;
}

// Build the DarkStrike move for a cultist: attack the player.
constexpr MonsterMove make_dark_strike(int16_t base_damage) {
  MonsterMove m;
  m.id = MoveId::kDarkStrike;
  m.follow_up_index = 1;  // loops: DarkStrike always follows DarkStrike
  m.effects[0] = MoveEffect{
      .kind = MoveEffectKind::kAttack,
      .value = base_damage,
      .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
      ._pad = 0,
  };
  m.effect_count = 1;
  return m;
}

// Build a MonsterMoveTable for a cultist archetype.
constexpr MonsterMoveTable make_cultist_table(int16_t dark_strike_base,
                                              int16_t ritual_amount,
                                              uint8_t hp_min, uint8_t hp_max) {
  MonsterMoveTable t;
  t.moves[0] = make_incantation(ritual_amount);
  t.moves[1] = make_dark_strike(dark_strike_base);
  t.move_count = 2;
  t.initial_move_index = 0;  // kIncantation is the first move
  t.min_hp = hp_min;
  t.max_hp = hp_max;
  t.spawn_power_count = 0;  // cultists have no spawn powers
  return t;
}

}  // namespace

// kMonsterMoveTables[MonsterKind::kCultistCalcified = 0]
// kMonsterMoveTables[MonsterKind::kCultistDamp       = 1]
// kMonsterMoveTables[MonsterKind::kLouseProgenitor   = 2] — zero-initialized
// placeholder; wave-17 populates
const std::array<MonsterMoveTable, kMonsterKindCount> kMonsterMoveTables = {{
    // kCultistCalcified (index 0)
    // Source: enemies.h kCultistArchetypes[0]
    make_cultist_table(/*dark_strike_base=*/9, /*ritual_amount=*/2,
                       /*hp_min=*/38, /*hp_max=*/41),
    // kCultistDamp (index 1)
    // Source: enemies.h kCultistArchetypes[1]
    make_cultist_table(/*dark_strike_base=*/1, /*ritual_amount=*/5,
                       /*hp_min=*/51, /*hp_max=*/53),
    // kLouseProgenitor (index 2) — zero-initialized placeholder; wave-17
    // populates
    MonsterMoveTable{},
}};

uint8_t find_move_index(MonsterKind kind, MoveId id) noexcept {
  const auto kind_idx = static_cast<std::size_t>(kind);
  if (kind_idx >= kMonsterKindCount) {
    return 0xFF;
  }
  const MonsterMoveTable& table = kMonsterMoveTables[kind_idx];
  for (uint8_t i = 0; i < table.move_count; ++i) {
    if (table.moves[i].id == id) {
      return i;
    }
  }
  return 0xFF;
}

}  // namespace sts2::game::monster_moves
