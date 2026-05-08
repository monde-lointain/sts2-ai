#pragma once

#include <ostream>

class Combat;

namespace render {

void render_combat(const Combat& combat, std::ostream& out);

}
