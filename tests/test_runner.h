#pragma once

#include <functional>
#include <stdexcept>
#include <string>
#include <vector>

struct TestCase {
    std::string name;
    std::function<void()> fn;
};

std::vector<TestCase>& test_registry();

struct TestRegistrar {
    TestRegistrar(const char* name, std::function<void()> fn);
};

#define TEST(name)                                                  \
    static void test_##name();                                      \
    static TestRegistrar registrar_##name(#name, test_##name);      \
    static void test_##name()

#define CHECK(expr)                                                                \
    do {                                                                           \
        if (!(expr)) {                                                             \
            throw std::runtime_error(std::string(__FILE__ ":") +                   \
                                     std::to_string(__LINE__) + ": CHECK(" #expr ")"); \
        }                                                                          \
    } while (0)
