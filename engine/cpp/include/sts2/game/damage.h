#pragma once

#include <vector>

#include "sts2/game/power.h"
#include "sts2/game/vitals.h"

namespace sts2::damage {

[[nodiscard]] int compute_outgoing(
    const std::vector<game::Power>& attacker_powers, int base_damage);

[[nodiscard]] int apply_to_defender(game::Vitals& target, int incoming);

// Thin wrapper over damage_calc::compute_outgoing with explicit scalar args.
// Provided for naming consistency with compute_outgoing_block below.
// Delegates to: damage_calc::compute_outgoing(base, strength, weak).
[[nodiscard]] int compute_outgoing_attack(int base, int attacker_strength,
                                          int attacker_weak) noexcept;

// STS canonical block formula:
//   effective_block = (base + gainer_dexterity)
//                     * (gainer_frail && is_powered_source ? 0.75 : 1.0)
// Integer rounding via (v * 3) / 4 (floor division). Result clamped to >= 0.
// Frail tax applies only when the block source is powered (powered card or
// monster move); non-powered block sources are not taxed per STS2 semantics.
[[nodiscard]] int compute_outgoing_block(int base, int gainer_dexterity,
                                         bool gainer_frail,
                                         bool is_powered_source) noexcept;

}  // namespace sts2::damage
