#include <cstdio>
#include <cstdlib>
#include <exception>
#include "test_runner.h"

int main() {
    int failures = 0;
    for (const auto& t : test_registry()) {
        std::printf("[RUN ] %s\n", t.name.c_str());
        try {
            t.fn();
            std::printf("[ OK ] %s\n", t.name.c_str());
        } catch (const std::exception& e) {
            std::printf("[FAIL] %s: %s\n", t.name.c_str(), e.what());
            ++failures;
        } catch (...) {
            std::printf("[FAIL] %s: unknown\n", t.name.c_str());
            ++failures;
        }
    }
    std::printf("%zu tests, %d failures\n", test_registry().size(), failures);
    return failures == 0 ? 0 : 1;
}
