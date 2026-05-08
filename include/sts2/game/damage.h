#pragma once

#include <vector>
#include "sts2/game/power.h"
#include "sts2/game/vitals.h"

namespace damage {

int compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage);

int apply_to_defender(Vitals& target, int incoming);

}
