#pragma once

#include <string_view>
#include <vector>

namespace sts2::game {
class Combat;
class Rng;
}  // namespace sts2::game

namespace sts2::game::encounter_registry {

// Constructs the encounter's monsters in `combat`. May be nullptr for
// adapter-detection-only encounters that cannot be built from a name alone
// (e.g., wire-blob-dependent bosses).
using SpawnFn = void (*)(sts2::game::Combat& combat, sts2::game::Rng& rng);

struct EncounterSpec {
  // Canonical upstream Godot encounter name, e.g., "CultistsNormal".
  std::string_view encounter_id;
  // Wire-format monster ids in alphabetical order (matches the order produced
  // by `sorted_monster_ids` in the adapter).
  std::vector<std::string_view> sorted_monster_ids;
  // Scenario-loader spawn dispatch. Nullptr ⇒ this encounter is recognized
  // (for adapter diagnostic / reject path) but cannot be name-constructed.
  SpawnFn spawn;
  // Adapter's detect_encounter_id includes this entry iff true. Some entries
  // are intentionally excluded:
  //   CultistsNormal — happy-path branch in the adapter, not a reject
  //   diagnostic. NibbitsNormal  — wave-27/N.alpha removal per Q2-ADR-017;
  //   reject route.
  bool in_adapter_map;
};

// All registered encounters. Stable iteration order = insertion order.
const std::vector<EncounterSpec>& all();

// Lookup by canonical encounter_id (used by scenario loader). Ignores
// `in_adapter_map`. O(N) linear scan; registry is ~10s of entries.
[[nodiscard]] const EncounterSpec* find_by_id(std::string_view id) noexcept;

// Lookup by sorted wire-format monster ids (used by adapter's
// detect_encounter_id). Filters on `in_adapter_map == true`.
[[nodiscard]] const EncounterSpec* find_by_monsters(
    const std::vector<std::string_view>& sorted_monster_ids) noexcept;

}  // namespace sts2::game::encounter_registry
