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
//
// Nibbit (wave-24/K.β):
//   Source: Nibbit.cs:26-36 (A0 baseline).
//   HP 42-46 (A0). 3-move cycle: BUTT_MOVE(0) → SLICE_MOVE(1) → HISS_MOVE(2)
//   → BUTT_MOVE(0). All kStrict. Initial move depends on encounter context
//   (alone=BUTT, front=SLICE, back=HISS); factory sets appropriately.
//   BUTT_MOVE:  attack 12 (Nibbit.cs:30, A0 ButtDamage).
//   SLICE_MOVE: attack 6 + block-self 5 (Nibbit.cs:34,32, A0 values).
//   HISS_MOVE:  +2 Strength to self (Nibbit.cs:36, A0 HissStrengthGain).
//
// GremlinMerc encounter (wave-26/M.β):
//   GremlinMerc:  GremlinMerc.cs:28,30  HP 47-49. 3-cycle GIMME→DOUBLE_SMASH
//                 →HEHE; carries kSurprise(1) spawn power; OnDeath spawns
//                 SneakyGremlin + FatGremlin (deterministic median HPs
//                 12 + 15 — B1 mode).
//   SneakyGremlin: SneakyGremlin.cs:21,23  HP 10-14. 2-move SPAWNED→TACKLE
//                  self-loop. TACKLE A0 = 9 (SneakyGremlin.cs:25, 3rd arg).
//                  No spawn powers; no OnDeath.
//   FatGremlin:    FatGremlin.cs:28,30  HP 13-17. 2-move SPAWNED→FLEE
//                  self-loop. FLEE uses MoveEffectKind::kFleeSelf — removes
//                  the carrier from combat WITHOUT routing through the
//                  OnDeath helper, so no kSurprise re-trigger paths apply.
//                  No spawn powers; no OnDeath.
//
// kTackleMove reuse: SneakyGremlin's TACKLE shares MoveId::kTackleMove with
//   the wave-22 slime port. Per-monster table stores the actual damage value
//   (slime TACKLE damages 3-4; SneakyGremlin TACKLE damages 9), so reuse is
//   safe — no new MoveId required. Wire-name "TACKLE_MOVE" is already
//   mapped in move_calc.h::try_move_id_from_wire_id from wave-22 H.γ.
//
// kThievery DROPPED at data layer: GremlinMerc applies ThieveryPower(20) in
//   upstream AfterAddedToRoom (GremlinMerc.cs:54), but Q2 is a combat-only
//   oracle and does not model gold. spawn_powers includes ONLY kSurprise(1).
//   Adapter projection (M.γ) silently drops kThievery via Q2-ADR-005
//   unknown-power infrastructure.

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

