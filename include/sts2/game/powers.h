#pragma once

#include <vector>
#include "sts2/game/power.h"
#include "sts2/game/types.h"

namespace sts2::powers {

game::Power*       find(std::vector<game::Power>& powers, game::PowerKind kind);
const game::Power* find(const std::vector<game::Power>& powers, game::PowerKind kind);

int amount(const std::vector<game::Power>& powers, game::PowerKind kind);

void apply(std::vector<game::Power>& target, game::PowerKind kind, int amt);

void tick_at_turn_end(std::vector<game::Power>& powers);

}  // namespace sts2::powers
