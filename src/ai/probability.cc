#include "sts2/ai/probability.h"

#include <array>
#include <cassert>

#include "sts2/game/types.h"

namespace sts2::ai::probability {

namespace {

using sts2::game::CardId;

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
  assert(pool[CardId::kStrike] <= kMaxN && pool[CardId::kDefend] <= kMaxN &&
         pool[CardId::kNeutralize] <= kMaxN &&
         pool[CardId::kSurvivor] <= kMaxN);

  std::vector<Outcome> out;
  if (k == 0) {
    out.push_back(Outcome{CardCounts{}, 1.0});
    return out;
  }

  const int total = pool.total();
  const uint64_t denom = binom(total, k);
  const double inv_denom = 1.0 / static_cast<double>(denom);

  for (int s = 0; s <= pool[CardId::kStrike]; ++s) {
    if (s > k) break;
    const uint64_t cs = binom(pool[CardId::kStrike], s);
    for (int d = 0; d <= pool[CardId::kDefend]; ++d) {
      const int sd = s + d;
      if (sd > k) break;
      const uint64_t csd = cs * binom(pool[CardId::kDefend], d);
      for (int n = 0; n <= pool[CardId::kNeutralize]; ++n) {
        const int sdn = sd + n;
        if (sdn > k) break;
        const uint64_t csdn = csd * binom(pool[CardId::kNeutralize], n);
        const int v = k - sdn;
        if (v < 0 || v > pool[CardId::kSurvivor]) continue;
        const uint64_t num = csdn * binom(pool[CardId::kSurvivor], v);
        Outcome o;
        o.hand[CardId::kStrike] = static_cast<uint8_t>(s);
        o.hand[CardId::kDefend] = static_cast<uint8_t>(d);
        o.hand[CardId::kNeutralize] = static_cast<uint8_t>(n);
        o.hand[CardId::kSurvivor] = static_cast<uint8_t>(v);
        o.weight = static_cast<double>(num) * inv_denom;
        out.push_back(o);
      }
    }
  }
  return out;
}

}  // namespace sts2::ai::probability
