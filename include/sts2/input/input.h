#pragma once

#include <iosfwd>

#include "sts2/game/index_types.h"

namespace sts2::input {

struct Action {
  enum Kind { kPlayCard, kEndTurn, kQuit, kInvalid };
  Kind kind = kInvalid;
  sts2::game::HandIndex card_idx = sts2::game::HandIndex::none();
};

Action read_action(std::istream& in);

int read_index(std::istream& in, int max_inclusive);

}  // namespace sts2::input
