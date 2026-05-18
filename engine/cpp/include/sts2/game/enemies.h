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

void roll_next_move(game::Enemy& e);
void act(game::Enemy& e, game::Combat& combat);

}  // namespace sts2::enemies
