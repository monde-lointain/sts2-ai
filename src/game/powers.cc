#include "sts2/game/powers.h"

namespace sts2::powers {

sts2::game::Power* find(std::vector<sts2::game::Power>& powers, sts2::game::PowerKind kind) {
    for (auto& p : powers) {
        if (p.kind == kind) return &p;
    }
    return nullptr;
}

const sts2::game::Power* find(const std::vector<sts2::game::Power>& powers, sts2::game::PowerKind kind) {
    for (const auto& p : powers) {
        if (p.kind == kind) return &p;
    }
    return nullptr;
}

int amount(const std::vector<sts2::game::Power>& powers, sts2::game::PowerKind kind) {
    const sts2::game::Power* p = find(powers, kind);
    return p ? p->amount : 0;
}

void apply(std::vector<sts2::game::Power>& target, sts2::game::PowerKind kind, int amt) {
    if (sts2::game::Power* existing = find(target, kind)) {
        existing->amount += amt;
        if (kind == sts2::game::PowerKind::Ritual) existing->just_applied = true;
        return;
    }
    target.push_back(sts2::game::Power{kind, amt, kind == sts2::game::PowerKind::Ritual});
}

void tick_at_turn_end(std::vector<sts2::game::Power>& powers) {
    // Ritual before Weak: Weak does not interact with Ritual, but source order is Ritual listener first.
    if (sts2::game::Power* ritual = find(powers, sts2::game::PowerKind::Ritual)) {
        if (ritual->just_applied) {
            ritual->just_applied = false;
        } else {
            int gain = ritual->amount;
            apply(powers, sts2::game::PowerKind::Strength, gain);
        }
    }
    for (auto it = powers.begin(); it != powers.end(); ) {
        if (it->kind == sts2::game::PowerKind::Weak) {
            it->amount -= 1;
            if (it->amount <= 0) { it = powers.erase(it); continue; }
        }
        ++it;
    }
}

}  // namespace sts2::powers
