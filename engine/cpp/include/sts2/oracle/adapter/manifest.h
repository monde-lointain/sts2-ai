#pragma once

#include <string>

// manifest_constants.h is generated at CMake build time by
// cmake/AlgorithmShaCompute.cmake (Q2-ADR-005 wave-20.β).
#include "manifest_constants.h"

// Algorithm-version manifest stamped onto every Q2 diagnostic / agreement row.
// Per Q2-ADR-005. algorithm_sha is a CMake-computed SHA-256 over the canonical
// algorithm-input source list (declared in cmake/AlgorithmSha.cmake).
// build_sha and version_tag remain stubs pending future fixup (out of scope).

namespace sts2::oracle::adapter {

struct AlgorithmManifest {
  std::string
      algorithm_sha;        // sha256 of canonicalized algo source (Q2-ADR-005).
  std::string build_sha;    // git commit sha of engine/cpp/ at build time.
  std::string version_tag;  // human-readable, e.g. "Q2-Phase-1A-...".

  bool operator==(const AlgorithmManifest&) const = default;
};

// Returns the current algorithm manifest. algorithm_sha is a real 64-char
// hex SHA-256, recomputed by CMake whenever any file in ALGORITHM_SHA_SOURCES
// changes (cmake/AlgorithmSha.cmake). build_sha and version_tag are stubs.
[[nodiscard]] inline AlgorithmManifest current_manifest() {
  return AlgorithmManifest{
      .algorithm_sha =
          std::string{sts2::oracle::adapter::generated::kAlgorithmSha},
      .build_sha = "phase1a-stub-build-sha",
      .version_tag = "Q2-Phase-1A-2026-05-12-001",
  };
}

}  // namespace sts2::oracle::adapter
