#include <algorithm>
#include <vector>

#include "game/Rng.h"
#include "test_runner.h"

TEST(rng_uniform_int_in_range) {
    Rng r(42);
    for (int i = 0; i < 1000; ++i) {
        int x = r.uniform_int(38, 41);
        CHECK(x >= 38 && x <= 41);
    }
}

TEST(rng_seed_determinism) {
    Rng a(123);
    Rng b(123);
    for (int i = 0; i < 100; ++i) {
        CHECK(a.uniform_int(0, 1000) == b.uniform_int(0, 1000));
    }
}

TEST(rng_shuffle_determinism_and_preservation) {
    std::vector<int> v1{1,2,3,4,5,6,7,8,9,10};
    std::vector<int> v2 = v1;
    Rng a(7);
    Rng b(7);
    a.shuffle(v1);
    b.shuffle(v2);
    CHECK(v1 == v2);
    std::vector<int> sorted = v1;
    std::sort(sorted.begin(), sorted.end());
    CHECK(sorted == (std::vector<int>{1,2,3,4,5,6,7,8,9,10}));
}
