#include "game/Rng.h"

Rng::Rng(uint64_t seed) : engine_(seed) {}

int Rng::uniform_int(int lo_inclusive, int hi_inclusive) {
    std::uniform_int_distribution<int> dist(lo_inclusive, hi_inclusive);
    return dist(engine_);
}
