#include "test_runner.h"

std::vector<TestCase>& test_registry() {
    static std::vector<TestCase> r;
    return r;
}

TestRegistrar::TestRegistrar(const char* name, std::function<void()> fn) {
    test_registry().push_back({name, std::move(fn)});
}