// Nibbit (wave-24/K.β)
// Source: Nibbit.cs:26-36 (A0 baseline cross-checked line-by-line).
// 3-move strict cycle: BUTT_MOVE(0) → SLICE_MOVE(1) → HISS_MOVE(2) →
// BUTT_MOVE(0). initial_move_index=0 (BUTT); encounter-specific init for
// front/back overridden by factory (make_nibbit_front / make_nibbit_back
// set current_move accordingly; build_enemy_state resolves move_index via
// find_move_index).
constexpr MonsterMoveTable make_nibbit_table() {
  MonsterMoveTable t;

  // Move 0: BUTT_MOVE — single attack 12 dmg.
  // Nibbit.cs:30: ButtDamage A0 = 12 (GetValueIfAscension(DeadlyEnemies,13,12))
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kButtMove;
    m.follow_up_index = 1;  // → SLICE_MOVE
    m.effects[0] = MoveEffect{
        .value = 12,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 1: SLICE_MOVE — attack 6 + block-self 5.
  // Nibbit.cs:34: SliceDamage A0 = 6 (GetValueIfAscension(DeadlyEnemies,7,6))
  // Nibbit.cs:32: SliceBlock  A0 = 5 (GetValueIfAscension(ToughEnemies,6,5))
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kSliceMove;
    m.follow_up_index = 2;  // → HISS_MOVE
    m.effects[0] = MoveEffect{
        .value = 6,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 5,
        .kind = MoveEffectKind::kBlockSelf,
        .power_kind = PowerKind::kWeak,  // power_kind unused for kBlockSelf
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 2;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 2: HISS_MOVE — self-Strength +2 (permanent stack).
  // Nibbit.cs:36: HissStrengthGain A0 = 2
  //   (GetValueIfAscension(DeadlyEnemies,3,2))
  {
    MonsterMove& m = t.moves[2];
    m.id = MoveId::kHissMove;
    m.follow_up_index = 0;  // → BUTT_MOVE (3-cycle)
    m.effects[0] = MoveEffect{
        .value = 2,
        .kind = MoveEffectKind::kBuffEnemy,
        .power_kind = PowerKind::kStrength,
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  t.move_count = 3;
  t.initial_move_index = 0;  // BUTT_MOVE (IsAlone default)
  // Nibbit.cs:26: MinInitialHp A0 = 42
  // (GetValueIfAscension(ToughEnemies,44,42)) Nibbit.cs:28: MaxInitialHp A0 =
  // 46 (GetValueIfAscension(ToughEnemies,48,46))
  t.min_hp = 42;
  t.max_hp = 46;
  t.spawn_power_count = 0;  // Nibbit has no spawn powers
  return t;
}

// ---------------------------------------------------------------------------
// GremlinMerc encounter table builders (wave-26/M.β)
// ---------------------------------------------------------------------------

// GremlinMerc (wave-26/M.β)
// Source: GremlinMerc.cs (A0 baseline; cite-per-value).
// HP 47-49 (GremlinMerc.cs:28,30). 3-move strict cycle:
//   GIMME(0) → DOUBLE_SMASH(1) → HEHE(2) → GIMME(0).
// Initial move = GIMME (GremlinMerc.cs:70).
// Spawn powers: kSurprise(1) only (GremlinMerc.cs:49). kThievery(20) from
// GremlinMerc.cs:54 is DROPPED at the data layer (Q2 combat-only; adapter
// silent-drop via Q2-ADR-005).
// on_death_spawns: [SneakyGremlin hp=12 SPAWNED, FatGremlin hp=15 SPAWNED]
// per SurprisePower.cs:22-23 + B1 median HP convention (Sneaky median of
// [10,14]=12; Fat median of [13,17]=15).
constexpr MonsterMoveTable make_gremlin_merc_table() {
  MonsterMoveTable t;

  // Move 0: GIMME_MOVE — MultiAttackIntent(GimmeDamage=7, GimmeRepeat=2).
  // Source: GremlinMerc.cs:36 GimmeDamage A0=7
  //   (GetValueIfAscension(ToughEnemies,8,7)); :38 GimmeRepeat=2; :61
  //   MoveState("GIMME_MOVE", ...). Modeled as 2 sequential kAttack effects
  //   so block + Strength applies per-hit (matches multi-hit damage test
  //   precedent set in M.α: each effect goes through one
  //   apply_damage_to_enemy chain).
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kGimmeMove;
    m.follow_up_index = 1;  // → DOUBLE_SMASH_MOVE (GremlinMerc.cs:64)
    m.effects[0] = MoveEffect{
        .value = 7,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 7,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 2;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 1: DOUBLE_SMASH_MOVE — MultiAttackIntent(DoubleSmashDamage=6,
  //   DoubleSmashRepeat=2) + DebuffIntent (WeakPower 2 to player).
  // Source: GremlinMerc.cs:40 DoubleSmashDamage A0=6
  //   (GetValueIfAscension(ToughEnemies,7,6)); :42 DoubleSmashRepeat=2;
  //   :62 MoveState("DOUBLE_SMASH_MOVE", ..., DebuffIntent());
  //   :109 PowerCmd.Apply<WeakPower>(... 2m ...).
  // Order MATTERS — both attacks BEFORE the Weak debuff per the upstream
  // Task body sequence (damage applied first, then Weak applied at
  // GremlinMerc.cs:109). Q2's kAttack effect chain applies damage in the
  // listed order; kDebuffPlayer applies after both attacks resolve.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kDoubleSmashMove;
    m.follow_up_index = 2;  // → HEHE_MOVE (GremlinMerc.cs:65)
    m.effects[0] = MoveEffect{
        .value = 6,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 6,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[2] = MoveEffect{
        .value = 2,  // 2 Weak stacks (GremlinMerc.cs:109)
        .kind = MoveEffectKind::kDebuffPlayer,
        .power_kind = PowerKind::kWeak,
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 3;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 2: HEHE_MOVE — SingleAttackIntent(HeheDamage=8) + BuffIntent
  //   (StrengthPower +2 to self).
  // Source: GremlinMerc.cs:44 HeheDamage A0=8
  //   (GetValueIfAscension(ToughEnemies,9,8)); :63 MoveState("HEHE_MOVE",
  //   SingleAttackIntent, BuffIntent); :122 PowerCmd.Apply<StrengthPower>
  //   (... 2m ...).
  // Order MATTERS — kAttack BEFORE kBuffEnemy(kStrength) so this turn's
  // attack uses the PRE-buff Strength (matches the upstream Task body
  // sequence: GremlinMerc.cs:114 attack runs at line 114-117; Strength is
  // applied at :122 AFTER the attack resolves). Verified by M.α's
  // HeheAttack_UsesPreBuffStrength transition test.
  {
    MonsterMove& m = t.moves[2];
    m.id = MoveId::kHeheMove;
    m.follow_up_index = 0;  // → GIMME_MOVE (GremlinMerc.cs:66)
    m.effects[0] = MoveEffect{
        .value = 8,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effects[1] = MoveEffect{
        .value = 2,  // +2 Strength (GremlinMerc.cs:122)
        .kind = MoveEffectKind::kBuffEnemy,
        .power_kind = PowerKind::kStrength,
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 2;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  t.move_count = 3;
  t.initial_move_index = 0;  // GIMME first (GremlinMerc.cs:70)
  t.min_hp = 47;             // GremlinMerc.cs:28 MinInitialHp A0
  t.max_hp = 49;             // GremlinMerc.cs:30 MaxInitialHp A0

  // Spawn power: kSurprise(1). Upstream AfterAddedToRoom applies
  // SurprisePower(1m) (GremlinMerc.cs:49). kThievery(20) from
  // GremlinMerc.cs:54 is DROPPED at data layer (Q2 combat-only).
  t.spawn_powers[0] = SpawnPowerEntry{
      .stacks = 1,
      .kind = PowerKind::kSurprise,
  };
  t.spawn_power_count = 1;

  // OnDeath spawns: 2 entries per SurprisePower.cs:22-23
  //   ([0] SneakyGremlin "sneaky"; [1] FatGremlin "fat"). B1 median HP:
  //   Sneaky=12 (median of [10,14]); Fat=15 (median of [13,17]).
  // Each spawn starts on kSpawnedMove (effect_count=0 no-op) and rolls into
  // its real first move on the next enemy turn.
  t.on_death_spawns[0] = SpawnEntry{
      .deterministic_hp = 12,  // B1 median of [10,14] (SneakyGremlin.cs:21,23)
      .initial_current_move = MoveId::kSpawnedMove,
      .kind = MonsterKind::kSneakyGremlin,
      ._pad = 0,
      ._pad2 = 0,
      ._pad3 = 0,
  };
  t.on_death_spawns[1] = SpawnEntry{
      .deterministic_hp = 15,  // B1 median of [13,17] (FatGremlin.cs:28,30)
      .initial_current_move = MoveId::kSpawnedMove,
      .kind = MonsterKind::kFatGremlin,
      ._pad = 0,
      ._pad2 = 0,
      ._pad3 = 0,
  };
  t.on_death_spawn_count = 2;

  return t;
}

// SneakyGremlin (wave-26/M.β)
// Source: SneakyGremlin.cs (A0 baseline; cite-per-value).
// HP 10-14 (SneakyGremlin.cs:21,23). 2-move rotation:
//   SPAWNED(0) → TACKLE(1) → TACKLE (self-loop).
// Initial move = SPAWNED (SneakyGremlin.cs:54). TACKLE A0 damage = 9
//   (SneakyGremlin.cs:25, 3rd arg of GetValueIfAscension(DeadlyEnemies,
//   10, 9)). MoveId::kTackleMove REUSED from wave-22 slime port — slime
//   TACKLE damages 3-4 in their tables; per-monster damage value lives in
//   this table so the reuse is safe.
constexpr MonsterMoveTable make_sneaky_gremlin_table() {
  MonsterMoveTable t;

  // Move 0: SPAWNED_MOVE — StunIntent, no damage.
  // Source: SneakyGremlin.cs:49 MoveState("SPAWNED_MOVE", SpawnedMove,
  //   StunIntent()). effect_count=0 — the spawn-turn stun has no oracle
  //   semantics (no damage; no card injection); SneakyGremlin's stun
  //   semantic is purely an animation gate upstream.
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kSpawnedMove;
    m.follow_up_index = 1;  // → TACKLE_MOVE (SneakyGremlin.cs:50)
    m.effect_count = 0;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 1: TACKLE_MOVE — SingleAttackIntent(TackleDamage=9).
  // Source: SneakyGremlin.cs:25 TackleDamage A0=9
  //   (GetValueIfAscension(DeadlyEnemies, 10, 9) — 3rd arg is A0); :50
  //   MoveState("TACKLE_MOVE", ..., SingleAttackIntent(TackleDamage));
  //   :51 moveState2.FollowUpState = moveState2 (self-loop).
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kTackleMove;
    m.follow_up_index = 1;  // self-loop (SneakyGremlin.cs:51)
    m.effects[0] = MoveEffect{
        .value = 9,
        .kind = MoveEffectKind::kAttack,
        .power_kind = PowerKind::kWeak,  // unused for kAttack
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  t.move_count = 2;
  t.initial_move_index = 0;  // SPAWNED first (SneakyGremlin.cs:54)
  t.min_hp = 10;             // SneakyGremlin.cs:21 MinInitialHp A0
  t.max_hp = 14;             // SneakyGremlin.cs:23 MaxInitialHp A0
  t.spawn_power_count = 0;
  t.on_death_spawn_count = 0;

  return t;
}

// FatGremlin (wave-26/M.β)
// Source: FatGremlin.cs (A0 baseline; cite-per-value).
// HP 13-17 (FatGremlin.cs:28,30). 2-move rotation:
//   SPAWNED(0) → FLEE(1) → FLEE (self-loop).
// Initial move = SPAWNED (FatGremlin.cs:57). FLEE uses kFleeSelf
//   (MoveEffectKind::kFleeSelf — M.α schema): removes the carrier from
//   combat WITHOUT routing through the OnDeath helper, so kSurprise
//   re-trigger paths don't fire (FatGremlin carries no kSurprise anyway,
//   but the discipline keeps semantics clean).
constexpr MonsterMoveTable make_fat_gremlin_table() {
  MonsterMoveTable t;

  // Move 0: SPAWNED_MOVE — StunIntent, no damage.
  // Source: FatGremlin.cs:52 MoveState("SPAWNED_MOVE", SpawnedMove,
  //   StunIntent()). effect_count=0 — same as SneakyGremlin's SPAWNED.
  {
    MonsterMove& m = t.moves[0];
    m.id = MoveId::kSpawnedMove;
    m.follow_up_index = 1;  // → FLEE_MOVE (FatGremlin.cs:53)
    m.effect_count = 0;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  // Move 1: FLEE_MOVE — EscapeIntent. Removes carrier from combat.
  // Source: FatGremlin.cs:53 MoveState("FLEE_MOVE", FleeMove,
  //   EscapeIntent()); :54 moveState2.FollowUpState = moveState2
  //   (self-loop — but semantically the first invocation Escapes the
  //   creature so this is a single-fire); :75 CreatureCmd.Escape(...)
  //   removes the creature from the combat. Dispatch in transition.cc
  //   case kFleeSelf sets M::alive(e) = false directly.
  {
    MonsterMove& m = t.moves[1];
    m.id = MoveId::kFleeMove;
    m.follow_up_index = 1;  // self-loop (FatGremlin.cs:54); fires once
                            // before kFleeSelf clears alive
    m.effects[0] = MoveEffect{
        .value = 0,  // unused for kFleeSelf
        .kind = MoveEffectKind::kFleeSelf,
        .power_kind = PowerKind::kWeak,  // unused for kFleeSelf
        ._pad = 0,
        ._pad2 = 0,
    };
    m.effect_count = 1;
    m.follow_up_rule = FollowUpRule::kStrict;
  }

  t.move_count = 2;
  t.initial_move_index = 0;  // SPAWNED first (FatGremlin.cs:57)
  t.min_hp = 13;             // FatGremlin.cs:28 MinInitialHp A0
  t.max_hp = 17;             // FatGremlin.cs:30 MaxInitialHp A0
  t.spawn_power_count = 0;
  t.on_death_spawn_count = 0;

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
// kMonsterMoveTables[MonsterKind::kNibbit            = 7]   (wave-24/K.β)
// kMonsterMoveTables[MonsterKind::kGremlinMerc       = 8]   (wave-26/M.β)
// kMonsterMoveTables[MonsterKind::kSneakyGremlin     = 9]   (wave-26/M.β)
// kMonsterMoveTables[MonsterKind::kFatGremlin        = 10]  (wave-26/M.β)
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
    // kNibbit (index 7) — wave-24/K.β
    make_nibbit_table(),
    // kGremlinMerc (index 8) — wave-26/M.β
    make_gremlin_merc_table(),
    // kSneakyGremlin (index 9) — wave-26/M.β
    make_sneaky_gremlin_table(),
    // kFatGremlin (index 10) — wave-26/M.β
    make_fat_gremlin_table(),
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
