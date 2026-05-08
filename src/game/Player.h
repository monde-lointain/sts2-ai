#pragma once

#include <vector>
#include "game/Card.h"
#include "game/Power.h"

struct Player {
    int hp = 70;
    int max_hp = 70;
    int block = 0;
    int energy = 0;
    int max_energy = 3;

    std::vector<Card> draw_pile;
    std::vector<Card> hand;
    std::vector<Card> discard_pile;
    std::vector<Card> exhaust_pile;

    std::vector<Power> powers;
};
