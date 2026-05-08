#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <random>
#include <string>

static bool parse_uint64(const char* s, uint64_t& out) {
    if (s == nullptr || *s == '\0') return false;
    std::string str(s);
    for (char c : str) {
        if (c < '0' || c > '9') return false;
    }
    try {
        size_t idx = 0;
        unsigned long long v = std::stoull(str, &idx, 10);
        if (idx != str.size()) return false;
        out = static_cast<uint64_t>(v);
        return true;
    } catch (...) {
        return false;
    }
}

int main(int argc, char** argv) {
    uint64_t seed = 0;
    bool seed_set = false;

    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--seed") == 0) {
            if (i + 1 >= argc) {
                std::fprintf(stderr, "error: --seed requires an unsigned 64-bit integer argument\n");
                return 1;
            }
            if (!parse_uint64(argv[i + 1], seed)) {
                std::fprintf(stderr, "error: --seed value '%s' is not a valid uint64\n", argv[i + 1]);
                return 1;
            }
            seed_set = true;
            ++i;
        } else {
            std::fprintf(stderr, "error: unknown argument '%s'\n", argv[i]);
            return 1;
        }
    }

    if (!seed_set) {
        std::random_device rd;
        uint64_t a = static_cast<uint64_t>(rd());
        uint64_t b = static_cast<uint64_t>(rd());
        seed = (a << 32) | b;
    }

    std::printf("seed=%llu\n", static_cast<unsigned long long>(seed));
    return 0;
}
