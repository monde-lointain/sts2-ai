#pragma once

#include <vector>
#include "game/Power.h"

struct Vitals {
    int hp = 0;
    int max_hp = 0;
    int block = 0;
    std::vector<Power> powers;
};
