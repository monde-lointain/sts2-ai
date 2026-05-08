#pragma once

#include <vector>

#include "sts2/game/card.h"

namespace sts2::cards {

game::Card make_strike();
game::Card make_defend();
game::Card make_neutralize();
game::Card make_survivor();

std::vector<game::Card> make_silent_starter_deck();

}  // namespace sts2::cards
