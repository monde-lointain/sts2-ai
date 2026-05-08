#pragma once

#include <string>
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Enemy {
    std::string name;
    Vitals vitals;

    MoveId current_move = MoveId::Incantation;
    bool performed_first_move = false;

    int dark_strike_base = 0;
    int ritual_amount = 0;
};

}  // namespace sts2::game
