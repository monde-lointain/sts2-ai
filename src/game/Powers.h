#pragma once

#include <vector>
#include "game/Power.h"
#include "game/Types.h"

namespace powers {

Power*       find(std::vector<Power>& powers, PowerKind kind);
const Power* find(const std::vector<Power>& powers, PowerKind kind);

int amount(const std::vector<Power>& powers, PowerKind kind);

void apply(std::vector<Power>& target, PowerKind kind, int amt);

void tick_at_turn_end(std::vector<Power>& powers);

}
