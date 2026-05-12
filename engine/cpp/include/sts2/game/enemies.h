#pragma once

#include "sts2/game/enemy.h"

namespace sts2::game {
class Combat;
class Rng;
}  // namespace sts2::game

namespace sts2::enemies {

game::Enemy make_calcified_cultist(game::Rng& rng);
game::Enemy make_damp_cultist(game::Rng& rng);

void roll_next_move(game::Enemy& e);
void act(game::Enemy& e, game::Combat& combat);

}  // namespace sts2::enemies
