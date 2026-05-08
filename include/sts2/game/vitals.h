#pragma once

#include <vector>
#include "sts2/game/power.h"

namespace sts2::game {

struct Vitals {
    int hp = 0;
    int max_hp = 0;
    int block = 0;
    std::vector<Power> powers;
};

}  // namespace sts2::game
