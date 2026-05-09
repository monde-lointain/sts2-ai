#pragma once

#include <vector>

#include "sts2/game/card.h"

namespace sts2::cards {

[[nodiscard]] game::Card make_card(game::CardId id);

std::vector<game::Card> make_silent_starter_deck();

}  // namespace sts2::cards
