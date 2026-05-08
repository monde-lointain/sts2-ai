#pragma once

#include <vector>
struct Power;

namespace damage {

// Compute outgoing damage after attacker's Strength (additive) and Weak (x0.75 multiplicative,
// truncated toward zero), then clamped to 0.
int compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage);

// Apply `incoming` damage to defender: block absorbs first, then remaining hits HP. HP clamped to 0.
// Returns HP lost (i.e., delta to defender HP, >= 0).
int apply_to_defender(int& defender_block, int& defender_hp, int incoming);

}
