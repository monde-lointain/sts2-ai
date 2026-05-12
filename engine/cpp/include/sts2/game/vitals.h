#pragma once

#include <vector>

#include "sts2/game/power.h"
#include "sts2/game/stat.h"

namespace sts2::game {

struct Vitals {
  Stat hp;
  Stat max_hp;
  Stat block;
  std::vector<Power> powers;
};

}  // namespace sts2::game
