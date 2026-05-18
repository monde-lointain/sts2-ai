# cmake/AlgorithmSha.cmake — build-time algorithm_sha computation (Q2-ADR-005).
# Included from engine/cpp/CMakeLists.txt after sts2_oracle_adapter is defined.
# Creates:
#   algorithm_sha_header  (custom target)
#   sts2_algorithm_sha_header (INTERFACE library; consumers link this for the
#                              generated manifest_constants.h include path)

# Canonical algorithm-SHA source list (sorted, per Q2-ADR-005 amendment wave-20.β).
# Extend here when adding semantically relevant source files; each addition rotates
# algorithm_sha per Q2-ADR-005. Excluded: manifest.cc (circular), CMake files
# (build-system not algorithm), tests (not runtime behavior).
set(ALGORITHM_SHA_SOURCES
    engine/cpp/include/sts2/ai/chance.h
    engine/cpp/include/sts2/ai/search.h
    engine/cpp/include/sts2/ai/state.h
    engine/cpp/include/sts2/ai/zobrist.h
    engine/cpp/include/sts2/game/damage_calc.h
    engine/cpp/include/sts2/oracle/adapter/project_powers.h
    engine/cpp/src/ai/chance.cc
    engine/cpp/src/ai/recommend.cc
    engine/cpp/src/ai/search.cc
    engine/cpp/src/ai/transition.cc
    engine/cpp/src/ai/zobrist.cc
    engine/cpp/src/game/card_effects.cc
    engine/cpp/src/game/damage.cc
    engine/cpp/src/game/monster_moves.cc
    engine/cpp/src/oracle/adapter/cultists_projection.cc
    engine/cpp/src/oracle/adapter/louse_progenitor_projection.cc
)
# list(SORT ...) not needed: already sorted lexicographically above for determinism.

# Resolve to absolute paths so add_custom_command DEPENDS works portably.
# Only include files that exist at configure time; future files (e.g.
# card_effects.cc not yet added) are skipped with a status message so the
# build graph is valid. When the file is later added it will appear in
# ALGORITHM_SHA_SOURCES and the configure step will pick it up automatically.
set(ALGORITHM_SHA_SOURCE_PATHS "")
foreach(REL IN LISTS ALGORITHM_SHA_SOURCES)
    set(_ABS "${CMAKE_SOURCE_DIR}/${REL}")
    if(EXISTS "${_ABS}")
        list(APPEND ALGORITHM_SHA_SOURCE_PATHS "${_ABS}")
    else()
        message(STATUS "algorithm_sha: source not yet present, skipping: ${REL}")
    endif()
endforeach()

# Fall back to empty string when absl is not fetched (non-test builds).
if(NOT DEFINED ABSL_VERSION_TAG)
    set(ABSL_VERSION_TAG "")
endif()

set(MANIFEST_CONSTANTS_HEADER "${CMAKE_BINARY_DIR}/generated/manifest_constants.h")
set(MANIFEST_CONSTANTS_TEMPLATE
    "${CMAKE_SOURCE_DIR}/engine/cpp/include/sts2/oracle/adapter/manifest_constants.h.in")

add_custom_command(
    OUTPUT  "${MANIFEST_CONSTANTS_HEADER}"
    DEPENDS ${ALGORITHM_SHA_SOURCE_PATHS}
            "${CMAKE_SOURCE_DIR}/cmake/AlgorithmShaCompute.cmake"
            "${MANIFEST_CONSTANTS_TEMPLATE}"
    COMMAND "${CMAKE_COMMAND}"
            "-DALGORITHM_SHA_SOURCES=${ALGORITHM_SHA_SOURCE_PATHS}"
            "-DABSL_VERSION_TAG=${ABSL_VERSION_TAG}"
            "-DTEMPLATE_HEADER=${MANIFEST_CONSTANTS_TEMPLATE}"
            "-DOUTPUT_HEADER=${MANIFEST_CONSTANTS_HEADER}"
            -P "${CMAKE_SOURCE_DIR}/cmake/AlgorithmShaCompute.cmake"
    COMMENT "Computing algorithm_sha (Q2-ADR-005)"
    VERBATIM
)
add_custom_target(algorithm_sha_header DEPENDS "${MANIFEST_CONSTANTS_HEADER}")

# INTERFACE library so consumers get the generated include path via
# target_link_libraries rather than manual target_include_directories.
add_library(sts2_algorithm_sha_header INTERFACE)
add_library(sts2::algorithm_sha_header ALIAS sts2_algorithm_sha_header)
target_include_directories(sts2_algorithm_sha_header
    INTERFACE "${CMAKE_BINARY_DIR}/generated")
add_dependencies(sts2_algorithm_sha_header algorithm_sha_header)
