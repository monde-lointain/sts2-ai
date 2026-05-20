#include "sts2/game/enemies.h"

#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/move_effect_dispatch.h"
#include "sts2/game/powers.h"
#include "sts2/game/rng.h"

namespace sts2::enemies {

namespace {

sts2::game::Enemy make_cultist(const CultistArchetype& archetype,
                               sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = archetype.internal_name;
  int hp = rng.uniform_int(archetype.hp_min, archetype.hp_max);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.dark_strike_base = sts2::game::Stat{archetype.dark_strike_base};
  e.ritual_amount = sts2::game::Stat{archetype.ritual_amount};
  return e;
}

}  // namespace

sts2::game::Enemy make_calcified_cultist(sts2::game::Rng& rng) {
  auto e = make_cultist(kCultistArchetypes[0], rng);
  e.kind = sts2::game::MonsterKind::kCultistCalcified;
  return e;
}

sts2::game::Enemy make_damp_cultist(sts2::game::Rng& rng) {
  auto e = make_cultist(kCultistArchetypes[1], rng);
  e.kind = sts2::game::MonsterKind::kCultistDamp;
  return e;
}

// LouseProgenitor factory — A0 baseline per upstream
// Models/Monsters/LouseProgenitor.cs:38-40,67-71. HP rolled from the
// monster_moves table (134-136); CurlUp(14) spawn power applied per upstream
// AfterAddedToRoom (LouseProgenitor.cs:67-71); initial move WEB_CANNON.
sts2::game::Enemy make_louse_progenitor(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  const auto& table =
      sts2::game::monster_moves::kMonsterMoveTables[static_cast<std::size_t>(
          sts2::game::MonsterKind::kLouseProgenitor)];
  e.name = "Louse Progenitor";
  e.kind = sts2::game::MonsterKind::kLouseProgenitor;
  const int hp = rng.uniform_int(table.min_hp, table.max_hp);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kWebCannon;
  e.vitals.powers.push_back(
      {sts2::game::PowerKind::kCurlUp, 14, /*just_applied=*/false});
  return e;
}

// Wave-22.β: slime factories — HP roll + initial move per upstream.
// Upstream A0 HP ranges per Models/Monsters/{LeafSlimeS,LeafSlimeM,
// TwigSlimeS,TwigSlimeM}.cs MinInitialHp/MaxInitialHp.
// Initial moves match the MonsterMoveStateMachine start state in each .cs.

// LeafSlimeS: HP 11-15 (A0). Source: LeafSlimeS.cs:20,22.
// Initial move: TACKLE_MOVE (kTackleMove). Source: LeafSlimeS.cs:39
// (randomBranchState start; TACKLE is list index 0).
sts2::game::Enemy make_leaf_slime_s(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Leaf Slime (S)";
  e.kind = sts2::game::MonsterKind::kLeafSlimeS;
  const int hp = rng.uniform_int(11, 15);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kTackleMove;
  e.performed_first_move = false;
  return e;
}

// LeafSlimeM: HP 32-35 (A0). Source: LeafSlimeM.cs:22,24.
// Initial move: STICKY_SHOT (kStickyShot). Source: LeafSlimeM.cs:40
// (MonsterMoveStateMachine initial=moveState2 = STICKY_SHOT).
sts2::game::Enemy make_leaf_slime_m(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Leaf Slime (M)";
  e.kind = sts2::game::MonsterKind::kLeafSlimeM;
  const int hp = rng.uniform_int(32, 35);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kStickyShot;
  e.performed_first_move = false;
  return e;
}

// TwigSlimeS: HP 7-11 (A0). Source: TwigSlimeS.cs:15,17.
// Initial move: TACKLE_MOVE (kTackleMove). Source: TwigSlimeS.cs:26
// (single move, self-loop).
sts2::game::Enemy make_twig_slime_s(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Twig Slime (S)";
  e.kind = sts2::game::MonsterKind::kTwigSlimeS;
  const int hp = rng.uniform_int(7, 11);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kTackleMove;
  e.performed_first_move = false;
  return e;
}

// TwigSlimeM: HP 26-28 (A0). Source: TwigSlimeM.cs:23,25.
// Initial move: STICKY_SHOT_MOVE (kStickyShot). Source: TwigSlimeM.cs:42
// (MonsterMoveStateMachine initial=moveState2 = STICKY_SHOT_MOVE).
sts2::game::Enemy make_twig_slime_m(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Twig Slime (M)";
  e.kind = sts2::game::MonsterKind::kTwigSlimeM;
  const int hp = rng.uniform_int(26, 28);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kStickyShot;
  e.performed_first_move = false;
  return e;
}

// ---------------------------------------------------------------------------
// Wave-24/K.β: Nibbit factories
// ---------------------------------------------------------------------------
// Source: Nibbit.cs:26,28 (A0 HP range 42-46).
// Three variants per ConditionalBranchState (Nibbit.cs:74-88):
//   alone  → init BUTT_MOVE  (IsAlone=true, Nibbit.cs:76-77)
//   front  → init SLICE_MOVE (IsFront=true, Nibbit.cs:82)
//   back   → init HISS_MOVE  (IsFront=false, Nibbit.cs:81)
// move_index consistency: build_enemy_state resolves via find_move_index;
//   kButtMove→0, kSliceMove→1, kHissMove→2 per make_nibbit_table().

// make_nibbit_alone — NibbitsWeak encounter (IsAlone=true); starts BUTT_MOVE.
// Nibbit.cs:26: MinInitialHp A0=42; :28: MaxInitialHp A0=46.
sts2::game::Enemy make_nibbit_alone(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Nibbit";
  e.kind = sts2::game::MonsterKind::kNibbit;
  const int hp = rng.uniform_int(42, 46);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kButtMove;  // Nibbit.cs:76-77 IsAlone
  e.performed_first_move = false;
  return e;
}

// make_nibbit_front — multi-Nibbit encounter front position; starts SLICE_MOVE.
// Nibbit.cs:82: IsFront=true branch → moveState2 (SLICE_MOVE).
sts2::game::Enemy make_nibbit_front(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Nibbit";
  e.kind = sts2::game::MonsterKind::kNibbit;
  const int hp = rng.uniform_int(42, 46);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kSliceMove;  // Nibbit.cs:82 IsFront
  e.performed_first_move = false;
  return e;
}

// make_nibbit_back — multi-Nibbit encounter back position; starts HISS_MOVE.
// Nibbit.cs:81: IsFront=false branch → moveState3 (HISS_MOVE).
sts2::game::Enemy make_nibbit_back(sts2::game::Rng& rng) {
  sts2::game::Enemy e;
  e.name = "Nibbit";
  e.kind = sts2::game::MonsterKind::kNibbit;
  const int hp = rng.uniform_int(42, 46);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kHissMove;  // Nibbit.cs:81 IsFront=false
  e.performed_first_move = false;
  return e;
}

namespace {

bool is_cultist_kind(sts2::game::MonsterKind k) noexcept {
  return k == sts2::game::MonsterKind::kCultistCalcified ||
         k == sts2::game::MonsterKind::kCultistDamp;
}

struct ProductionTarget {
  sts2::game::Combat& combat;
  sts2::game::Enemy& e;

