#pragma once

#include <algorithm>
#include <array>
#include <string_view>

#include "sts2/game/enemy.h"

namespace sts2::game {
class Combat;
class Rng;
}  // namespace sts2::game

namespace sts2::enemies {

struct CultistArchetype {
  std::string_view internal_name;
  std::string_view wire_name;
  int hp_min;
  int hp_max;
  int dark_strike_base;
  int ritual_amount;
};

inline constexpr std::array<CultistArchetype, 2> kCultistArchetypes = {{
    {.internal_name = "Calcified Cultist",
     .wire_name = "CalcifiedCultist",
     .hp_min = 38,
     .hp_max = 41,
     .dark_strike_base = 9,
     .ritual_amount = 2},
    {.internal_name = "Damp Cultist",
     .wire_name = "DampCultist",
     .hp_min = 51,
     .hp_max = 53,
     .dark_strike_base = 1,
     .ritual_amount = 5},
}};

[[nodiscard]] constexpr const CultistArchetype*
cultist_archetype_from_wire_name(std::string_view wire_name) noexcept {
  const auto* const it =
      std::find_if(kCultistArchetypes.begin(), kCultistArchetypes.end(),
                   [wire_name](const CultistArchetype& a) {
                     return a.wire_name == wire_name;
                   });
  return (it != kCultistArchetypes.end()) ? it : nullptr;
}

[[nodiscard]] constexpr const CultistArchetype*
cultist_archetype_from_internal_name(std::string_view internal_name) noexcept {
  const auto* const it =
      std::find_if(kCultistArchetypes.begin(), kCultistArchetypes.end(),
                   [internal_name](const CultistArchetype& a) {
                     return a.internal_name == internal_name;
                   });
  return (it != kCultistArchetypes.end()) ? it : nullptr;
}

game::Enemy make_calcified_cultist(game::Rng& rng);
game::Enemy make_damp_cultist(game::Rng& rng);

// LouseProgenitor factory (LouseProgenitor.cs:38-40, A0 baseline).
// HP 134-136, applies CurlUp(14) spawn power per upstream AfterAddedToRoom,
// starts on WEB_CANNON. Used by the scenario loader for the
// LouseProgenitorNormal encounter (no direct C# factory upstream — encounter
// instantiation goes through the adapter on the wire-blob path; the scenario
// loader needs a name-buildable path).
game::Enemy make_louse_progenitor(game::Rng& rng);

// Wave-21: slime factory stubs (HP only; move-table data deferred to
// wave-22.β). HP ranges: A0 baselines per upstream
// Models/Monsters/{LeafSlimeS,LeafSlimeM,
//            TwigSlimeS,TwigSlimeM}.cs MinInitialHp/MaxInitialHp.
game::Enemy make_leaf_slime_s(game::Rng& rng);  // HP 11-15 (A0)
game::Enemy make_leaf_slime_m(game::Rng& rng);  // HP 32-35 (A0)
game::Enemy make_twig_slime_s(game::Rng& rng);  // HP 7-11  (A0)
game::Enemy make_twig_slime_m(game::Rng& rng);  // HP 26-28 (A0)

// Wave-24/K.β: Nibbit factories (Nibbit.cs:26-36, A0 baseline).
// Three variants per encounter context (Nibbit.cs:74-88
// ConditionalBranchState):
//   alone  → starts BUTT_MOVE  (IsAlone=true)
//   front  → starts SLICE_MOVE (IsFront=true)
//   back   → starts HISS_MOVE  (IsFront=false, IsAlone=false)
game::Enemy make_nibbit_alone(game::Rng& rng);  // HP 42-46 (A0); init BUTT_MOVE
game::Enemy make_nibbit_front(
    game::Rng& rng);                           // HP 42-46 (A0); init SLICE_MOVE
game::Enemy make_nibbit_back(game::Rng& rng);  // HP 42-46 (A0); init HISS_MOVE

// Wave-26/M.β: GremlinMerc encounter factories (A0 baseline; A0 = 3rd arg of
// AscensionHelper.GetValueIfAscension upstream).
//   GremlinMerc:   HP rolled in [47, 49] (GremlinMerc.cs:28,30); starts on
//                  GIMME_MOVE (GremlinMerc.cs:70); carries kSurprise(1)
//                  spawn power (GremlinMerc.cs:49). kThievery(20) at
//                  GremlinMerc.cs:54 is DROPPED — Q2 combat-only oracle.
//   SneakyGremlin: HP defaults to deterministic median 12 (median of
//                  [10,14] per SneakyGremlin.cs:21,23); spawn entry point;
//                  starts on SPAWNED_MOVE (SneakyGremlin.cs:54).
//                  hp_override = 12 default routes through the B1 path.
//   FatGremlin:    HP defaults to deterministic median 15 (median of
//                  [13,17] per FatGremlin.cs:28,30); spawn entry point;
//                  starts on SPAWNED_MOVE (FatGremlin.cs:57).
//                  hp_override = 15 default routes through the B1 path.
//
// SneakyGremlin + FatGremlin retain the Rng& parameter for API parity with
// other factories (cultist + slime + Nibbit); the Rng is unused along the
// hp_override path. Caller may pass a non-override variant in future waves
// if Q1 next_spawn_hps metadata becomes available (Q2-ADR-029 §Path A).
game::Enemy make_gremlin_merc(game::Rng& rng);  // HP 47-49 (A0); init GIMME
game::Enemy make_sneaky_gremlin(
    game::Rng& rng,
    int32_t hp_override = 12);  // HP override default = B1 median
game::Enemy make_fat_gremlin(
    game::Rng& rng,
    int32_t hp_override = 15);  // HP override default = B1 median

void roll_next_move(game::Enemy& e);
void act(game::Enemy& e, game::Combat& combat);

}  // namespace sts2::enemies
