# Standalone CMake script invoked via `cmake -P` from add_custom_command.
# Computes algorithm_sha at build time when any source-list file changes.
#
# Inputs (passed via -D on cmake -P invocation):
#   ALGORITHM_SHA_SOURCES  — semicolon-separated absolute paths to source files
#   ABSL_VERSION_TAG       — absl LTS pin string (e.g. "20260107.1")
#   TEMPLATE_HEADER        — path to manifest_constants.h.in
#   OUTPUT_HEADER          — target path for the generated header
#
# Writes: OUTPUT_HEADER with @ALGORITHM_SHA_HEX@ substituted.
# Q2-ADR-005: canonical source list + seeds + absl version → SHA-256.

# Q2-ADR-010 seeds — must match zobrist.h kZobristSeedLo/kZobristSeedHi.
set(K_ZOBRIST_SEED_LO "0xC0FFEE12345678ULL")
set(K_ZOBRIST_SEED_HI "0xDEADBEEF20260517ULL")

set(MANIFEST_BLOB "")
foreach(SRC IN LISTS ALGORITHM_SHA_SOURCES)
    if(NOT EXISTS "${SRC}")
        message(FATAL_ERROR "algorithm_sha: source file missing at build time: ${SRC}")
    endif()
    file(SHA256 "${SRC}" FILE_HASH)
    string(APPEND MANIFEST_BLOB "${SRC}\n${FILE_HASH}\n")
endforeach()
string(APPEND MANIFEST_BLOB "kZobristSeedLo=${K_ZOBRIST_SEED_LO}\n")
string(APPEND MANIFEST_BLOB "kZobristSeedHi=${K_ZOBRIST_SEED_HI}\n")
string(APPEND MANIFEST_BLOB "ABSL_VERSION_TAG=${ABSL_VERSION_TAG}\n")

string(SHA256 ALGORITHM_SHA_HEX "${MANIFEST_BLOB}")

configure_file("${TEMPLATE_HEADER}" "${OUTPUT_HEADER}" @ONLY)
message(STATUS "Computed algorithm_sha=${ALGORITHM_SHA_HEX}")
