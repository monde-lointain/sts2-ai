#pragma once

#include <functional>
#include <vector>

#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Rng.h"

class Combat {
public:
    Player player;
    std::vector<Enemy> enemies;
    Rng rng;
    std::function<int(const Player&)> on_pick_discard;

    int round = 1;
    bool combat_over = false;

    explicit Combat(uint64_t seed) : rng(seed) {}
};
