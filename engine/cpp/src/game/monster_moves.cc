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
//
// LouseProgenitor (wave-18):
//   Source: engine/headless/src/.../Phase1Monsters.cs:157-190 (Q1 port)
//   Upstream: src/Core/Models/Monsters/LouseProgenitor.cs:36-122
//   HP: 134-136 (A0). Rotation: WEB_CANNON(0) → CURL_AND_GROW(1) → POUNCE(2).
//   WEB_CANNON:    attack 9 + apply 2 Frail to player.
//   CURL_AND_GROW: defend 14 block (self) + 5 Strength to self.
//   POUNCE:        attack 14.  (A0 baseline; 16 is A11+ per
//   LouseProgenitor.cs:63) Spawn power: CurlUp(14).

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

// Build the LouseProgenitor move table (wave-18).
// Move indices: WEB_CANNON=0, CURL_AND_GROW=1, POUNCE=2.
// follow_up_index chains: 0→1→2→0.
constexpr MonsterMoveTable make_louse_progenitor_table() {
  MonsterMoveTable t;

  // Index 0: WEB_CANNON — attack 9 + apply 2 Frail to player.
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kWebCannon;
    m.follow_up_index = 1;  // → CURL_AND_GROW
    m.effects[0] = MoveEffect{
        .kind = MoveEffectKind::kAttack,
        .value = 9,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
    };
    m.effects[1] = MoveEffect{
        .kind = MoveEffectKind::kDebuffPlayer,
        .value = 2,
        .power_kind = PowerKind::kFrail,
        ._pad = 0,
    };
    m.effect_count = 2;
  }

  // Index 1: CURL_AND_GROW — defend 14 block (self) + 5 Strength to self.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kCurlAndGrow;
    m.follow_up_index = 2;  // → POUNCE
    m.effects[0] = MoveEffect{
        .kind = MoveEffectKind::kDefend,
        .value = 14,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kDefend
        ._pad = 0,
    };
    m.effects[1] = MoveEffect{
        .kind = MoveEffectKind::kBuffSelf,
        .value = 5,
        .power_kind = PowerKind::kStrength,
        ._pad = 0,
    };
    m.effect_count = 2;
  }

  // Index 2: POUNCE — attack 14.
  // POUNCE: 14 damage at A0 per upstream Models/Monsters/LouseProgenitor.cs:63
  // (16 is the DeadlyEnemies/A11+ ascension value; Q2 Phase-1A ships A0 only
  // per Q2-ADR-002).
  {
    MonsterMove& m = t.moves[2];
    m.id = MoveId::kPounce;
    m.follow_up_index = 0;  // → WEB_CANNON
    m.effects[0] = MoveEffect{
        .kind = MoveEffectKind::kAttack,
        .value = 14,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
    };
    m.effect_count = 1;
  }

  t.move_count = 3;
  t.initial_move_index = 0;  // WEB_CANNON is the first move
  t.min_hp = 134;            // A0 min HP
  t.max_hp = 136;            // A0 max HP
  // Spawn power: CurlUp(14). Upstream AfterAddedToRoom applies CurlUpPower
  // with CurlBlock=14 at A0.
  t.spawn_powers[0] = SpawnPowerEntry{
      .kind = PowerKind::kCurlUp,
      .stacks = 14,
      ._pad = 0,
  };
  t.spawn_power_count = 1;
  return t;
}

}  // namespace

// kMonsterMoveTables[MonsterKind::kCultistCalcified = 0]
// kMonsterMoveTables[MonsterKind::kCultistDamp       = 1]
// kMonsterMoveTables[MonsterKind::kLouseProgenitor   = 2]
// kMonsterMoveTables[MonsterKind::kLeafSlimeS        = 3] (wave-22.β populates)
// kMonsterMoveTables[MonsterKind::kLeafSlimeM        = 4] (wave-22.β populates)
// kMonsterMoveTables[MonsterKind::kTwigSlimeS        = 5] (wave-22.β populates)
// kMonsterMoveTables[MonsterKind::kTwigSlimeM        = 6] (wave-22.β populates)
const std::array<MonsterMoveTable, kMonsterKindCount> kMonsterMoveTables = {{
    // kCultistCalcified (index 0)
    // Source: enemies.h kCultistArchetypes[0]
    make_cultist_table(/*dark_strike_base=*/9, /*ritual_amount=*/2,
                       /*hp_min=*/38, /*hp_max=*/41),
    // kCultistDamp (index 1)
    // Source: enemies.h kCultistArchetypes[1]
    make_cultist_table(/*dark_strike_base=*/1, /*ritual_amount=*/5,
                       /*hp_min=*/51, /*hp_max=*/53),
    // kLouseProgenitor (index 2) — wave-18
    make_louse_progenitor_table(),
    // kLeafSlimeS (index 3) — wave-22.β populates real data
    MonsterMoveTable{},
    // kLeafSlimeM (index 4) — wave-22.β populates real data
    MonsterMoveTable{},
    // kTwigSlimeS (index 5) — wave-22.β populates real data
    MonsterMoveTable{},
    // kTwigSlimeM (index 6) — wave-22.β populates real data
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
