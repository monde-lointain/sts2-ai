#pragma once

#include <string>
#include <vector>
#include "game/Power.h"
#include "game/Types.h"

struct Enemy {
    std::string name;
    int hp = 0;
    int max_hp = 0;
    int block = 0;

    std::vector<Power> powers;

    MoveId current_move = MoveId::Incantation;
    bool performed_first_move = false;

    int dark_strike_base = 0;
    int ritual_amount = 0;
};
