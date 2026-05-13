#pragma once

#include <filesystem>
#include <string>

// Registry-SHA computation. Q2-ADR-005 stamping discipline: every pinned
// regression row + every oracle-agreement row carries `registry_sha`. This
// header is the public utility surface; callers consume it from S2 stream-B
// (within-CULTISTS_NORMAL pin tests) and stream-D (tools/seed-pinner
// generalization).

namespace sts2::oracle::registry {

// SHA-256 of the file at `path`. Returns 64-char lowercase-hex string.
// Throws std::runtime_error on file-not-found or read error.
[[nodiscard]] std::string compute_registry_sha256(
    const std::filesystem::path& path);

// Convenience: SHA-256 of the Phase-1 registry at
// `contracts/registry/phase1-silent.json`. Path resolved at build time via
// the CMake-defined macro STS2_CONTRACTS_ROOT (absolute path of `contracts/`).
// Throws if the file is missing.
[[nodiscard]] std::string current_phase1_registry_sha256();

}  // namespace sts2::oracle::registry
