#pragma once

#include "sts2/game/enemy.h"

class Combat;
class Rng;

namespace enemies {

Enemy make_calcified_cultist(Rng& rng);
Enemy make_damp_cultist(Rng& rng);

void roll_next_move(Enemy& e);
void act(Enemy& e, Combat& combat);

}
