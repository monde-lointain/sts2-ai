#pragma once

#include <string>
#include "game/Types.h"
#include "game/Vitals.h"

struct Enemy {
    std::string name;
    Vitals vitals;

    MoveId current_move = MoveId::Incantation;
    bool performed_first_move = false;

    int dark_strike_base = 0;
    int ritual_amount = 0;
};
