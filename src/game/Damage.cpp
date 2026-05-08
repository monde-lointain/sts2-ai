#include "game/Damage.h"

#include "game/Power.h"
#include "game/Powers.h"
#include "game/Types.h"

namespace damage {

int compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage) {
    int d = base_damage + powers::amount(attacker_powers, PowerKind::Strength);
    if (powers::amount(attacker_powers, PowerKind::Weak) > 0) {
        d = static_cast<int>(d * 0.75);
    }
    return d < 0 ? 0 : d;
}

int apply_to_defender(Vitals& target, int incoming) {
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

}
