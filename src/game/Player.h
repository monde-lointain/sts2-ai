#pragma once

#include <vector>
#include "game/Card.h"
#include "game/Vitals.h"

struct Player {
    Vitals vitals{70, 70, 0, {}};
    int energy = 0;
    int max_energy = 3;

    std::vector<Card> draw_pile;
    std::vector<Card> hand;
    std::vector<Card> discard_pile;
    std::vector<Card> exhaust_pile;
};
