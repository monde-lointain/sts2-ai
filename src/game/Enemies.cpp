#include "game/Enemies.h"

#include "game/Combat.h"
#include "game/Damage.h"
#include "game/Powers.h"
#include "game/Rng.h"

namespace enemies {

Enemy make_calcified_cultist(Rng& rng) {
    Enemy e;
    e.name = "Calcified Cultist";
    int hp = rng.uniform_int(38, 41);
    e.vitals.max_hp = hp;
    e.vitals.hp = hp;
    e.dark_strike_base = 9;
    e.ritual_amount = 2;
    return e;
}

Enemy make_damp_cultist(Rng& rng) {
    Enemy e;
    e.name = "Damp Cultist";
    int hp = rng.uniform_int(51, 53);
    e.vitals.max_hp = hp;
    e.vitals.hp = hp;
    e.dark_strike_base = 1;
    e.ritual_amount = 5;
    return e;
}

void roll_next_move(Enemy& e) {
    if (!e.performed_first_move) {
        e.performed_first_move = true;
        return;
    }
    if (e.current_move == MoveId::Incantation) {
        e.current_move = MoveId::DarkStrike;
    }
}

void act(Enemy& e, Combat& combat) {
    switch (e.current_move) {
        case MoveId::Incantation:
            powers::apply(e.vitals.powers, PowerKind::Ritual, e.ritual_amount);
            break;
        case MoveId::DarkStrike: {
            int dmg = damage::compute_outgoing(e.vitals.powers, e.dark_strike_base);
            damage::apply_to_defender(combat.player.vitals, dmg);
            break;
        }
    }
}

}
