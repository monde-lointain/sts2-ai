#include "sts2/oracle/adapter/adapter.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <iterator>
#include <optional>
#include <set>
#include <span>
#include <string>
#include <string_view>
#include <vector>

#include "sha256_internal.h"
#include "sts2/oracle/adapter/cultists_projection.h"
#include "sts2/oracle/adapter/diagnostic.h"
#include "sts2/oracle/adapter/louse_progenitor_projection.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/adapter/state_blob.h"

// Adapter facade: encounter detection + reject-with-diagnostic path
// (Q2-ADR-002) + unknown-power diagnostic (Q2-ADR-005). The encounter-id
// detection map and the spawn-power expectation map are static constexpr
// data; both grow as Phase-1.5+ encounters land.

namespace sts2::oracle::adapter {

namespace {

// Encounter-id detection. Maps the SORTED set of wire-emitted monster
// Creature.Name strings to the encounter id used by Q1-side recipes /
// downstream consumers. Phase-1A: 6 entries pinned against the D3 fixture
// corpus. CULTISTS_NORMAL is intentionally NOT in this map — it's the
// happy-path branch, not the reject branch.
struct EncounterEntry {
  // Sorted lower-case-insensitive set; we store sorted ASCII as the source
  // strings happen to be CamelCase distinct.
  std::vector<std::string_view> sorted_monster_ids;
  std::string_view encounter_id;
};

const std::vector<EncounterEntry>& encounter_map() {
  static const std::vector<EncounterEntry> kMap = {
      // FossilStalkerElite — single-monster.
      {{"FossilStalker"}, "FossilStalkerElite"},
      // KaiserCrabBoss — Crusher + Rocket spawn pair.
      {{"Crusher", "Rocket"}, "KaiserCrabBoss"},
      // LouseProgenitorNormal — single-monster.
      {{"LouseProgenitor"}, "LouseProgenitorNormal"},
      // SmallSlimes — AcidSlimeS + SpikeSlimeS pair.
      {{"AcidSlimeS", "SpikeSlimeS"}, "SmallSlimes"},
  };
  return kMap;
}

// Spawn-power expectation map (Q2-ADR-005). Maps encounter_id -> the set
// of source-declared PowerInstance.ModelId strings that Q1's content
// classes register on this encounter's spawns. Used to detect Q1's
// silent-fail-soft on KaiserCrabBoss spawn powers (per the fixture #4
// README header note) and LouseProgenitorNormal's CurlUp spawn power.
//
// LouseProgenitorNormal: upstream AfterAddedToRoom applies CurlUp(14) at
// A0 (Phase1Monsters.cs spawnPowers). Q1 may silent-drop this at boot;
// the projection synthesizes it if absent (Q2-ADR-005 pattern).
const std::vector<std::string>& spawn_power_expectation_for(
    std::string_view encounter_id) {
  static const std::vector<std::string> kEmpty;
  static const std::vector<std::string> kKaiserCrabBoss = {
      "BackAttackLeftPower",
      "BackAttackRightPower",
      "CrabRagePower",
      "SurroundedPower",
  };
  static const std::vector<std::string> kLouseProgenitorNormal = {
      "CurlUp",
  };
  if (encounter_id == "KaiserCrabBoss") {
    return kKaiserCrabBoss;
  }
  if (encounter_id == "LouseProgenitorNormal") {
    return kLouseProgenitorNormal;
  }
  return kEmpty;
}

std::vector<std::string> sorted_monster_ids(const ParsedCombatState& combat) {
  std::vector<std::string> ids;
  ids.reserve(combat.enemies.size());
  std::transform(combat.enemies.begin(), combat.enemies.end(),
                 std::back_inserter(ids),
                 [](const ParsedCreature& e) { return e.name; });
  std::sort(ids.begin(), ids.end());
  return ids;
}

std::string detect_encounter_id(
    const std::vector<std::string>& sorted_wire_names) {
  for (const auto& entry : encounter_map()) {
    if (entry.sorted_monster_ids.size() != sorted_wire_names.size()) {
      continue;
    }
    bool match = true;
    for (std::size_t i = 0; i < sorted_wire_names.size(); ++i) {
      if (sorted_wire_names[i] != entry.sorted_monster_ids[i]) {
        match = false;
        break;
      }
    }
    if (match) {
      return std::string(entry.encounter_id);
    }
  }
  return "<unknown>";
}

// Collect all PowerInstance.ModelId strings from a CombatState's enemies.
std::set<std::string> collect_enemy_power_ids(const ParsedCombatState& combat) {
  std::set<std::string> ids;
  for (const auto& e : combat.enemies) {
    for (const auto& p : e.powers) {
      ids.insert(p.model_id);
    }
  }
  return ids;
}

}  // namespace

AdapterResult from_blob_payload(std::span<const std::uint8_t> m1_payload) {
  const ParsedStateBlob blob = read_state_blob(m1_payload);
  // blob_canonical_hash mirrors Q1's CanonicalHash.Sha256Hex per
  // state-codec.md §Data Ownership: SHA-256 over the FULL blob bytes
  // (not the in-trailer hash, which covers only [start, trailer_magic)).
  // The fixture corpus's expected_canonical_hash_hex pins exactly this
  // shape so downstream consumers (oracle-agreement rows) can correlate.
  const auto full_sha = detail::sha256(m1_payload);
  const std::string blob_hash = detail::to_hex_lower(
      std::span<const std::uint8_t>(full_sha.data(), full_sha.size()));
  const auto manifest = current_manifest();

  if (is_cultists_normal(blob.combat_state)) {
    return project_cultists_normal(blob.combat_state);
  }
  if (is_louse_progenitor_normal(blob.combat_state)) {
    return project_louse_progenitor_normal(blob.combat_state);
  }

  // Reject path. Stamp the rejection with manifest + canonical hash, and
  // attach the unknown-power diagnostic if applicable.
  AdapterReject reject;
  reject.unsupported.blob_canonical_hash = blob_hash;
  reject.unsupported.monster_ids.reserve(blob.combat_state.enemies.size());
  for (const auto& e : blob.combat_state.enemies) {
    reject.unsupported.monster_ids.push_back(e.name);
  }
  reject.unsupported.encounter_id =
      detect_encounter_id(sorted_monster_ids(blob.combat_state));
  reject.unsupported.reason = "encounter_not_in_cpp_engine";
  reject.unsupported.manifest = manifest;

  // Unknown-power diagnostic (Q2-ADR-005). Compare the encounter's
  // source-declared spawn-power set against the wire snapshot's actual
  // PowerInstance.ModelId set; any declared id absent from the snapshot
  // populates the diagnostic.
  const auto& expected =
      spawn_power_expectation_for(reject.unsupported.encounter_id);
  if (!expected.empty()) {
    const auto observed = collect_enemy_power_ids(blob.combat_state);
    std::vector<std::string> absent;
    std::copy_if(expected.begin(), expected.end(), std::back_inserter(absent),
                 [&observed](const std::string& want) {
                   return !observed.contains(want);
                 });
    if (!absent.empty()) {
      std::sort(absent.begin(), absent.end());
      UnknownPowerDiagnostic diag;
      diag.blob_canonical_hash = blob_hash;
      diag.encounter_id = reject.unsupported.encounter_id;
      diag.source_declared_power_ids_absent_from_snapshot = std::move(absent);
      diag.source_simulator_build_sha = blob.stamp.git_sha;
      diag.manifest = manifest;
      reject.unknown_powers = std::move(diag);
    }
  }

  return reject;
}

}  // namespace sts2::oracle::adapter
