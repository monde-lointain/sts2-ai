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
//
// Slime monsters (wave-22.β):
//   LeafSlimeS:  LeafSlimeS.cs:20-39.  HP 11-15. TACKLE(3) / GOOP(Slimed×1).
//                RandomBranchCannotRepeat; both branches CannotRepeat.
//   LeafSlimeM:  LeafSlimeM.cs:22-40.  HP 32-35. CLUMP_SHOT(8) ↔
//   STICKY_SHOT(Slimed×2).
//                kStrict strict alternation; initial=STICKY_SHOT.
//   TwigSlimeS:  TwigSlimeS.cs:15-27.  HP  7-11. TACKLE(4) self-loop kStrict.
//   TwigSlimeM:  TwigSlimeM.cs:23-42.  HP 26-28. POKEY_POUNCE(11) /
//   STICKY_SHOT(Slimed×1).
//                kWeightedRandomCannotRepeat; weights {2,1}; only STICKY
//                CannotRepeat; initial=STICKY_SHOT.

#include "sts2/game/monster_moves.h"

namespace sts2::game::monster_moves {

namespace {

// Build the Incantation move for a cultist: buffs self with Ritual stacks.
constexpr MonsterMove make_incantation(int32_t ritual_amount) {
  MonsterMove m;
  m.id = MoveId::kIncantation;
  m.follow_up_index = 1;  // next move is DarkStrike at index 1
  m.effects[0] = MoveEffect{
      .value = ritual_amount,
      .kind = MoveEffectKind::kBuffSelf,
      .power_kind = PowerKind::kRitual,
      ._pad = 0,
      ._pad2 = 0,
  };
  m.effect_count = 1;
  return m;
}

// Build the DarkStrike move for a cultist: attack the player.
constexpr MonsterMove make_dark_strike(int32_t base_damage) {
  MonsterMove m;
  m.id = MoveId::kDarkStrike;
  m.follow_up_index = 1;  // loops: DarkStrike always follows DarkStrike
  m.effects[0] = MoveEffect{
      .value = base_damage,
      .kind = MoveEffectKind::kAttack,
      .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
      ._pad = 0,
      ._pad2 = 0,
  };
  m.effect_count = 1;
  return m;
}

// Build a MonsterMoveTable for a cultist archetype.
constexpr MonsterMoveTable make_cultist_table(int32_t dark_strike_base,
                                              int32_t ritual_amount,
                                              int32_t hp_min, int32_t hp_max) {
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
        .value = 9,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 2,
        .kind = MoveEffectKind::kDebuffPlayer,
        .power_kind = PowerKind::kFrail,
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 2;
  }

  // Index 1: CURL_AND_GROW — defend 14 block (self) + 5 Strength to self.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kCurlAndGrow;
    m.follow_up_index = 2;  // → POUNCE
    m.effects[0] = MoveEffect{
        .value = 14,
        .kind = MoveEffectKind::kDefend,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kDefend
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 5,
        .kind = MoveEffectKind::kBuffSelf,
        .power_kind = PowerKind::kStrength,
        ._pad = 0,
        ._pad2 = 0,
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
        .value = 14,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
  }

