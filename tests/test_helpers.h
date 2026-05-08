#pragma once

#include <cstdint>
#include <utility>
#include <vector>

#include "game/Combat.h"
#include "game/Enemy.h"

class CombatTestAccess {
public:
    explicit CombatTestAccess(Combat& c) : c_(c) {}
    Player& player()                          { return c_.player_; }
    // Convenience; not in the spec's six-method list but used by several tests.
    Enemy&  enemy(int i)                      { return c_.enemies_[static_cast<size_t>(i)]; }
    std::vector<Enemy>& enemies()             { return c_.enemies_; }
    Rng&    rng()                             { return c_.rng_; }
    int&    round()                           { return c_.round_; }
    bool&   combat_over()                     { return c_.combat_over_; }
private:
    Combat& c_;
};

inline Combat make_combat_with(Enemy enemy, uint64_t seed = 42) {
    Combat c{seed};
    c.add_enemy(std::move(enemy));
    return c;
}

inline Combat make_combat_with(std::vector<Enemy> enemies_in, uint64_t seed = 42) {
    Combat c{seed};
    for (auto& e : enemies_in) c.add_enemy(std::move(e));
    return c;
}

inline Enemy make_dummy_enemy(int hp) {
    Enemy e;
    e.name = "Dummy";
    e.vitals.hp = hp;
    e.vitals.max_hp = hp;
    return e;
}
