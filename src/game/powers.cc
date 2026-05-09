#include "sts2/game/powers.h"

#include <algorithm>

#include "sts2/game/move_calc.h"

namespace sts2::powers {

sts2::game::Power* find(std::vector<sts2::game::Power>& powers,
                        sts2::game::PowerKind kind) {
  auto it = std::find_if(
      powers.begin(), powers.end(),
      [kind](const sts2::game::Power& p) { return p.kind == kind; });
  return it != powers.end() ? &*it : nullptr;
}

const sts2::game::Power* find(const std::vector<sts2::game::Power>& powers,
                              sts2::game::PowerKind kind) {
  auto it = std::find_if(
      powers.begin(), powers.end(),
      [kind](const sts2::game::Power& p) { return p.kind == kind; });
  return it != powers.end() ? &*it : nullptr;
}

int amount(const std::vector<sts2::game::Power>& powers,
           sts2::game::PowerKind kind) {
  const sts2::game::Power* p = find(powers, kind);
  return (p != nullptr) ? p->amount : 0;
}

void apply(std::vector<sts2::game::Power>& target, sts2::game::PowerKind kind,
           int amt) {
  if (sts2::game::Power* existing = find(target, kind)) {
    existing->amount += amt;
    if (kind == sts2::game::PowerKind::kRitual) {
      existing->just_applied = true;
    }
    return;
  }
  target.push_back(sts2::game::Power{
      .kind = kind,
      .amount = amt,
      .just_applied = kind == sts2::game::PowerKind::kRitual});
}

void tick_at_turn_end(std::vector<sts2::game::Power>& powers) {
  // Ritual before Weak: Weak does not interact with Ritual, but source order is
  // Ritual listener first.
  if (sts2::game::Power* ritual =
          find(powers, sts2::game::PowerKind::kRitual)) {
    int gain = sts2::game::move_calc::ritual_tick_strength_gain(
        ritual->just_applied, ritual->amount);
    if (gain > 0) apply(powers, sts2::game::PowerKind::kStrength, gain);
  }
  for (auto it = powers.begin(); it != powers.end();) {
    if (it->kind == sts2::game::PowerKind::kWeak) {
      it->amount -= 1;
      if (it->amount <= 0) {
        it = powers.erase(it);
        continue;
      }
    }
    ++it;
  }
}

}  // namespace sts2::powers
