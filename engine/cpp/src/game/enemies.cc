#include "sts2/game/enemies.h"

#include "sts2/game/combat.h"
#include "sts2/game/move_calc.h"
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
  return make_cultist(kCultistArchetypes[0], rng);
}

sts2::game::Enemy make_damp_cultist(sts2::game::Rng& rng) {
  return make_cultist(kCultistArchetypes[1], rng);
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
  const int hp = rng.uniform_int(26, 28);
  e.vitals.max_hp = sts2::game::Stat{hp};
  e.vitals.hp = sts2::game::Stat{hp};
  e.current_move = sts2::game::MoveId::kStickyShot;
  e.performed_first_move = false;
  return e;
}

void roll_next_move(sts2::game::Enemy& e) {
  sts2::game::move_calc::advance_intent(e.performed_first_move, e.current_move);
}

void act(sts2::game::Enemy& e, sts2::game::Combat& combat) {
  sts2::game::move_calc::act_on_intent(
      e.current_move,
      [&]() {
        sts2::powers::apply(e.vitals.powers, sts2::game::PowerKind::kRitual,
                            e.ritual_amount.value());
      },
      [&]() { combat.enemy_attack_player(e, e.dark_strike_base.value()); });
}

}  // namespace sts2::enemies
