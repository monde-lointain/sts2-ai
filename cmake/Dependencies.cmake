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
