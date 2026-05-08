#pragma once

#include <vector>

#include "game/Card.h"

namespace cards {

constexpr int IdStrike     = 1;
constexpr int IdDefend     = 2;
constexpr int IdNeutralize = 3;
constexpr int IdSurvivor   = 4;

Card make_strike();
Card make_defend();
Card make_neutralize();
Card make_survivor();

std::vector<Card> make_silent_starter_deck();

}
