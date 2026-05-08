#pragma once

#include <cstdint>
#include <utility>
#include <vector>

#include "game/Combat.h"
#include "game/Enemy.h"

inline Combat make_combat_with(Enemy enemy, uint64_t seed = 42) {
    Combat c{seed};
    c.enemies.push_back(std::move(enemy));
    return c;
}

inline Combat make_combat_with(std::vector<Enemy> enemies_in, uint64_t seed = 42) {
    Combat c{seed};
    c.enemies = std::move(enemies_in);
    return c;
}

inline Enemy make_dummy_enemy(int hp) {
    Enemy e;
    e.name = "Dummy";
    e.vitals.hp = hp;
    e.vitals.max_hp = hp;
    return e;
}
