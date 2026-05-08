#include <algorithm>
#include <cassert>
#include <cstdio>
#include <string>
#include <vector>
#include "../src/game/Rng.h"

#define RUN(name) do { std::printf("[RUN ] %s\n", #name); name(); std::printf("[ OK ] %s\n", #name); } while(0)

static void test_rng_uniform_int_in_range() {
    Rng r(42);
    for (int i = 0; i < 1000; ++i) {
        int x = r.uniform_int(38, 41);
        assert(x >= 38 && x <= 41);
    }
}

static void test_rng_seed_determinism() {
    Rng a(123), b(123);
    for (int i = 0; i < 100; ++i) assert(a.uniform_int(0, 1000) == b.uniform_int(0, 1000));
}

static void test_rng_shuffle_determinism() {
    std::vector<int> v1{1,2,3,4,5,6,7,8,9,10};
    std::vector<int> v2 = v1;
    Rng a(7), b(7);
    a.shuffle(v1);
    b.shuffle(v2);
    assert(v1 == v2);
    std::vector<int> sorted = v1;
    std::sort(sorted.begin(), sorted.end());
    assert(sorted == std::vector<int>({1,2,3,4,5,6,7,8,9,10}));
}

int main() {
    RUN(test_rng_uniform_int_in_range);
    RUN(test_rng_seed_determinism);
    RUN(test_rng_shuffle_determinism);
    std::printf("All tests passed.\n");
    return 0;
}
