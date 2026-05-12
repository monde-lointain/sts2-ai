// S1-T0 placeholder TU. Manifest is header-only (see manifest.h); this file
// exists so sts2_oracle_adapter has at least one .cc source until S1-T1+
// adds the real reader/parser/projection sources. The static-lib target
// would otherwise build empty and trip linker warnings on some toolchains.

#include "sts2/oracle/adapter/manifest.h"

namespace sts2::oracle::adapter {

// Anchor symbol so the TU isn't empty. Touched at static-init only;
// no behavior. Named with the manifest namespace so dead-code analyzers
// can attribute it correctly.
namespace {
[[maybe_unused]] const AlgorithmManifest kAnchorManifest = current_manifest();
}  // namespace

}  // namespace sts2::oracle::adapter
