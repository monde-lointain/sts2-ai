# 01 — Architectural Decision Log: Q2 Internals

ADRs that shape Q2 internals. **Subordinate** to pipeline-level ADRs at
`~/development/projects/cpp/sts2-ai/docs/specs/01-decisions-log.md`. Where
a pipeline ADR constrains Q2 internals, this log cross-references rather
than restates.

Each entry: Title, Status, Context, Decision, Consequences (negatives first,
then positives).

| # | Title | Status |
|---|---|---|
| Q2-ADR-001 | Adapter location, namespace, CMake target, wire-parse strategy | Accepted |
| Q2-ADR-002 | Phase-1A adapter encounter scope = CULTISTS_NORMAL only | Accepted (re-surface candidate) |
| Q2-ADR-003 | Verify-RPC transport = JSON-over-Unix-socket | Accepted |
| Q2-ADR-004 | Oracle-agreement sink = Parquet on local filesystem | Accepted |
| Q2-ADR-005 | Algorithm-version manifest stamping + unknown-power diagnostic | Accepted |

---

## Q2-ADR-001 — Adapter location, namespace, CMake target, wire-parse strategy

**Status:** Accepted.

**Context.** Where the engine→CompactState adapter (pipeline ADR-011) lives
in the existing C++ tree, what it links against, and how it parses the Q1→Q2
wire.

The existing tree exposes one ALIAS target `sts2::simulator` covering all
`sts2::game`, `sts2::ai`, `sts2::render`, `sts2::input`, `sts2::app` code.
Folding adapter code into `sts2::ai::*` conflates verifier-internals with the
cross-quantum wire boundary. The adapter deserializes Q1's binary blob and
emits Q2's internal `CompactState` — distinct concern.

`contracts/generated/cpp/game-simulator/{state_blob,hook}_pb.h` are stub-only
(empty struct declarations); adapter cannot link against them. Lead's
Unresolved #2 confirmed *negatively*.

**Decision.**

1. **Namespace:** `sts2::oracle::adapter::*`. Sibling to `sts2::ai`, not
   nested in it.
2. **Source location:** `engine/cpp/src/oracle/adapter/`; public headers
   under `engine/cpp/include/sts2/oracle/adapter/`.
3. **CMake target:** new ALIAS target `sts2::oracle_adapter` (static lib).
   Depends on `sts2::simulator` (consumes `CompactState`, `EnemyState`,
   `CardCounts`). The future verify-server (S4) and adapter tests link
   `sts2::oracle_adapter`. Existing `sts2_simulator_tests.exe` unchanged.
4. **Wire-parse strategy:** hand-roll, no protobuf runtime dependency.
   - `StateBlobEnvelope` proto3: 7 fields, no nested messages. Hand-coded
     varint + length-prefix reader, ~50 LOC.
   - M1 binary payload format: fully documented in
     `engine/headless/docs/specs/modules/state-codec.md`. Hand-coded reader
     matches the C# `StateCodec` wire layout. Trailer SHA-256 re-verified
     on every read.
   - If/when `contracts/generated/cpp/` ships real codegen, migration is
     mechanical and ADR-revisited. `sts2::oracle::adapter` is the seam.

**Consequences.**

- *Negative:* Two parsers for `StateBlobEnvelope` exist (C# via
  `Google.Protobuf` codegen; C++ hand-roll). Schema-drift risk if v0.1 → v0.2
  ships without both sides updated. Mitigation: minor-bumps are coordinated
  per Q1-ADR-005; D3 fixture canonical-hash regression catches drift loud.
- *Negative:* Hand-rolled proto3 parse is more error-prone than generated
  code. Mitigation: 7-field flat shape is the lowest-complexity slice of
  proto3; fixture round-trip CI gate is the safety net.
- *Negative:* New top-level `sts2::oracle` namespace alongside `sts2::ai` —
  minor disambiguation cost in code review.
- *Positive:* Adapter is self-contained; `sts2::ai` and the existing 424-test
  battery untouched.
- *Positive:* No protobuf runtime in C++ build — build stays lean.
- *Positive:* Forward-compatible with future generated bindings.

---

## Q2-ADR-002 — Phase-1A adapter encounter scope = CULTISTS_NORMAL only

**Status:** Accepted (re-surface candidate to project lead).

