#pragma once

#include <cstdint>

#include "sts2/ai/state.h"
#include "sts2/game/types.h"

namespace sts2::tests::ai {

// Wave-23/J.beta: CardCounts.counts widened uint8_t → int32_t to match
// upstream STS2's uniform int storage (Q2-ADR-014). Parameter types widened
// to match.
inline sts2::ai::CardCounts make_counts(int32_t s, int32_t d, int32_t n,
                                        int32_t v) {
  sts2::ai::CardCounts c;
  c[sts2::game::CardId::kStrike] = s;
  c[sts2::game::CardId::kDefend] = d;
  c[sts2::game::CardId::kNeutralize] = n;
  c[sts2::game::CardId::kSurvivor] = v;
  return c;
}

}  // namespace sts2::tests::ai
