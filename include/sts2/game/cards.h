#pragma once

#include <vector>

#include "sts2/game/card.h"

namespace cards {

Card make_strike();
Card make_defend();
Card make_neutralize();
Card make_survivor();

std::vector<Card> make_silent_starter_deck();

}
