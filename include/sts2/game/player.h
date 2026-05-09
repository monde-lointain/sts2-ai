#pragma once

#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/vitals.h"

namespace sts2::game {

struct Player {
  Vitals vitals{.hp = 70, .max_hp = 70, .block = 0, .powers = {}};
  int energy = 0;
  int max_energy = 3;

  std::vector<Card> draw_pile;
  std::vector<Card> hand;
  std::vector<Card> discard_pile;
  std::vector<Card> exhaust_pile;
};

}  // namespace sts2::game
