#pragma once

#include <iosfwd>

namespace input {

struct Action {
    enum Kind { PlayCard, EndTurn, Quit, Invalid };
    Kind kind = Invalid;
    int card_idx = -1;
};

Action read_action(std::istream& in);

int read_index(std::istream& in, int max_inclusive);

}
