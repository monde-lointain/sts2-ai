#pragma once

#include <string>

// Algorithm-version manifest stamped onto every Q2 diagnostic / agreement row.
// Per Q2-ADR-005. Shape is the contract for S1; the real source-content SHA
// computation lands in S3+ (CMake build-step over canonicalized
// search.cc + transition.cc + state.h + Score + TT-config). For Phase-1A the
// manifest carries a fixed placeholder version-tag so adapter outputs remain
// stable while downstream consumers integrate.

namespace sts2::oracle::adapter {

struct AlgorithmManifest {
  std::string algorithm_sha;  // sha256 of canonicalized algo source (S3+).
  std::string build_sha;      // git commit sha of engine/cpp/ at build time.
  std::string version_tag;    // human-readable, e.g. "Q2-Phase-1A-...".

  bool operator==(const AlgorithmManifest&) const = default;
};

// S1 placeholder. Real SHA computation deferred to S3+ per Q2-ADR-005.
// The "algorithm_sha" string is a fixed marker that adapter outputs are
// produced by the Phase-1A stubbed manifest, distinguishable from any real
// SHA-256 hex string (32 bytes / 64 hex chars).
[[nodiscard]] inline AlgorithmManifest current_manifest() {
  return AlgorithmManifest{
      .algorithm_sha = "phase1a-stub-algorithm-sha",
      .build_sha = "phase1a-stub-build-sha",
      .version_tag = "Q2-Phase-1A-2026-05-12-001",
  };
}

}  // namespace sts2::oracle::adapter
