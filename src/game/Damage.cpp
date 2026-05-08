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

int apply_to_defender(int& defender_block, int& defender_hp, int incoming) {
    if (incoming <= defender_block) {
        defender_block -= incoming;
        return 0;
    }
    incoming -= defender_block;
    defender_block = 0;
    int hp_loss = incoming < defender_hp ? incoming : defender_hp;
    defender_hp -= hp_loss;
    if (defender_hp < 0) defender_hp = 0;
    return hp_loss;
}

}
