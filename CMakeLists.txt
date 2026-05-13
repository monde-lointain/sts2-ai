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

# No GNU extensions; standard required. Actual level set per-target.
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Force ON so IDE/clang-tidy always have compile_commands.json.
if(NOT CMAKE_EXPORT_COMPILE_COMMANDS)
    set(CMAKE_EXPORT_COMPILE_COMMANDS ON CACHE BOOL
        "Export compile_commands.json" FORCE)
endif()

# Unify executable output across single- and multi-config generators.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/$<CONFIG>")

option(STS2_BUILD_TESTS "Build the unit test executable" ON)
option(STS2_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)
option(STS2_BUILD_ORACLE_SINK
    "Build the Q2 oracle-agreement Parquet sink (Q2-ADR-004; pulls Apache Arrow + Parquet)"
    OFF)
option(ENABLE_SANITIZERS "Enable AddressSanitizer + UndefinedBehaviorSanitizer" OFF)

# CMake helper modules.
include(cmake/CompilerFlags.cmake)
include(cmake/StaticAnalysis.cmake)
include(cmake/Formatting.cmake)
include(cmake/Complexity.cmake)
include(cmake/Cloc.cmake)
include(cmake/CodeCoverage.cmake)

# Sanitizers must apply uniformly across all targets to avoid linker errors
# when some TUs are instrumented and others aren't.
if(ENABLE_SANITIZERS)
  if(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    add_compile_options(-fsanitize=address,undefined -fno-omit-frame-pointer)
    add_link_options(-fsanitize=address,undefined)
    message(STATUS "Sanitizers enabled")
  else()
    message(WARNING "Sanitizers only supported on GCC and Clang")
  endif()
endif()

if(STS2_BUILD_TESTS)
    include(cmake/Dependencies.cmake)
    enable_testing()
endif()

# Q2-ADR-004: oracle-agreement sink lives under sts2::oracle_agreement and pulls
# Apache Arrow + Parquet. Off by default — adapter / search / existing 424 tests
# do not depend on Arrow. Gate kept in the root so engine/cpp consumers see
# `Arrow::arrow_shared` / `Parquet::parquet_shared` imported targets at the
# point of use.
if(STS2_BUILD_ORACLE_SINK)
    find_package(Arrow REQUIRED)
    find_package(Parquet REQUIRED)
    message(STATUS "Q2 oracle-agreement sink ON — Arrow ${Arrow_VERSION}, Parquet ${Parquet_VERSION}")
endif()

add_subdirectory(engine/cpp)