  bool attack_player(int32_t base) noexcept {
    combat.enemy_attack_player(e, base);
    return !combat.combat_over();
  }
  void gain_self_block(int32_t base) noexcept {
    e.vitals.block +=
        sts2::damage::compute_outgoing_block(base, 0, false, true);
  }
  void add_self_power(sts2::game::PowerKind kind, int32_t v) noexcept {
    sts2::powers::apply(e.vitals.powers, kind, v);
  }
  // Phase-1 production-unsupported (slime status injection + slime status cards
  // aren't modelled in the production Combat path; preserve today's
  // silent-noop).
  void add_player_frail(int32_t) noexcept { /* unsupported */ }
  void add_player_weak(int32_t) noexcept { /* unsupported */ }
  void add_player_vulnerable(int32_t) noexcept { /* unsupported */ }
  void add_player_discard_slimed(int32_t) noexcept { /* unsupported */ }
  void unsupported(
      sts2::game::MoveEffectKind) noexcept { /* mirrors kNone noop */ }
};

}  // namespace

void roll_next_move(sts2::game::Enemy& e) {
  if (is_cultist_kind(e.kind)) {
    sts2::game::move_calc::advance_intent(e.performed_first_move,
                                          e.current_move);
    return;
  }
  const auto kind_idx = static_cast<std::size_t>(e.kind);
  if (kind_idx >= sts2::game::monster_moves::kMonsterMoveTables.size()) {
    return;
  }
  const auto& table = sts2::game::monster_moves::kMonsterMoveTables[kind_idx];
  uint8_t move_index =
      sts2::game::monster_moves::find_move_index(e.kind, e.current_move);
  sts2::game::move_calc::advance_intent_table(
      e.performed_first_move, e.current_move, move_index, table);
}

void act(sts2::game::Enemy& e, sts2::game::Combat& combat) {
  if (is_cultist_kind(e.kind)) {
    sts2::game::move_calc::act_on_intent(
        e.current_move,
        [&]() {
          sts2::powers::apply(e.vitals.powers, sts2::game::PowerKind::kRitual,
                              e.ritual_amount.value());
        },
        [&]() { combat.enemy_attack_player(e, e.dark_strike_base.value()); });
    return;
  }
  // Table-driven dispatch via shared move_effect_dispatch.h.
  const auto kind_idx = static_cast<std::size_t>(e.kind);
  if (kind_idx >= sts2::game::monster_moves::kMonsterMoveTables.size()) {
    return;
  }
  const auto& table = sts2::game::monster_moves::kMonsterMoveTables[kind_idx];
  const uint8_t move_index =
      sts2::game::monster_moves::find_move_index(e.kind, e.current_move);
  if (move_index >= table.move_count) {
    return;
  }
  const auto& move = table.moves[move_index];
  ProductionTarget target{combat, e};
  for (uint8_t i = 0; i < move.effect_count; ++i) {
    sts2::game::apply_move_effect(move.effects[i], target);
    if (combat.combat_over()) {
      return;  // matches today's enemies.cc short-circuit
    }
  }
}

}  // namespace sts2::enemies
