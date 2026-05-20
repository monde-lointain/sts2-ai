#include "sts2/game/encounter_registry.h"

#include <algorithm>
#include <iterator>

#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/rng.h"

namespace sts2::game::encounter_registry {

namespace {

void spawn_cultists_normal(sts2::game::Combat& c, sts2::game::Rng& rng) {
  c.add_enemy(sts2::enemies::make_calcified_cultist(rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(rng));
}
void spawn_louse_progenitor_normal(sts2::game::Combat& c,
                                   sts2::game::Rng& rng) {
  c.add_enemy(sts2::enemies::make_louse_progenitor(rng));
}
void spawn_nibbits_weak(sts2::game::Combat& c, sts2::game::Rng& rng) {
  c.add_enemy(sts2::enemies::make_nibbit_alone(rng));
}
void spawn_nibbits_normal(sts2::game::Combat& c, sts2::game::Rng& rng) {
  c.add_enemy(sts2::enemies::make_nibbit_front(rng));
  c.add_enemy(sts2::enemies::make_nibbit_back(rng));
}

}  // namespace

const std::vector<EncounterSpec>& all() {
  static const std::vector<EncounterSpec> kRegistry = {
      // Cultists' base encounter — simulator-buildable. Intentionally NOT in
      // the adapter's detection map: cultist projection is the adapter's
      // happy-path branch (cultists_projection.cc), not the reject diagnostic.
      {"CultistsNormal",
       {"CalcifiedCultist", "DampCultist"},
       &spawn_cultists_normal,
       /*in_adapter_map=*/false},
      // Adapter-only diagnostic entries (spawn == nullptr — wire-blob
      // required).
      {"FossilStalkerElite",
       {"FossilStalker"},
       nullptr,
       /*in_adapter_map=*/true},
      {"KaiserCrabBoss",
       {"Crusher", "Rocket"},
       nullptr,
       /*in_adapter_map=*/true},
      // Adapter-supported + scenario-buildable.
      {"LouseProgenitorNormal",
       {"LouseProgenitor"},
       &spawn_louse_progenitor_normal,
       /*in_adapter_map=*/true},
      {"NibbitsWeak",
       {"Nibbit"},
       &spawn_nibbits_weak,
       /*in_adapter_map=*/true},
      // Scenario-buildable only — adapter dropped in wave-27/N.alpha per
      // Q2-ADR-017. Substrate retained for debug use via scenario loader.
      {"NibbitsNormal",
       {"Nibbit", "Nibbit"},
       &spawn_nibbits_normal,
       /*in_adapter_map=*/false},
  };
  return kRegistry;
}

const EncounterSpec* find_by_id(std::string_view id) noexcept {
  const auto& registry = all();
  auto it = std::find_if(
      registry.begin(), registry.end(),
      [id](const EncounterSpec& e) { return e.encounter_id == id; });
  return (it != registry.end()) ? &*it : nullptr;
}

const EncounterSpec* find_by_monsters(
    const std::vector<std::string_view>& sorted_monster_ids) noexcept {
  for (const auto& e : all()) {
    if (!e.in_adapter_map) {
      continue;
    }
    if (e.sorted_monster_ids == sorted_monster_ids) {
      return &e;
    }
  }
  return nullptr;
}

}  // namespace sts2::game::encounter_registry
