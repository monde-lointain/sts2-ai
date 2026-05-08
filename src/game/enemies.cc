#include "sts2/game/enemies.h"

#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/powers.h"
#include "sts2/game/rng.h"

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
            combat.apply_power_to_enemy_self(e, PowerKind::Ritual, e.ritual_amount);
            break;
        case MoveId::DarkStrike:
            combat.enemy_attack_player(e, e.dark_strike_base);
            break;
    }
}

}
