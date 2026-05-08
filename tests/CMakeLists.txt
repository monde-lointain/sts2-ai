# ------------------------------------------------------------------
# GoogleTest 1.17.0 via FetchContent
# ------------------------------------------------------------------
include(FetchContent)
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG        v1.17.0
)

# gmock ON: Part 2 §11 renderer tests use HasSubstr matchers.
set(BUILD_GMOCK    ON  CACHE BOOL "" FORCE)
set(INSTALL_GTEST  OFF CACHE BOOL "" FORCE)
# Match MSVC dynamic CRT to avoid runtime mismatch with the rest of the project.
if(WIN32)
    set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
endif()

FetchContent_MakeAvailable(googletest)

include(GoogleTest)

# ------------------------------------------------------------------
# sts2_simulator_tests — single test executable
# ------------------------------------------------------------------
add_executable(sts2_simulator_tests)

target_sources(sts2_simulator_tests PRIVATE
    test_smoke.cpp
    game/test_rng.cpp
    game/test_powers.cpp
    game/test_damage.cpp
    game/test_cards.cpp
    game/test_enemies.cpp
    game/test_combat.cpp
    # headers — listed so IDEs surface them in the project view
    game/test_helpers.h
)

target_compile_features(sts2_simulator_tests PRIVATE cxx_std_20)

target_link_libraries(sts2_simulator_tests PRIVATE
    sts2::simulator
    GTest::gtest_main
)

# Resolves `#include "tests/seeds/expected_values.h"` from test sources.
target_include_directories(sts2_simulator_tests PRIVATE
    ${CMAKE_SOURCE_DIR}
)

# Apply project warnings, then disable warnings-as-errors for tests only:
# GoogleTest's headers can trip /W4 + /WX on MSVC.
sts2_apply_warnings(sts2_simulator_tests)
if(MSVC)
    target_compile_options(sts2_simulator_tests PRIVATE /WX-)
else()
    target_compile_options(sts2_simulator_tests PRIVATE -Wno-error)
endif()

gtest_discover_tests(sts2_simulator_tests)
