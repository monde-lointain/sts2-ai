#pragma once

#include <algorithm>
#include <cstdint>
#include <string_view>

#include "sts2/game/types.h"
#include "sts2/oracle/adapter/state_blob.h"

// Shared power-projection lookup helpers for encounter projections (wave-18+).
// Inline; no separate .cc TU required.

namespace sts2::oracle::adapter {

// Map a Q1-wire PowerInstance.ModelId to a PowerKind enum value.
// Returns false if the id is not recognized as a Q2-relevant power.
[[nodiscard]] inline bool try_power_kind_from_wire_id(
    std::string_view model_id, sts2::game::PowerKind& out) noexcept {
  if (model_id == "Strength") {
    out = sts2::game::PowerKind::kStrength;
    return true;
  }
  if (model_id == "Weak") {
    out = sts2::game::PowerKind::kWeak;
    return true;
  }
  if (model_id == "Frail") {
    out = sts2::game::PowerKind::kFrail;
    return true;
  }
  if (model_id == "CurlUp") {
    out = sts2::game::PowerKind::kCurlUp;
    return true;
  }
  if (model_id == "Ritual") {
    out = sts2::game::PowerKind::kRitual;
    return true;
  }
  // Wave-26/M.γ: SurprisePower wire → kSurprise (GremlinMerc OnDeath trigger).
  // ThieveryPower is UNRECOGNIZED → caller sees false → silent-drop
  // (Q2-ADR-005).
  if (model_id == "SurprisePower") {
    out = sts2::game::PowerKind::kSurprise;
    return true;
  }
  return false;
}

// Find power stack count by ModelId from a ParsedCreature; 0 if absent.
[[nodiscard]] inline std::int32_t parsed_power_stacks(
    const ParsedCreature& cr, std::string_view id) noexcept {
  const auto it = std::find_if(
      cr.powers.begin(), cr.powers.end(),
      [id](const ParsedPowerInstance& p) { return p.model_id == id; });
  return (it != cr.powers.end()) ? it->stacks : 0;
}

// Check for a power with just_applied flag set.
[[nodiscard]] inline bool parsed_power_just_applied(
    const ParsedCreature& cr, std::string_view id) noexcept {
  const auto it = std::find_if(
      cr.powers.begin(), cr.powers.end(),
      [id](const ParsedPowerInstance& p) { return p.model_id == id; });
  return (it != cr.powers.end()) && it->just_applied;
}

}  // namespace sts2::oracle::adapter