**Context.** Lead's S1 framing in the boot directive expects "each of 6 D3
fixtures through adapter → existing expectimax → optimal-action value matches
a per-fixture pinned expected value."

The C++ substrate cannot satisfy this for 5 of 6 fixtures. Scaling-strategy
§1.3 already enumerates this as a known boundary:

| Substrate dependency | Where pinned | Consequence |
|---|---|---|
| `EnemyState` is a fixed POD with cultist-specific fields | `state.h:66-78` | non-cultist enemies have no struct shape |
| `enemies` array hardcoded `N=2` | `state.h:94` | N=1 (FossilStalker, Louse) and any future N≥3 (BowlbugsTrio post-Phase-1.5) unsupported |
| `CardCounts` packed 8-bit ≤8 distinct kinds | `state.h:23-26` | future deck expansion blocked at 8 kinds |
| Engine logic: cultist-only | `game::enemies`, `transition.cc` | even if state-shape supported FossilStalker, no transition function exists |

Lead's queue item #2 framing (arity N≠2 reject) is a strict *narrow* subset of
this scope: fixture #4 KaiserCrabBoss is N=2 but still encounter-specific
(Crusher + Rocket, not Calcified + Damp Cultist). Honest framing is by
encounter, not arity.

D3 fixture round-trip feasibility:

| Fixture | Encounter | Engine mechanics in `engine/cpp/`? |
|---|---|---|
| 1 | CultistsNormal | YES — round-trip path |
| 2,3 | FossilStalkerElite | NO |
| 4 | KaiserCrabBoss | NO (engine + unknown-power IDs per fixture README) |
| 5 | LouseProgenitorNormal | NO |
| 6 | SmallSlimes | NO (also B.1-ε DEFER per fixture README) |

**Decision.** Phase-1A adapter encounter scope = CULTISTS_NORMAL only.

- Adapter inspects the deserialized blob's encounter signature (monster IDs
  from the M1 CombatState section). Accepts CULTISTS_NORMAL — `(Calcified
  Cultist, Damp Cultist)` pair — and produces a `CompactState`. Rejects all
  other encounter signatures.
- Reject path emits `sts2::oracle::adapter::UnsupportedEncounter` as a
  first-class adapter outcome (not an error). Diagnostic shape:
  `{ encounter_id, monster_ids, reason: "encounter_not_in_cpp_engine" }`.
  Caller branches on the result type.
- Oracle-agreement signal (S3) for these states is "not-verified" — exactly
  the tractability-frontier framing in `oracle.md` Open Risks.
- Adapter API (sketch): `std::variant<CompactState, UnsupportedEncounter>
  from_blob(std::span<const uint8_t>);` or equivalent typed-result.

Extension paths reserved for later (out of Phase-1A):

- **Path A:** expand `engine/cpp/` to cover FossilStalker, KaiserCrab, Louse,
  Slime engine mechanics + polymorphic `EnemyState`. Largest scope; reopens
  pipeline ADR-004 representation contract.
- **Path B (this ADR):** defer non-cultist verification. Phase-1A scope.
- **Path C:** Q2 calls Q1's C# engine for transition; never expands C++.
  Loses TT/expectimax speed for non-cultist states; rejected for Phase-1A.

ADR revisited when (a) lead directs Path A as Q2 scope expansion, or
(b) Q1 Phase-1.5 lands additional Q1-side encounter mechanics and Path A
becomes a viable Q2 follow-up.

**Consequences.**

- *Negative:* 5 of 6 D3 fixtures don't exercise the round-trip path; only
  the reject-with-diagnostic path. Lead's S1 validation surface is reduced.
- *Negative:* Oracle-agreement coverage for Phase-1A is CULTISTS_NORMAL
  only. Phase-1.5+ encounter coverage requires engine expansion.
- *Negative:* Cross-quantum signal: when Q10 boots, the trainer will see
  "not-verified" labels for most Phase-1.5 encounters. Q10's prioritization
  must weight non-verified states by uncertainty rather than skipping —
  Q10's design call, not Q2's.
- *Positive:* Honest about substrate. No silent CompactState fabrication
  for encounters with no engine mechanics.
- *Positive:* Reject-with-diagnostic *is* the verifier's "I cannot verify
  this" signal — exactly what `oracle.md` describes.
