#pragma once

#include <vector>

#include "sts2/game/power.h"
#include "sts2/game/vitals.h"

namespace sts2::damage {

[[nodiscard]] int compute_outgoing(const std::vector<game::Power>& attacker_powers,
                                   int base_damage);

[[nodiscard]] int apply_to_defender(game::Vitals& target, int incoming);

}  // namespace sts2::damage
