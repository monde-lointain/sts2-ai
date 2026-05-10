#pragma once

#include <cstdint>
#include <vector>

#include "sts2/ai/state.h"

namespace sts2::ai::probability {

struct Outcome {
  CardCounts hand;
  double weight{};
};

[[nodiscard]] uint64_t binom(int n, int r) noexcept;

[[nodiscard]] std::vector<Outcome> enumerate_draws(CardCounts pool, int k);

}  // namespace sts2::ai::probability
