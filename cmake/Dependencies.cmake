include(FetchContent)

# ------------------------------------------------------------------
# GoogleTest 1.17.0 via FetchContent
# ------------------------------------------------------------------
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
# Abseil C++ LTS via FetchContent (Q2-ADR-011)
# ------------------------------------------------------------------
# Pinned to LTS 20260107.1. Bumps require Q2-ADR-011 amendment because
# absl::flat_hash_map iteration order / hash mixing changes can affect
# solve trajectories -> algorithm_sha rotates.
set(ABSL_VERSION_TAG "20260107.1" CACHE STRING "Abseil LTS version pin" FORCE)

# Quiet build noise + match project C++ standard. Avoid pulling absl tests.
set(ABSL_PROPAGATE_CXX_STD ON CACHE BOOL "" FORCE)
set(ABSL_ENABLE_INSTALL OFF CACHE BOOL "" FORCE)
set(ABSL_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(ABSL_USE_EXTERNAL_GOOGLETEST OFF CACHE BOOL "" FORCE)

FetchContent_Declare(
    absl
    GIT_REPOSITORY https://github.com/abseil/abseil-cpp.git
    GIT_TAG ${ABSL_VERSION_TAG}
)
FetchContent_MakeAvailable(absl)

# Sanity: surface configured absl version so build logs make the pin obvious.
message(STATUS "Abseil C++ LTS: ${ABSL_VERSION_TAG}")