- *Positive:* Phase-1A delivers a working adapter on real D3 fixture bytes
  with a value floor higher than zero, even on a narrow scope.

**Re-surface trigger.** If lead directs Path A (expand C++ engine into Q2
scope) before S1 begins, S1/S2 wall-clocks grow substantially and ADR-revisits
open (pipeline ADR-004 representation reopen).

---

## Q2-ADR-003 — Verify-RPC transport = JSON-over-Unix-socket

**Status:** Accepted.

**Context.** `oracle.md` Communication: `verify(state_blob) → {value, action,
expansion_complete}` cold-path RPC consumed by Q12. Q12 not booted yet;
RPC is forward-laid.

Transport options:

- (a) JSON-over-Unix-socket. Local-only, no schema-gen tooling, mirrors
  Q1 M4 control-plane pattern.
- (b) gRPC. Polyglot schema-gen; adds `protoc` + `grpc-cpp` runtime
  dependency.
- (c) Shared memory. Hot-path-grade; verify is cold-path — overkill.

Lead's directive default: (a).

**Decision.** JSON-over-Unix-socket. Server: `engine/cpp/tools/oracle-verify-server/`.
Default socket `/tmp/sts2-q2-verify.sock`, env-overridable. Every payload
carries `protocol_version: "1"`.

Request:

```json
{
  "protocol_version": "1",
  "state_blob_b64": "<base64 of StateBlobEnvelope bytes>",
  "budget": { "max_states_expanded": 100000, "deadline_ms": 1000 }
}
```

Response (success):

```json
{
  "protocol_version": "1",
  "verified": true,
  "value": { "expected_hp": 53.4, "expected_rounds": 6.0 },
  "action": { "kind": "play_card", "card_id": "Strike", "target_idx": 0 },
  "expansion_complete": true,
  "states_expanded": 4321,
  "algorithm_sha": "...",
  "simulator_manifest_echo": {
    "schema_major": 0, "schema_minor": 1,
    "game_version": "...", "simulator_build_sha": "...",
    "registry_sha": "..."
  }
}
```

Response (not-verified):

```json
{
  "protocol_version": "1",
  "verified": false,
  "reason": "encounter_not_in_cpp_engine | budget_exceeded | malformed_blob | unknown_power_diagnostic",
  "diagnostic": { ... }
}
```

5-tuple manifest echo lets Q12 correlate verify-rows back to the producing
Q1 build.

**Consequences.**

- *Negative:* JSON verbose; base64 of the state-blob adds ~33% payload
  size. Negligible for cold path.
- *Negative:* No cross-language schema-gen. Q12 (likely Python) hand-rolls
  the matching client shape. Mitigation: `protocol_version` field gates
  evolution.
- *Negative:* Unix-socket binds verify-server and Q12 to the same host.
  Phase-1A deployment model is single-host; future cross-host verify would
  need a TCP wrap. ADR-revisit reserved.
- *Positive:* No new toolchain dependency in C++ build.
- *Positive:* Mirrors Q1 M4 control-plane pattern; engineers familiar with
  one are familiar with both.
- *Positive:* JSON payloads are human-readable in logs — debuggable.

---

## Q2-ADR-004 — Oracle-agreement sink = Parquet on local filesystem

**Status:** Accepted. **Schema FROZEN as of S3 ship (2026-05-12).**

**Schema freeze.** The 15-column row schema below is now load-bearing. Two
evolution regimes apply:

- *Pre-Q10-boot (current).* Any schema change is a Q2-internal ADR-bump
  event — bump the ADR Status to "Accepted (revised)", add an entry to the
  Phase-1A schema-history table below, regen `data/oracle/agreement/`
  partitions if the change is non-additive.
- *Post-Q10-boot.* The schema becomes a cross-quantum contract (Q10 is the
  consumer). Any schema change is then a coordination event per ADR-001
  (versioned schema migrations). Promotion to
  `contracts/schemas/oracle-agreement/` lands at Q10-boot.

Schema history (Phase-1A):

| Version | Date | Change | Driver |
|---|---|---|---|
| v0 | 2026-05-12 | Initial 15-column schema (this ADR) | S3 land |


