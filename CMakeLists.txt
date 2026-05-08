cmake_minimum_required(VERSION 3.16)
project(sts2_fight CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/utf-8 /W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

set(RNG_SOURCES
    src/game/Rng.cpp
)

add_executable(sts2_fight
    src/main.cpp
    ${RNG_SOURCES}
)

add_executable(sts2_fight_tests
    tests/test_main.cpp
    ${RNG_SOURCES}
)

enable_testing()
add_test(NAME unit COMMAND sts2_fight_tests)
