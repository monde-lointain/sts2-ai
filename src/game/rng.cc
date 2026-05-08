#include "sts2/game/rng.h"

namespace sts2::game {

Rng::Rng(uint64_t seed) : engine_(seed) {}

int Rng::uniform_int(int lo_inclusive, int hi_inclusive) {
    std::uniform_int_distribution<int> dist(lo_inclusive, hi_inclusive);
    return dist(engine_);
}

}  // namespace sts2::game
