cmake_minimum_required(VERSION 3.28)

# Forbid in-source builds. Must run before project().
if(CMAKE_SOURCE_DIR STREQUAL CMAKE_BINARY_DIR)
    message(FATAL_ERROR
        "In-source builds are forbidden. Configure with `cmake -S . -B build` "
        "or use a CMakePresets.json preset.")
endif()

project(sts2-ai
    VERSION 0.1.0
    DESCRIPTION "Deterministic CLI simulator of one STS2 fight"
    LANGUAGES CXX
)

# Default Debug for single-config generators when unset; multi-config picks at build time.
get_property(_sts2_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
if(NOT _sts2_multi_config AND NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Debug CACHE STRING "Build type" FORCE)
    set_property(CACHE CMAKE_BUILD_TYPE PROPERTY STRINGS
        Debug Release RelWithDebInfo MinSizeRel)
endif()

# No GNU extensions; standard required. Actual level is set per-target via target_compile_features.
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Force ON so IDE/clang-tidy always have compile_commands.json (Ninja/Makefile generators init this as empty cache).
if(NOT CMAKE_EXPORT_COMPILE_COMMANDS)
    set(CMAKE_EXPORT_COMPILE_COMMANDS ON CACHE BOOL
        "Export compile_commands.json" FORCE)
endif()

# Unify executable output across single- and multi-config generators.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/$<CONFIG>")

option(STS2_BUILD_TESTS "Build the unit test executable" ON)
option(STS2_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)

# Per-target warnings. Apply with sts2_apply_warnings(<target>).
function(sts2_apply_warnings target)
    if(MSVC)
        target_compile_options(${target} PRIVATE
            /W4 /permissive- /utf-8 /Zc:__cplusplus
        )
        if(STS2_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE
            -Wall -Wextra -Wpedantic
        )
        if(STS2_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()

# ------------------------------------------------------------------
# sts2::simulator — combined game/render/input static library
# ------------------------------------------------------------------
add_library(sts2_simulator STATIC)
add_library(sts2::simulator ALIAS sts2_simulator)

target_sources(sts2_simulator PRIVATE
    # game module
    src/game/Rng.cpp
    src/game/Powers.cpp
    src/game/Damage.cpp
    src/game/Cards.cpp
    src/game/Enemies.cpp
    src/game/Combat.cpp
    # render module
    src/render/Bar.cpp
    src/render/Console.cpp
    src/render/Render.cpp
    # input module
    src/input/Input.cpp
    # headers — listed so IDEs surface them in the project view
    src/game/Types.h
    src/game/Rng.h
    src/game/Power.h
    src/game/Powers.h
    src/game/Damage.h
    src/game/Card.h
    src/game/Cards.h
    src/game/Vitals.h
    src/game/Player.h
    src/game/Enemy.h
    src/game/Enemies.h
    src/game/Combat.h
    src/render/Ansi.h
    src/render/Bar.h
    src/render/Console.h
    src/render/Glyphs.h
    src/render/Render.h
    src/render/Render_internal.h
    src/input/Input.h
)

target_compile_features(sts2_simulator PUBLIC cxx_std_20)

target_include_directories(sts2_simulator PUBLIC
    $<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/src>
)

sts2_apply_warnings(sts2_simulator)

# ------------------------------------------------------------------
# sts2_fight — fight simulator executable
# ------------------------------------------------------------------
add_executable(sts2_fight src/main.cpp)
target_link_libraries(sts2_fight PRIVATE sts2::simulator)
sts2_apply_warnings(sts2_fight)

# ------------------------------------------------------------------
# Tests
# ------------------------------------------------------------------
if(STS2_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
    # Seed pinner is a manual regenerator for tests/seeds/expected_values.h;
    # not run automatically. Gated on STS2_BUILD_TESTS so production builds
    # don't pull it in.
    add_subdirectory(tools/seed-pinner)
endif()
