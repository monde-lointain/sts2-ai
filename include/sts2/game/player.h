#pragma once

#include "sts2/game/deck.h"
#include "sts2/game/hand.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Player {
  Vitals vitals{.hp = 70, .max_hp = 70, .block = 0, .powers = {}};
  int energy = 0;

  Deck deck;
  Hand hand;
};

}  // namespace sts2::game
