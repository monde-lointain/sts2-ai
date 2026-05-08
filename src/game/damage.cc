#include "sts2/game/damage.h"

#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"

namespace sts2::damage {

int compute_outgoing(const std::vector<sts2::game::Power>& attacker_powers, int base_damage) {
    int d = base_damage + sts2::powers::amount(attacker_powers, sts2::game::PowerKind::Strength);
    if (sts2::powers::amount(attacker_powers, sts2::game::PowerKind::Weak) > 0) {
        d = static_cast<int>(d * 0.75);
    }
    return d < 0 ? 0 : d;
}

int apply_to_defender(sts2::game::Vitals& target, int incoming) {
    if (incoming <= target.block) {
        target.block -= incoming;
        return 0;
    }
    incoming -= target.block;
    target.block = 0;
    int hp_loss = incoming < target.hp ? incoming : target.hp;
    target.hp -= hp_loss;
    return hp_loss;
}

}  // namespace sts2::damage
