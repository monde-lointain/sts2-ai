#pragma once

#include <cstdint>
#include <random>
#include <vector>

class Rng {
public:
    explicit Rng(uint64_t seed)
        : seed_(seed), engine_(seed) {}

    int uniform_int(int lo_inclusive, int hi_inclusive) {
        std::uniform_int_distribution<int> dist(lo_inclusive, hi_inclusive);
        return dist(engine_);
    }

    template <typename T>
    void shuffle(std::vector<T>& v) {
        const int n = static_cast<int>(v.size());
        for (int i = n - 1; i >= 1; --i) {
            int j = uniform_int(0, i);
            using std::swap;
            swap(v[static_cast<size_t>(i)], v[static_cast<size_t>(j)]);
        }
    }

    uint64_t seed() const { return seed_; }

private:
    uint64_t seed_;
    std::mt19937_64 engine_;
};
