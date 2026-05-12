#pragma once

#include <cstdint>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::tests::ai {

inline sts2::ai::CardCounts make_counts(uint8_t s, uint8_t d, uint8_t n,
                                        uint8_t v) {
  sts2::ai::CardCounts c;
  c[sts2::game::CardId::kStrike] = s;
  c[sts2::game::CardId::kDefend] = d;
  c[sts2::game::CardId::kNeutralize] = n;
  c[sts2::game::CardId::kSurvivor] = v;
  return c;
}

}  // namespace sts2::tests::ai
