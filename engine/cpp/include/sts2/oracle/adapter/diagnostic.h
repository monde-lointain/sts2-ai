#pragma once

#include <optional>
#include <string>
#include <vector>

#include "sts2/oracle/adapter/manifest.h"

// Diagnostic types emitted by the adapter on the reject-with-diagnostic
// path (Q2-ADR-002) and the unknown-power-reference path (Q2-ADR-005).
//
// These are first-class adapter outcomes, not exceptions: the Q2 verifier
// emits "I cannot verify this" signal exactly via these structs. Every
// diagnostic carries the AlgorithmManifest stamp (per Q2-ADR-005) so
// downstream consumers (Q10, Q12) can correlate signals to a specific
// (algorithm, registry, simulator) tuple.

namespace sts2::oracle::adapter {

struct UnsupportedEncounter {
  // M1 trailer SHA-256 (64 lowercase-hex chars). Q1 canonical hash anchor.
  std::string blob_canonical_hash;
  // Best-effort encounter name (e.g. "FossilStalkerElite"). "<unknown>"
  // when no entry in the spawn-power expectation map matches the snapshot.
  std::string encounter_id;
  // Wire-emitted monster Creature.Name strings (in slot order).
  std::vector<std::string> monster_ids;
  // Machine-readable rejection reason. Phase-1A vocabulary:
  //   "encounter_not_in_cpp_engine"  — non-CULTISTS_NORMAL encounter
  // Future reasons (forward-laid): "n_not_2", "registry_sha_mismatch",
  // "malformed_blob", "budget_exceeded".
  std::string reason;
  AlgorithmManifest manifest;
};

struct UnknownPowerDiagnostic {
  std::string blob_canonical_hash;
  std::string encounter_id;
  // Source-declared spawn-time PowerInstance.ModelId strings that the
  // encounter's content class registers but that are ABSENT from the
  // wire snapshot's PowerInstance.ModelId set. Sorted ascending for
  // deterministic comparison.
  std::vector<std::string> source_declared_power_ids_absent_from_snapshot;
  // Q1's git sha at blob-emit time, echoed from ManifestStamp.GitSha so
  // downstream consumers can attribute the divergence to a Q1 build.
  std::string source_simulator_build_sha;
  AlgorithmManifest manifest;
};

}  // namespace sts2::oracle::adapter
