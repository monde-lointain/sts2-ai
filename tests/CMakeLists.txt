# ------------------------------------------------------------------
# sts2_simulator_tests — single test executable
# ------------------------------------------------------------------
add_executable(sts2_simulator_tests)

target_sources(sts2_simulator_tests PRIVATE
    test_smoke.cc
    game/test_rng.cc
    game/test_powers.cc
    game/test_damage.cc
    game/test_cards.cc
    game/test_enemies.cc
    game/test_combat.cc
    render/test_bar.cc
    render/test_render_internal.cc
    render/test_render.cc
    render/test_console.cc
    input/test_input.cc
    input/test_input_internal.cc
    app/test_args.cc
    app/test_prompts.cc
    # headers — listed so IDEs surface them in the project view
    game/test_helpers.h
)

target_compile_features(sts2_simulator_tests PRIVATE cxx_std_20)

target_link_libraries(sts2_simulator_tests PRIVATE
    sts2::simulator
    GTest::gtest_main
    GTest::gmock
)

# Resolves `#include "tests/seeds/expected_values.h"` from test sources, plus
# `#include "render/render_internal.h"` etc. for internal-header tests.
target_include_directories(sts2_simulator_tests PRIVATE
    ${CMAKE_SOURCE_DIR}
    ${PROJECT_SOURCE_DIR}/src
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
