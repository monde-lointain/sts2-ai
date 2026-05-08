#pragma once

#include <vector>
#include "game/Power.h"
#include "game/Vitals.h"

namespace damage {

int compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage);

int apply_to_defender(Vitals& target, int incoming);

}
