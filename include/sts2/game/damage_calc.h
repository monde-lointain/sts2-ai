#pragma once

// Canonical damage formula primitives, shared by the production damage path
// (src/game/damage.cc) and the AI transition simulator (src/ai/transition.cc)
// to prevent silent divergence.

#include "sts2/game/stat.h"

namespace sts2::damage {

// Canonical outgoing-damage formula: base + strength, scaled 0.75 if weak>0,
// clamped to >=0. Pure; no allocation.
[[nodiscard]] inline int compute_outgoing(int base, int strength, int weak) noexcept {
  int d = base + strength;
  if (weak > 0) {
    d = static_cast<int>(d * 0.75);
  }
  return d < 0 ? 0 : d;
}

// Canonical block-then-hp absorption. Mutates hp/block in place; returns the
// hp lost (>=0). Caller is responsible for any death/cleanup logic.
[[nodiscard]] inline int apply_to_defender(int& hp, int& block, int incoming) noexcept {
  if (incoming <= block) {
    block -= incoming;
    return 0;
  }
  incoming -= block;
  block = 0;
  int hp_loss = incoming < hp ? incoming : hp;
  hp -= hp_loss;
  return hp_loss;
}

[[nodiscard]] inline int apply_to_defender(sts2::game::Stat& hp, sts2::game::Stat& block, int incoming) noexcept {
  if (incoming <= block.value()) {
    block -= incoming;
    return 0;
  }
  incoming -= block.value();
  block = sts2::game::Stat{0};
  const int hp_loss = incoming < hp.value() ? incoming : hp.value();
  hp -= hp_loss;
  return hp_loss;
}

}  // namespace sts2::damage