**Context.** `oracle.md` Communication: oracle-agreement rows pushed to a
Q3 sideband or dedicated table consumable by Q10. Neither Q3 nor Q10 booted.

Lead's directive default: (a) Parquet on local filesystem, Q3 ingest as
future consumer. Lead's Unresolved #3: `data/oracle/agreement/` per monorepo
`data/<service>/` convention. Confirmed — `data/` exists at project root
with seven per-service subdirs; `data/oracle/` is open.

**Decision.** Parquet rows under `data/oracle/agreement/`. Partition layout:
`data/oracle/agreement/year=YYYY/month=MM/day=DD/model=<sha>.parquet`.
Append-only files; one file per `(model_version, day)`.

Row schema (Apache Arrow / Parquet types):

| Column | Type | Notes |
|---|---|---|
| `state_hash` | `string` | M1 trailer SHA-256 (64 hex chars) |
| `oracle_action_json` | `string` | serialized `transition::Action` |
| `oracle_value_hp` | `double` | `Score.expected_hp` |
| `oracle_value_rounds` | `double` | `Score.expected_rounds` |
| `model_action_json` | `string` | serialized model proposal |
| `model_value_hp` | `double` | model expected_hp; NaN if model emits run-value only |
| `model_value_rounds` | `double` | NaN if not emitted |
| `model_version` | `string` | Q5 artifact sha |
| `algorithm_sha` | `string` | Q2 algorithm manifest sha (Q2-ADR-005) |
| `registry_sha` | `string` | Q4 token-registry sha echoed from input blob |
| `simulator_build_sha` | `string` | Q1 build sha echoed from input blob |
| `expansion_complete` | `bool` | true iff oracle fully expanded the state |
| `unsupported_reason` | `string` | non-empty iff `verified=false`; matches RPC `reason` |
| `q1_divergence_diagnostic_json` | `string` | optional; populated when Q2-ADR-005 unknown-power diagnostic fires |
| `timestamp_ms` | `int64` | epoch millis of verify call |

C++ writer: `engine/cpp/src/oracle/agreement/sink.cc`, wraps Apache Arrow's
Parquet writer. Library dependency (`libarrow`, `libparquet`) added to the
verify-server target only — not to the core `sts2::simulator` library.

**Consequences.**

- *Negative:* Adds Apache Arrow C++ dependency to the verify-server build.
  Non-trivial in CI environments without prebuilt Arrow. Mitigation: gate
  the Arrow dependency under a CMake option `STS2_BUILD_ORACLE_SINK=ON`;
  unit tests for adapter / search do not pull Arrow.
- *Negative:* Local-FS-only Phase-1A. Q3 ingest is the future story; if
  Q3 schema preferences differ, schema-versioning via Parquet column-name
  reads tolerates additive changes only (structural changes are cross-quantum
  ADR-001 events).
- *Negative:* `oracle-agreement schema` is a *new* cross-quantum contract.
  Once Q10 consumes it, the schema is load-bearing. Mitigation: live the
  schema as ADR-004 here for Phase-1A; promote to
  `contracts/schemas/oracle-agreement/` (proto) when Q10 boots and the
  contract solidifies.
- *Positive:* Parquet is standard; many consumers (pandas, Spark, DuckDB)
  read it without Q2-specific tooling.
- *Positive:* `(model_version, day)` partitioning gives natural row-group
  locality for Q10 prioritized-replay sampling.
- *Positive:* `data/oracle/agreement/` follows monorepo `data/<service>/`
  convention.

---

## Q2-ADR-005 — Algorithm-version manifest stamping + unknown-power diagnostic

**Status:** Accepted.

**Context.** `oracle.md` Data Ownership: "Expectimax algorithm version
manifest — algorithm SHA, transposition table parameters, scoring rule
version. Stamped on every report row." Open Risks: algorithm-version
drift in report rows.

Lead's queue items #5 (where does the manifest live?) and #6 (unknown-power
diagnostic for KaiserCrabBoss spawn-power IDs absent from Phase-1 catalog).
Fold: manifest is the data structure; unknown-power diagnostic is one event
kind the manifest stamps. They share the same stamping discipline.

**Decision.**

### Manifest contents

