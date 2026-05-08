#pragma once

#include <vector>
#include "game/Power.h"

namespace damage {

int compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage);

int apply_to_defender(int& defender_block, int& defender_hp, int incoming);

}
