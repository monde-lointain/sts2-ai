#include "sts2/app/prompts.h"

#include <istream>
#include <ostream>
#include <string>
#include <vector>

#include "sts2/game/combat.h"
#include "sts2/game/player.h"
#include "sts2/input/input.h"
#include "sts2/render/ansi.h"
#include "sts2/render/render.h"

namespace app {

int prompt_index(std::ostream& out, std::istream& in, const char* label, int max_inclusive) {
    while (true) {
        out << label << std::flush;
        int idx = input::read_index(in, max_inclusive);
        if (idx >= 0) return idx;
        out << ansi::kRed << "  invalid index." << ansi::kReset << "\n";
    }
}

int prompt_target(const Combat& c, std::istream& in, std::ostream& out) {
    std::vector<int> alive_indices;
    for (std::size_t i = 0; i < c.enemies().size(); ++i) {
        if (c.enemies()[i].vitals.hp > 0) alive_indices.push_back(static_cast<int>(i));
    }
    if (alive_indices.empty()) return -1;
    if (alive_indices.size() == 1) return alive_indices[0];
    std::string label = std::string("\n") + ansi::kGreen + ">" + ansi::kReset + " Target enemy [index]: ";
    int display_idx = prompt_index(out, in, label.c_str(), static_cast<int>(alive_indices.size()) - 1);
    return alive_indices[static_cast<std::size_t>(display_idx)];
}

int prompt_discard(const Combat& combat, std::istream& in, std::ostream& out) {
    const Player& p = combat.player();
    if (p.hand.size() == 1) return 0;
    render::render_combat(combat, out);
    std::string label = "  Discard which? [0-" + std::to_string(p.hand.size() - 1) + "]: ";
    return prompt_index(out, in, label.c_str(), static_cast<int>(p.hand.size()) - 1);
}

}
