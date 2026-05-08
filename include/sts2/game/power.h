#pragma once

#include "sts2/game/types.h"

namespace sts2::game {

struct Power {
  PowerKind kind = PowerKind::Weak;
  int amount = 0;
  bool just_applied = false;  // Ritual sets this true on the turn it's applied;
                              // tick suppresses Strength gain that one turn.
};

}  // namespace sts2::game
