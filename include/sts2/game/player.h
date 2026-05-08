#pragma once

#include <vector>
#include "sts2/game/card.h"
#include "sts2/game/vitals.h"

struct Player {
    Vitals vitals{70, 70, 0, {}};
    int energy = 0;
    int max_energy = 3;

    std::vector<Card> draw_pile;
    std::vector<Card> hand;
    std::vector<Card> discard_pile;
    std::vector<Card> exhaust_pile;
};
