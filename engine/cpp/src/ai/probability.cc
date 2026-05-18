#include "sts2/ai/probability.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstddef>
#include <iterator>

#include "sts2/game/card_effects.h"
#include "sts2/game/types.h"

namespace sts2::ai::probability {

namespace {

using sts2::game::CardId;
using sts2::game::card_effects::kCountedCardIds;

constexpr int kMaxN = 12;

constexpr auto kBinomTable = []() {
  std::array<std::array<uint64_t, kMaxN + 1>, kMaxN + 1> t{};
  for (int n = 0; n <= kMaxN; ++n) {
    t[n][0] = 1;
    for (int r = 1; r <= n; ++r) {
      t[n][r] = t[n - 1][r - 1] + (r <= n - 1 ? t[n - 1][r] : 0);
    }
  }
  return t;
}();

void enumerate_recursive(const CardCounts& pool, int k_left,
                         std::size_t card_idx, uint64_t numerator,
                         CardCounts& acc, std::vector<Outcome>& out,
                         double inv_denom) {
  if (card_idx == std::size(kCountedCardIds)) {
    if (k_left == 0) {
      Outcome o;
      o.hand = acc;
      o.weight = static_cast<double>(numerator) * inv_denom;
      out.push_back(o);
    }
    return;
  }
  const CardId id = kCountedCardIds[card_idx];
  const int avail = pool[id];
  const int upper = std::min(avail, k_left);
  for (int take = 0; take <= upper; ++take) {
    acc[id] = take;
    enumerate_recursive(pool, k_left - take, card_idx + 1,
                        numerator * binom(avail, take), acc, out, inv_denom);
  }
  acc[id] = 0;  // restore for sibling branches
}

}  // namespace

uint64_t binom(int n, int r) noexcept {
  if (r < 0 || n < 0 || r > n || n > kMaxN) {
    return 0;
  }
  return kBinomTable[n][r];
}

std::vector<Outcome> enumerate_draws(CardCounts pool, int k) {
  assert(pool.total() <= kMaxN);
  assert(k >= 0 && k <= pool.total());
  for (const auto id : kCountedCardIds) {
    assert(pool[id] <= kMaxN);
    (void)id;
  }

  std::vector<Outcome> out;
  if (k == 0) {
    out.push_back(Outcome{CardCounts{}, 1.0});
    return out;
  }

  const int total = pool.total();
  const uint64_t denom = binom(total, k);
  const double inv_denom = 1.0 / static_cast<double>(denom);

  CardCounts acc;
  enumerate_recursive(pool, k, 0, 1, acc, out, inv_denom);
  return out;
}

}  // namespace sts2::ai::probability
