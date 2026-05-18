// Q2-ADR-005 algorithm-SHA source-file list (extend when adding semantically
// relevant source files; each addition rotates algorithm_sha per Q2-ADR-005):
//   engine/cpp/src/ai/search.cc
//   engine/cpp/src/ai/transition.cc
//   engine/cpp/include/sts2/ai/state.h
//   engine/cpp/include/sts2/game/damage_calc.h
//   engine/cpp/src/game/damage.cc                        -- wave-16
//   engine/cpp/src/game/monster_moves.cc                 -- wave-16
//   engine/cpp/src/oracle/adapter/louse_progenitor_projection.cc -- wave-18
//   engine/cpp/include/sts2/oracle/adapter/project_powers.h      -- wave-18
//   engine/cpp/src/ai/zobrist.cc                         -- wave-19 (new TU)
//   kZobristSeedLo / kZobristSeedHi (sts2/ai/zobrist.h)  -- wave-19 seeds
//   ABSL_VERSION_TAG_STR                                 -- wave-19 absl pin
//
// Real SHA-256 computation over canonicalized source content deferred to S3+
// (Q2-ADR-005). For Phase-1A the algorithm_sha is a fixed stub string in
// manifest.h; the real computation replaces it in S3+.

#include "sts2/oracle/adapter/manifest.h"

#include <cstdint>

// ---------------------------------------------------------------------------
// Wave-19 algorithm inputs — compile-time dependency declarations.
// ---------------------------------------------------------------------------

// Zobrist seeds (kZobristSeedLo, kZobristSeedHi) are algorithm inputs per
// Q2-ADR-010 §Seeds. This #include makes them a compile-time dependency of
// the oracle_adapter target: any change to zobrist.h's committed seeds will
// recompile this TU and can be caught by the static_asserts below.
#include "sts2/ai/zobrist.h"

// Abseil LTS pin is an algorithm input (Q2-ADR-011): different absl releases
// can change flat_hash_map hash-mixing, potentially altering solve
// trajectories. ABSL_VERSION_TAG_STR is exposed by
// engine/cpp/src/oracle/adapter/CMakeLists.txt via
// target_compile_definitions(sts2_oracle_adapter PRIVATE
//   ABSL_VERSION_TAG_STR="${ABSL_VERSION_TAG}") — set by B.1-γ CMake stream.
// A missing definition indicates a CMake propagation gap; coordinate with
// B.1-γ to add target_compile_definitions before building this TU.
#ifndef ABSL_VERSION_TAG_STR
#error \
    "ABSL_VERSION_TAG_STR must be defined; see engine/cpp/src/oracle/adapter/CMakeLists.txt"
#endif

// Verify committed seed constants match the values locked in Q2-ADR-010 §Seeds.
// These fire at compile time if the seeds change without a corresponding
// algorithm_sha rotation + ADR amendment. Intentional seed changes require:
//   1. Amend Q2-ADR-010 §Seeds with new values.
//   2. Update these static_asserts.
//   3. Re-run seed-pinner (D.1) to regenerate expected_values.h.
// NOLINTNEXTLINE(cppcoreguidelines-avoid-magic-numbers)
static_assert(sts2::ai::kZobristSeedLo == 0xC0FFEE12345678ULL,
              "kZobristSeedLo changed — rotate algorithm_sha + amend ADR-010");
// NOLINTNEXTLINE(cppcoreguidelines-avoid-magic-numbers)
static_assert(sts2::ai::kZobristSeedHi == 0xDEADBEEF20260517ULL,
              "kZobristSeedHi changed — rotate algorithm_sha + amend ADR-010");

namespace sts2::oracle::adapter {

// Anchor symbol so the TU is non-empty. The ABSL_VERSION_TAG_STR guard and
// seed static_asserts above are the primary deliverable of this TU: they make
// the absl version pin and Zobrist seed values explicit compile-time
// dependencies of the oracle_adapter library target, ensuring that any change
// to these algorithm inputs breaks the build and prompts an algorithm_sha
// rotation per Q2-ADR-005.
namespace {
[[maybe_unused]] const AlgorithmManifest kAnchorManifest = current_manifest();
}  // namespace

}  // namespace sts2::oracle::adapter