  t.move_count = 3;
  t.initial_move_index = 0;  // WEB_CANNON is the first move
  t.min_hp = 134;            // A0 min HP
  t.max_hp = 136;            // A0 max HP
  // Spawn power: CurlUp(14). Upstream AfterAddedToRoom applies CurlUpPower
  // with CurlBlock=14 at A0.
  // SpawnPowerEntry post-wave-23/J.beta: 8B (int32 stacks + 1B kind + 3B pad).
  t.spawn_powers[0] = SpawnPowerEntry{
      .stacks = 14,
      .kind = PowerKind::kCurlUp,
  };
  t.spawn_power_count = 1;
  return t;
}

// ---------------------------------------------------------------------------
// Slime table builders (wave-22.β)
// ---------------------------------------------------------------------------

// LeafSlimeS (wave-22.β)
// Source: LeafSlimeS.cs:20-39 (A0 baseline)
// HP 11-15. Moves: TACKLE_MOVE(0)=kAttack/3, GOOP_MOVE(1)=kAddStatusCard/1.
// Both share a RandomBranchState with CannotRepeat; post-turn-1 alternates.
// follow_up_rule=kRandomBranchCannotRepeat on both moves (index into branch
// table); initial=TACKLE_MOVE (first move in .cs move list, index 0).
constexpr MonsterMoveTable make_leaf_slime_s_table() {
  MonsterMoveTable t;

  // Index 0: TACKLE_MOVE — attack 3.
  // Source: LeafSlimeS.cs:24 (TackleDamage A0=3), :31 (MoveState name).
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kTackleMove;
    m.follow_up_rule = FollowUpRule::kRandomBranchCannotRepeat;
    m.effects[0] = MoveEffect{
        .value = 3,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.branch_indices = {0, 1, 0, 0};
    m.branch_weights = {1, 1, 0, 0};
    m.branch_cannot_repeat = {true, true, false, false};
    m.branch_count = 2;
  }

  // Index 1: GOOP_MOVE — add 1 Slimed to player discard.
  // Source: LeafSlimeS.cs:32 (MoveState "GOOP_MOVE", StatusIntent(1)), :34-35.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kGoopMove;
    m.follow_up_rule = FollowUpRule::kRandomBranchCannotRepeat;
    m.effects[0] = MoveEffect{
        .value = 1,  // 1 Slimed card added
        .kind = MoveEffectKind::kAddStatusCard,
        .power_kind = PowerKind::kWeak,  // unused for kAddStatusCard
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.branch_indices = {0, 1, 0, 0};
    m.branch_weights = {1, 1, 0, 0};
    m.branch_cannot_repeat = {true, true, false, false};
    m.branch_count = 2;
  }

  t.move_count = 2;
  t.initial_move_index =
      0;          // TACKLE_MOVE (LeafSlimeS.cs:39 randomBranch start)
  t.min_hp = 11;  // LeafSlimeS.cs:20 A0 MinInitialHp
  t.max_hp = 15;  // LeafSlimeS.cs:22 A0 MaxInitialHp
  t.spawn_power_count = 0;
  return t;
}

// LeafSlimeM (wave-22.β)
// Source: LeafSlimeM.cs:22-40 (A0 baseline)
// HP 32-35. Moves: CLUMP_SHOT(0)=kAttack/8, STICKY_SHOT(1)=kAddStatusCard/2.
// kStrict strict alternation; CLUMP→STICKY→CLUMP... Initial=STICKY_SHOT.
// Source: LeafSlimeM.cs:34 (FollowUpState chains), :40 (initial=moveState2).
constexpr MonsterMoveTable make_leaf_slime_m_table() {
  MonsterMoveTable t;

  // Index 0: CLUMP_SHOT — attack 8.
  // Source: LeafSlimeM.cs:26 (ClumpDamage A0=8), :33 (MoveState "CLUMP_SHOT").
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kClumpShot;
    m.follow_up_rule = FollowUpRule::kStrict;
    m.follow_up_index = 1;  // → STICKY_SHOT (LeafSlimeM.cs:34)
    m.effects[0] = MoveEffect{
        .value = 8,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
  }

  // Index 1: STICKY_SHOT — add 2 Slimed to player discard.
  // Source: LeafSlimeM.cs:34 (MoveState "STICKY_SHOT", StatusIntent(2)), :36.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kStickyShot;
    m.follow_up_rule = FollowUpRule::kStrict;
    m.follow_up_index = 0;  // → CLUMP_SHOT (LeafSlimeM.cs:36)
    m.effects[0] = MoveEffect{
        .value = 2,  // 2 Slimed cards added
        .kind = MoveEffectKind::kAddStatusCard,
        .power_kind = PowerKind::kWeak,  // unused for kAddStatusCard
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
  }

  t.move_count = 2;
  t.initial_move_index =
      1;          // STICKY_SHOT (LeafSlimeM.cs:40: initial=moveState2)
  t.min_hp = 32;  // LeafSlimeM.cs:22 A0 MinInitialHp
  t.max_hp = 35;  // LeafSlimeM.cs:24 A0 MaxInitialHp
  t.spawn_power_count = 0;
  return t;
}

// TwigSlimeS (wave-22.β)
// Source: TwigSlimeS.cs:15-27 (A0 baseline)
// HP 7-11. Single move: TACKLE_MOVE(0)=kAttack/4, self-loop (kStrict).
// Source: TwigSlimeS.cs:26-27 (moveState.FollowUpState = moveState).
constexpr MonsterMoveTable make_twig_slime_s_table() {
  MonsterMoveTable t;

  // Index 0: TACKLE_MOVE — attack 4.
  // Source: TwigSlimeS.cs:19 (TackleDamage A0=4), :26 (MoveState
  // "TACKLE_MOVE").
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kTackleMove;
    m.follow_up_rule = FollowUpRule::kStrict;
    m.follow_up_index = 0;  // self-loop (TwigSlimeS.cs:27)
    m.effects[0] = MoveEffect{
        .value = 4,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
  }

  t.move_count = 1;
  t.initial_move_index = 0;  // TACKLE_MOVE (only move)
  t.min_hp = 7;              // TwigSlimeS.cs:15 A0 MinInitialHp
  t.max_hp = 11;             // TwigSlimeS.cs:17 A0 MaxInitialHp
  t.spawn_power_count = 0;
  return t;
}

// TwigSlimeM (wave-22.β)
// Source: TwigSlimeM.cs:23-42 (A0 baseline)
// HP 26-28. Moves: POKEY_POUNCE(0)=kAttack/11, STICKY_SHOT(1)=kAddStatusCard/1.
// kWeightedRandomCannotRepeat; weights {2,1}; only STICKY has CannotRepeat.
// Initial=STICKY_SHOT_MOVE (TwigSlimeM.cs:42: initial=moveState2).
constexpr MonsterMoveTable make_twig_slime_m_table() {
  MonsterMoveTable t;

  // Index 0: POKEY_POUNCE_MOVE — attack 11.
  // Source: TwigSlimeM.cs:27 (ClumpDamage A0=11), :34 (MoveState
  // "POKEY_POUNCE_MOVE").
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kPokeyPounce;
    m.follow_up_rule = FollowUpRule::kWeightedRandomCannotRepeat;
    m.effects[0] = MoveEffect{
        .value = 11,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.branch_indices = {0, 1, 0, 0};
    m.branch_weights = {2, 1, 0, 0};
    m.branch_cannot_repeat = {false, true, false, false};
    m.branch_count = 2;
  }

  // Index 1: STICKY_SHOT_MOVE — add 1 Slimed to player discard.
  // Source: TwigSlimeM.cs:35 (MoveState "STICKY_SHOT_MOVE", StatusIntent(1)),
  // :38.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kStickyShot;
    m.follow_up_rule = FollowUpRule::kWeightedRandomCannotRepeat;
    m.effects[0] = MoveEffect{
        .value = 1,  // 1 Slimed card added
        .kind = MoveEffectKind::kAddStatusCard,
        .power_kind = PowerKind::kWeak,  // unused for kAddStatusCard
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.branch_indices = {0, 1, 0, 0};
    m.branch_weights = {2, 1, 0, 0};
    m.branch_cannot_repeat = {false, true, false, false};
    m.branch_count = 2;
  }

  t.move_count = 2;
  t.initial_move_index =
      1;          // STICKY_SHOT_MOVE (TwigSlimeM.cs:42: initial=moveState2)
  t.min_hp = 26;  // TwigSlimeM.cs:23 A0 MinInitialHp
  t.max_hp = 28;  // TwigSlimeM.cs:25 A0 MaxInitialHp
  t.spawn_power_count = 0;
  return t;
}

}  // namespace

// kMonsterMoveTables[MonsterKind::kCultistCalcified = 0]
// kMonsterMoveTables[MonsterKind::kCultistDamp       = 1]
// kMonsterMoveTables[MonsterKind::kLouseProgenitor   = 2]
// kMonsterMoveTables[MonsterKind::kLeafSlimeS        = 3]
// kMonsterMoveTables[MonsterKind::kLeafSlimeM        = 4]
// kMonsterMoveTables[MonsterKind::kTwigSlimeS        = 5]
// kMonsterMoveTables[MonsterKind::kTwigSlimeM        = 6]
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
    // kLeafSlimeS (index 3) — wave-22.β
    make_leaf_slime_s_table(),
    // kLeafSlimeM (index 4) — wave-22.β
    make_leaf_slime_m_table(),
    // kTwigSlimeS (index 5) — wave-22.β
    make_twig_slime_s_table(),
    // kTwigSlimeM (index 6) — wave-22.β
    make_twig_slime_m_table(),
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
