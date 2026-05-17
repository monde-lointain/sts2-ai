// S1-T0 placeholder TU. Manifest is header-only (see manifest.h); this file
// exists so sts2_oracle_adapter has at least one .cc source until S1-T1+
// adds the real reader/parser/projection sources. The static-lib target
// would otherwise build empty and trip linker warnings on some toolchains.
//
// Q2-ADR-005 algorithm-SHA source-file list (populated when S3+ SHA computation
// lands; extend this list when adding semantically relevant source files):
//   engine/cpp/src/ai/search.cc
//   engine/cpp/src/ai/transition.cc
//   engine/cpp/include/sts2/ai/state.h
//   engine/cpp/include/sts2/game/damage_calc.h
//   engine/cpp/src/game/damage.cc                        -- wave-16
//   engine/cpp/src/game/monster_moves.cc                 -- wave-16
//   engine/cpp/src/oracle/adapter/louse_progenitor_projection.cc -- wave-18
//   engine/cpp/include/sts2/oracle/adapter/project_powers.h      -- wave-18

#include "sts2/oracle/adapter/manifest.h"

namespace sts2::oracle::adapter {

// Anchor symbol so the TU isn't empty. Touched at static-init only;
// no behavior. Named with the manifest namespace so dead-code analyzers
// can attribute it correctly.
namespace {
[[maybe_unused]] const AlgorithmManifest kAnchorManifest = current_manifest();
}  // namespace

}  // namespace sts2::oracle::adapter
