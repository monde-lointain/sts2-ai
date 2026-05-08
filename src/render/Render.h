#pragma once

#include <ostream>
#include <string>

class Combat;

namespace render {

void render_combat(const Combat& combat, std::ostream& out);

std::string card_inline_stats(int card_id);

}