```cpp
namespace sts2::oracle {
struct AlgorithmManifest {
  std::string algorithm_sha;       // sha256 of canonicalized search.cc + transition.cc + state.h
  std::string tt_parameters_sha;   // sha256 of TT config (capacity, eviction policy)
  std::string scoring_rule_sha;    // sha256 of Score struct + better_than() impl
  std::string build_sha;           // git commit sha of engine/cpp/ at build time
  std::string version_tag;         // human-readable, e.g. "Q2-Phase-1A-2026-05-12-001"
};
}  // namespace sts2::oracle
```

Computed at build time; embedded as a compiled-in constant. CI gate: if any
of (search.cc, transition.cc, state.h, Score, TT config) change without an
intentional manifest re-derive, regen fails loud.

### Stamping discipline

- Every oracle-agreement row (Q2-ADR-004) carries `algorithm_sha`.
- Every pinned regression row (S2) carries `algorithm_sha`.
- `tools/seed-pinner` gains `--manifest` flag emitting the full
  `AlgorithmManifest` JSON to stderr alongside the pinned header.
- Verify-server (Q2-ADR-003) embeds `algorithm_sha` in every response.

### Algorithm-change → regression rebuild

Algorithm SHA change triggers a regression-set rebuild (per `oracle.md`).
`expected_values.h` is re-generated under the new SHA; old pinned rows are
not deleted but flagged `algorithm_sha={old}` and quarantined from current
oracle-agreement signal. Q10 (when booted) filters by current
`algorithm_sha`.

### Unknown-power diagnostic (folds queue item #6)

When the adapter decodes a CompactState payload from an encounter whose
source-declared spawn-powers (resolvable by cross-referencing the encounter
id against `contracts/registry/phase1-silent.json`) include power IDs
absent from the snapshot's `PowerInstance.ModelId` set, the adapter emits:

```cpp
namespace sts2::oracle::adapter {
struct UnknownPowerDiagnostic {
  std::string blob_canonical_hash;       // M1 trailer hash
  std::vector<std::string> source_declared_power_ids_absent_from_snapshot;
  std::string encounter_id;
  std::string source_simulator_build_sha;
};
}  // namespace sts2::oracle::adapter
```

D3 fixture #4 (KaiserCrabBoss) exercises this: source declares
`BackAttackLeftPower`, `BackAttackRightPower`, `CrabRagePower`,
`SurroundedPower` for Crusher / Rocket spawns; the blob's
`PowerInstance.ModelId` set is empty (Q1 silent-drops at boot per fixture
README). Adapter logs the diagnostic; oracle-agreement row populates
`q1_divergence_diagnostic_json` (Q2-ADR-004 schema).

Posture: project-lead-recommended (boot directive Forward Flag) — Q2 logs;
Q1's silent fail-soft is Q1's call; Q2 surfaces the divergence in its own
data lineage. The oracle-agreement signal exists exactly to catch
Q1↔ground-truth divergences from this vantage point.

**Consequences.**

- *Negative:* Manifest computation adds a CMake build step (sha256 over
  canonicalized source files). Cost <1 s; negligible.
- *Negative:* Algorithm-change → regression rebuild = wall-clock cost on
  every meaningful change to search.cc, transition.cc, state.h. Real cost;
  the stamping makes it auditable rather than amplifying it.
- *Negative:* Manifest SHAs are over source content, not behavior. Two
  semantically identical refactors produce different SHAs. Intentional
  conservatism — prefer false-positive regen over false-negative skip.
- *Negative:* Unknown-power diagnostic depends on Q4 registry availability
  + an encounter-id → declared-spawn-powers map. Phase-1A registry is
  `contracts/registry/phase1-silent.json`; the declared-spawn-powers map
  is small (one entry per Phase-1 encounter) and lives in
  `engine/cpp/src/oracle/adapter/encounter_spawn_powers.h`. If
  registry-SHA in the blob doesn't match the known catalog, diagnostic
  emission is gated; fallback is a separate `registry_sha_mismatch`
  diagnostic.
- *Positive:* Every signal Q2 emits is traceable to a precise
  `(algorithm, registry, simulator)` tuple. Q10 can quarantine signals
  from old algorithm versions without explicit Q2 coordination.
- *Positive:* Q1 silent-fail-soft on KaiserCrabBoss spawn-powers becomes
  observable in Q2's data lineage. Diagnostic posture matches `oracle.md`
  role.
