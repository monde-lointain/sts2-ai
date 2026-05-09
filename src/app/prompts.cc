#include "sts2/app/prompts.h"

#include <cstddef>
#include <istream>
#include <ostream>
#include <string>
#include <vector>

#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"
#include "sts2/input/input.h"
#include "sts2/render/ansi.h"
#include "sts2/render/render.h"

namespace sts2::app {

int prompt_index(std::ostream& out, std::istream& in, const char* label,
                 int max_inclusive) {
  while (true) {
    out << label << std::flush;
    int idx = sts2::input::read_index(in, max_inclusive);
    if (idx >= 0) {
      return idx;
    }
    out << ansi::kRed << "  invalid index." << ansi::kReset << "\n";
  }
}

sts2::game::EnemySlot prompt_target(const sts2::game::Combat& c,
                                    std::istream& in, std::ostream& out) {
  const std::vector<sts2::game::EnemySlot> alive = c.alive_enemy_indices();
  if (alive.empty()) {
    return sts2::game::EnemySlot::none();
  }
  if (alive.size() == 1) {
    return alive[0];
  }
  std::string label = std::string("\n") + ansi::kGreen + ">" + ansi::kReset +
                      " Target enemy [index]: ";
  int display_idx = prompt_index(out, in, label.c_str(),
                                 static_cast<int>(alive.size()) - 1);
  return alive[static_cast<std::size_t>(display_idx)];
}

sts2::game::HandIndex prompt_discard(const sts2::game::Combat& combat,
                                     std::istream& in, std::ostream& out) {
  const std::size_t hand = combat.hand_size();
  if (hand == 1) {
    return sts2::game::HandIndex{0};
  }
  sts2::render::render_combat(combat, out);
  std::string label =
      "  Discard which? [0-" + std::to_string(hand - 1) + "]: ";
  int idx = prompt_index(out, in, label.c_str(), static_cast<int>(hand) - 1);
  return sts2::game::HandIndex{idx};
}

}  // namespace sts2::app
