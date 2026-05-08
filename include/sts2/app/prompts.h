#pragma once

// Internal helpers for main.cc prompts. Test-only header. Not part of the
// public sts2::simulator API.

#include <iosfwd>

namespace sts2::game {
class Combat;
}

namespace sts2::app {

int prompt_index(std::ostream& out, std::istream& in, const char* label,
                 int max_inclusive);
int prompt_target(const sts2::game::Combat& combat, std::istream& in,
                  std::ostream& out);
int prompt_discard(const sts2::game::Combat& combat, std::istream& in,
                   std::ostream& out);

}  // namespace sts2::app
