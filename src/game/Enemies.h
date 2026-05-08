#pragma once

#include "game/Enemy.h"

class Combat;
class Rng;

namespace enemies {

Enemy make_calcified_cultist(Rng& rng);
Enemy make_damp_cultist(Rng& rng);

void roll_next_move(Enemy& e);
void act(Enemy& e, Combat& combat);

}
