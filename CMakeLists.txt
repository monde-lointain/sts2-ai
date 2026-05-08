cmake_minimum_required(VERSION 3.16)
project(sts2_fight CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

function(sts2_apply_warnings target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /utf-8 /W4 /permissive-)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
    endif()
endfunction()

# Future game sources (Card.cpp, Powers.cpp, etc.) go here.
add_library(sts2_game STATIC
    src/game/Rng.cpp
    src/game/Powers.cpp
    src/game/Damage.cpp
    src/game/Types.h
    src/game/Power.h
    src/game/Powers.h
    src/game/Damage.h
    src/game/Card.h
    src/game/Player.h
    src/game/Enemy.h
)
target_include_directories(sts2_game PUBLIC src)
target_compile_features(sts2_game PUBLIC cxx_std_17)
sts2_apply_warnings(sts2_game)

add_executable(sts2_fight src/main.cpp)
target_link_libraries(sts2_fight PRIVATE sts2_game)
sts2_apply_warnings(sts2_fight)

add_executable(sts2_fight_tests
    tests/main.cpp
    tests/test_runner.cpp
    tests/test_rng.cpp
    tests/test_types.cpp
    tests/test_powers.cpp
    tests/test_damage.cpp
)
target_link_libraries(sts2_fight_tests PRIVATE sts2_game)
sts2_apply_warnings(sts2_fight_tests)

enable_testing()
add_test(NAME unit COMMAND sts2_fight_tests)
