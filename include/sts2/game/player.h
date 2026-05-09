#pragma once

#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/deck.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Player {
  Vitals vitals{.hp = 70, .max_hp = 70, .block = 0, .powers = {}};
  int energy = 0;

  Deck deck;
  std::vector<Card> hand;
};

}  // namespace sts2::game
