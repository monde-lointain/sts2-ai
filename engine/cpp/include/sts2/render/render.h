#pragma once

#include <ostream>

namespace sts2::game {
class Combat;
}

namespace sts2::render {

void render_combat(const sts2::game::Combat& combat, std::ostream& out);

}  // namespace sts2::render
