#include "sts2/game/powers.h"

namespace powers {

Power* find(std::vector<Power>& powers, PowerKind kind) {
    for (auto& p : powers) {
        if (p.kind == kind) return &p;
    }
    return nullptr;
}

const Power* find(const std::vector<Power>& powers, PowerKind kind) {
    for (const auto& p : powers) {
        if (p.kind == kind) return &p;
    }
    return nullptr;
}

int amount(const std::vector<Power>& powers, PowerKind kind) {
    const Power* p = find(powers, kind);
    return p ? p->amount : 0;
}

void apply(std::vector<Power>& target, PowerKind kind, int amt) {
    if (Power* existing = find(target, kind)) {
        existing->amount += amt;
        if (kind == PowerKind::Ritual) existing->just_applied = true;
        return;
    }
    target.push_back(Power{kind, amt, kind == PowerKind::Ritual});
}

void tick_at_turn_end(std::vector<Power>& powers) {
    // Ritual before Weak: Weak does not interact with Ritual, but source order is Ritual listener first.
    if (Power* ritual = find(powers, PowerKind::Ritual)) {
        if (ritual->just_applied) {
            ritual->just_applied = false;
        } else {
            int gain = ritual->amount;
            apply(powers, PowerKind::Strength, gain);
        }
    }
    for (auto it = powers.begin(); it != powers.end(); ) {
        if (it->kind == PowerKind::Weak) {
            it->amount -= 1;
            if (it->amount <= 0) { it = powers.erase(it); continue; }
        }
        ++it;
    }
}

}
