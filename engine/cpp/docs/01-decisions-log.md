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
| Q2-ADR-002 | Phase-1A adapter encounter scope = CULTISTS_NORMAL only | Superseded by Q2-ADR-006 |
| Q2-ADR-003 | Verify-RPC transport = JSON-over-Unix-socket | Accepted |
| Q2-ADR-004 | Oracle-agreement sink = Parquet on local filesystem | Accepted |
| Q2-ADR-005 | Algorithm-version manifest stamping + unknown-power diagnostic | Accepted |
| Q2-ADR-006 | Polymorphic Power-Hook Framework | Accepted (2026-05-17) |
| Q2-ADR-007 | Data-Driven `MonsterMoveTable` | Accepted (2026-05-17) |
| Q2-ADR-008 | STS-Canonical Damage/Block Formula Extraction | Accepted (2026-05-17) |
| Q2-ADR-009 | LouseProgenitor Port — First Encounter via Q2-ADR-006 Framework | Accepted (2026-05-17) |
| Q2-ADR-010 | Zobrist 128-bit Hash-Only Transposition Table | Accepted (2026-05-18) |
| Q2-ADR-011 | `absl::flat_hash_map` Container + Hard TT Entry Cap + Flag-and-Early-Return | Accepted (2026-05-18) |
| Q2-ADR-012 | Slime prerequisites — kMaxEnemies 2→4 + MonsterKind/MoveId/FollowUpRule extensions + Zobrist table widening | Accepted (2026-05-18) |
| Q2-ADR-013 | SmallSlimes port — Slimed card mechanics + Exhaust emulation + enemy-move RNG chance-node + CannotRepeat rule | Accepted (2026-05-18) |
| Q2-ADR-014 | Restore upstream stat widths (wave-23 stat widening to int32 + Zobrist key table expansion) | Accepted (2026-05-18) |

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

**Status:** Superseded by Q2-ADR-006.

> **Superseded by Q2-ADR-006 on 2026-05-17** — Phase-1A CULTISTS_NORMAL-only scope no longer applies; framework now supports arbitrary monster kinds.

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

### Amendment 2026-05-18 (wave-19 — Zobrist + absl)

Algorithm-sha source list expanded to include:

- `engine/cpp/src/ai/zobrist.cc` — NEW file; content (Zobrist tables + hash function implementation) is now part of the algorithm definition.
- Zobrist seeds as algorithm constants: `kZobristSeedLo = 0xC0FFEE12345678ULL`, `kZobristSeedHi = 0xDEADBEEF20260517ULL`. Seed change → algorithm_sha rotation + full cultist pin re-validation.
- `ABSL_VERSION_TAG` CMake variable (currently `20260107.1`) — absl LTS releases can change `flat_hash_map` iteration order or hash mixing, potentially yielding different solve trajectories. Algorithm_sha must rotate on any absl version bump.

**Rationale.** Zobrist hashing replaces the CompactState-keyed TT with a hash-only TT. The hash function implementation, seed values, and the absl container version all influence search behavior (via TT hit/miss patterns and iteration order). Manifest conservatism — prefer false-positive regen over false-negative skip — requires folding all three into the algorithm_sha source list.

**Cross-references.** Q2-ADR-010 (Zobrist key design + seeds). Q2-ADR-011 (absl::flat_hash_map + ABSL_VERSION_TAG).

### Amendment 2026-05-18 — Stub → CMake-computed algorithm_sha

Wave-20.β replaced the stub `"phase1a-stub-algorithm-sha"` literal with a CMake build-time SHA-256 computed via `cmake/AlgorithmShaCompute.cmake` over a canonical source list (declared in `cmake/AlgorithmSha.cmake`). An `add_custom_command` with explicit file dependencies ensures the hash recomputes on every build where any listed file changes.

**Canonical algorithm-SHA source list** (sorted, applied via `file(SHA256)` each, then concatenated + folded with seeds + absl version):
- engine/cpp/include/sts2/ai/{state.h, search.h, chance.h, zobrist.h}
- engine/cpp/include/sts2/game/damage_calc.h
- engine/cpp/include/sts2/oracle/adapter/project_powers.h
- engine/cpp/src/ai/{search.cc, transition.cc, recommend.cc, chance.cc, zobrist.cc}
- engine/cpp/src/game/{damage.cc, monster_moves.cc, card_effects.cc}
- engine/cpp/src/oracle/adapter/{cultists_projection.cc, louse_progenitor_projection.cc}
- Plus constants: `kZobristSeedLo`, `kZobristSeedHi`, `ABSL_VERSION_TAG`

Note: `card_effects.cc` is in the declared list but not yet present on disk; `cmake/AlgorithmSha.cmake` skips missing files at configure time and will include it automatically once added.

**Exclusions**: `manifest.cc` itself (circular), CMake files (build-system not algorithm), tests (not runtime behavior).

**Determinism**: cross-platform LF line endings enforced via `.gitattributes`. Dual `cmake -B` on the same source tree produces identical `kAlgorithmSha`.

**Effect**: every consumer of `current_manifest().algorithm_sha` (pinned test rows, verify-server response, oracle-agreement schema) now sees a real 64-char hex value that rotates on any algorithm-impacting source change. The generated constant lives in `build*/generated/manifest_constants.h` (excluded from git via existing `build*/` pattern).

### Wave-23 amendment (2026-05-18) — stat.h added

Per Q2-ADR-014 (wave-23 stat widening), `engine/cpp/include/sts2/game/stat.h`
added to `ALGORITHM_SHA_SOURCES` in `cmake/AlgorithmSha.cmake`. Rationale:
Stat::pack16 (and previously pack8) affects every Zobrist fold; the header
is algorithm-impacting. Source-list inclusion ensures algorithm_sha rotates
on Stat changes.

---

## Q2-ADR-006 — Polymorphic Power-Hook Framework

**Status:** Accepted (2026-05-17).

**Context.** Q2's `EnemyState` was hardcoded for cultist mechanics: dedicated fields `dark_strike_base_`, `ritual_amount_`, `just_applied_ritual_`, and `strength_`/`weak_` scalars. `transition.cc` contained cultist-only branch logic. Adding any future encounter required new fields on `EnemyState` and new switch arms in every transition function — O(encounters × transition-sites) code growth. Q2-ADR-002 scoped Phase-1A to CULTISTS_NORMAL only because the substrate provided no generic mechanism for other monsters.

Wave-16 opens Path A: the substrate is generalized into a framework where future encounters land as data and per-`PowerKind` hook functions, not as new struct fields or new transition arms.

**Decision.**

1. **`PowerKind` enum** — stable order, never reorder. Values: `kStrength=0`, `kWeak=1`, `kRitual=2`, `kCurlUp=3`, `kFrail=4`, `kVulnerable=5`, future entries append at end. Cultist-relevant kinds appear first so existing cultist hash representation is preserved (strength/ritual fields map to the same logical positions).

2. **`PowerInstance` POD** — `{kind: PowerKind, stacks: int16_t, flags: uint8_t, _pad: uint8_t}`. `sizeof(PowerInstance) == 4` (static_asserted). `flags` bit 0 = `just_applied` (formerly `just_applied_ritual_`). `stacks` is signed (some powers can transiently go negative). `operator==` defaulted.

3. **Per-creature power array** — each creature carries `std::array<PowerInstance, kMaxPowersPerCreature=6>` + `uint8_t power_count`. Zero-stack powers are dropped from the array (not stored). Helpers: `find_power`, `add_power`, `remove_power`, `tick_down_power`.

4. **`MonsterKind` byte on `EnemyState`** — distinguishes archetype at the state level (e.g. `kCultistCalcified`, `kCultistDamp`, `kLouseProgenitor`). Replaces the per-field cultist-archetype encoding. `EnemyState` carries no per-kind union — all kind-specific behavior lives in the `MonsterMoveTable` and hook functions.

5. **`HookPoint` enum** and per-`PowerKind` dispatch — hooks fire at fixed transition boundaries via a switch on `PowerKind` in `transition.cc`. Each power gets a single `hook_<name>` function with an inner switch on `HookPoint`. No virtual dispatch; all types are POD. Adding a new power = one new case + one new hook function.

   `HookPoint` values: `kOnSpawn`, `kBeforeAttackDamage`, `kBeforeBlockGain`, `kAfterDamageReceived`, `kAfterCardPlayedFinished`, `kAtEnemyTurnStart`, `kAtEnemyTurnEnd`, `kAtPlayerTurnStart`, `kAtPlayerTurnEnd`.

6. **Player power array** — `CompactState` gains `std::array<PowerInstance, kMaxPowersPerCreature> player_powers_` + `uint8_t player_power_count_`. Old `player_strength_` / `player_weak_` become `find_power(player, kStrength).stacks` / `find_power(player, kWeak).stacks`; existing call sites preserved as accessor wrappers.

**Cross-links.** ADR-004 (state representation — `CompactState` storage widens per the Amendment block). Q2-ADR-002 (superseded by this ADR).

### §Canonical hook firing order

Per plan §3a (locked; deviation requires Q2-ADR-006 amendment):

```
PLAYER TURN START:
  1. Player block clears (set to 0)
  2. Player draws cards (Phase-1 = 5/turn fixed; deterministic)
  3. Player energy refills (3)
  4. Fire kAtPlayerTurnStart hook for every active power (player + each enemy)
     — order: player powers first, then enemies in slot order
PLAYER ACTIONS (one per "action" step in expectimax):
  For each card played:
    a. Decrement player.energy by card.cost
    b. Remove card from hand → discard (or exhaust if Exhaust keyword)
    c. For each card effect in declared order:
       - kAttack:    base_damage → compute_outgoing_attack(base, p_str, p_weak, target_vuln)
                      → mitigated by target.block → remainder reduces target.hp
                      → fire kAfterDamageReceived on target's powers (CurlUp records here)
       - kDefend:    base_block → compute_outgoing_block(base, p_dex=0, p_frail, is_powered=true)
                      → adds to player.block
       - kBuff:      add_power(target=self_or_player, kind, stacks)
       - kDebuff:    add_power(target, kind, stacks)
    d. Fire kAfterCardPlayedFinished on every active power
       (CurlUp's stored-card-match → block gain + power remove fires here)
PLAYER TURN END:
  4. Fire kAtPlayerTurnEnd hook for every active power
ENEMY TURN START:
  5. For each alive enemy in slot order:
     a. Enemy block clears (set to 0)
     b. Fire kAtEnemyTurnStart hooks for that enemy's powers
        — Ritual fires here: if performed_first_move → add_power(self, kStrength, ritual_amount)
                                                       AND set just_applied flag
  6. For each alive enemy in slot order:
     a. Resolve enemy.move[move_index] effects in declared order
     b. Set performed_first_move = true
     c. Advance move_index to follow_up_index
ENEMY TURN END:
  7. Fire kAtEnemyTurnEnd hook for every active power
     — Frail decrement fires here (per STS2 FrailPower.AfterTurnEnd(side=Enemy)):
        tick_down_power(player, kFrail, 1)
  8. Loop back to PLAYER TURN START (round++)
```

`just_applied` flag: set when a power was applied THIS turn boundary; cleared at the next turn-start hook of the same side. Block clear timing is per-side: player block clears at PLAYER TURN START step 1; enemy block clears at ENEMY TURN START step 5a. This matches cultist behavior pre-refactor and is preserved post-refactor.

**Consequences.**

- *Negative:* `PowerKind` enum order is permanently frozen; reordering breaks the hash invariant for existing pinned seeds. Future powers must append — no insertion.
- *Negative:* Fixed `kMaxPowersPerCreature=6` is a hard ceiling. If a future encounter requires >6 simultaneous powers on a single creature, the array must widen (ADR amendment required; all existing states re-hashed).
- *Negative:* Per-`PowerKind` switch in `dispatch_enemy_power_hook` / `dispatch_player_power_hook` must be updated for every new power. Forgetting a case compiles silently (no exhaustiveness check without `-Wswitch` coverage). Mitigation: clang's `-Wswitch` enforced in build; test_power_hooks.cc covers dispatch for each registered power.
- *Negative:* `algorithm_sha` flips (per Q2-ADR-005) because `transition.cc`, `state.h`, and the new framework headers all change. Existing cultist pinned seeds must be regenerated via `seed-pinner`. Values are numerically identical; only the stamp rotates.
- *Positive:* Adding a new encounter (wave-17+) = one `MonsterKind` enum entry + one `MonsterMoveTable` entry + optional new `PowerKind` entries + hook functions. No changes to existing transition sites. Marginal cost per encounter drops dramatically from wave-16 onward.
- *Positive:* All power semantics are localized to `hook_<power_name>` functions. Reading any power's behavior requires inspecting one function, not tracing through multiple transition sites.
- *Positive:* Cultist behavior is byte-invariant at the value level: oracle `solve()` output for any cultist state is numerically identical pre/post refactor. Locked by `CompactStateValueInvariants.CultistSolveMatchesPreRefactor` regression test.
- *Positive:* Player power generalization (player_powers_ array) unblocks all future player-side debuffs (Frail, Vulnerable) and buffs (Strength, Dexterity) at zero additional structural cost.

**Origin.** Wave-16 plan §Framework design §1–3; plan §3a (hook firing order). Q2-ADR-002 Path A directive.

---

## Q2-ADR-007 — Data-Driven `MonsterMoveTable`

**Status:** Accepted (2026-05-17).

**Context.** Cultist move rotation was hardcoded in `transition.cc`: dedicated `MoveId` enum with only `kIncantation`/`kDarkStrike`, damage values burned into the transition logic, move-advancement logic cultist-specific. Adding LouseProgenitor (wave-17) required adding new enum values AND new switch arms in every transition site that looked at `current_move`. The hardcoded structure did not generalize.

**Decision.**

A `constexpr` table `kMonsterMoveTables[MonsterKind]` provides all per-monster move data:

- `MonsterMove` — carries `MoveId id`, `uint8_t follow_up_index`, `std::array<MoveEffect, kMaxEffectsPerMove=3> effects`, `uint8_t effect_count`.
- `MoveEffect` — `{kind: MoveEffectKind, value: int16_t, power_kind: PowerKind, _pad}`. `sizeof(MoveEffect) == 6`. `MoveEffectKind` values: `kNone`, `kAttack`, `kDefend`, `kBuffSelf`, `kDebuffPlayer`.
- `MonsterMoveTable` — `std::array<MonsterMove, kMaxMovesPerMonster=6> moves`, `uint8_t move_count`, `uint8_t initial_move_index`, `uint8_t min_hp`, `uint8_t max_hp`, `std::array<SpawnPowerEntry, kMaxSpawnPowers=3> spawn_powers`, `uint8_t spawn_power_count`.
- `SpawnPowerEntry` — `{kind: PowerKind, stacks: int16_t, _pad}`. `sizeof(SpawnPowerEntry) == 4`. Applied at `kOnSpawn` hook. Cultist has `spawn_power_count=0`; LouseProgenitor (wave-17) will have `{kCurlUp, 14}`.

Constants: `kMaxEnemies=4`, `kMaxMovesPerMonster=6`, `kMaxEffectsPerMove=3`, `kMaxSpawnPowers=3`.

`MonsterKind` indexes directly into `kMonsterMoveTables`. `move_index_` in `EnemyState` follows the table's `follow_up_index` chain. Adding a new monster = append one `MonsterMoveTable` entry; no changes to transition logic.

Adapter helper `find_move_index(MonsterKind kind, MoveId id) → uint8_t` walks the table to map a wire-emitted `MoveId` to a table index. Returns `0xFF` (not-found sentinel) if the MoveId is absent from the monster's table.

Cultist re-expression: `dark_strike_base` → `kMonsterMoveTables[kCultistCalcified/Damp].moves[DarkStrike].effects[0].value`. `ritual_amount` → `kMonsterMoveTables[...].moves[Incantation].effects[0].value`. The `cultist_archetype_from_wire_name` adapter returns `MonsterKind` directly.

Wave-16 scope: only cultist entries are populated in `kMonsterMoveTables`. The `MonsterKind::kLouseProgenitor` enum slot is RESERVED; the table entry is zero-initialized until wave-17 populates it.

**Consequences.**

- *Negative:* `kMonsterMoveTables` is a `constexpr` array; compile-time size is `MonsterKind` enum cardinality. Adding a new `MonsterKind` value grows the array at compile time. Entries for reserved-but-unpopulated monsters must be zero-initialized (valid but not invoked until the wave that activates them).
- *Negative:* `MoveEffect` layout has explicit `_pad` field. `static_assert(sizeof(MoveEffect) == 6)` enforces this, but the pad is wasted storage for moves with fewer than 3 effects. Accepted: array is small (≤6 moves × ≤3 effects = 18 entries per monster).
- *Negative:* `kMaxEffectsPerMove=3` is a hard ceiling. Compound moves with >3 sub-effects require a constant bump + all existing table entries re-validated. Current Phase-1 survey shows max = 2 (CURL_AND_GROW, WEB_CANNON); 3 is a single-increment reserve.
- *Positive:* Adding a new monster is one `MonsterMoveTable` entry. No changes to transition sites.
- *Positive:* Compound moves (attack + debuff in one action, like WEB_CANNON) are first-class: `effect_count=2`, both effects in `effects[]`. No special-casing in transition.
- *Positive:* Spawn-power synthesis (LouseProgenitor's CurlUp at spawn) is declared in the table rather than scattered across adapter + transition code. The adapter checks `spawn_powers` and applies them on `kOnSpawn` hook.
- *Positive:* Adapter `find_move_index` provides a stable seam between wire-emitted `MoveId` and table-index; wave-17 adds new `MoveId` enum values without touching any table-iteration logic.

**Origin.** Wave-16 plan §Framework design §4. Generalizes cultist `transition.cc` move-advance logic.

---

## Q2-ADR-008 — STS-Canonical Damage/Block Formula Extraction

**Status:** Accepted (2026-05-17).

**Context.** Damage and block computation was inlined at use-sites in `transition.cc`. The inline logic handled cultist-only cases (no Frail, no Vulnerable in Phase-1A). Generalizing to LouseProgenitor (Frail debuff; wave-17) and future encounters with Vulnerable, Dexterity, or Strength requires that the formulas be expressed once, correctly, and tested in isolation.

Upstream canonical authority: `~/development/projects/godot/sts2/src/Core/Commands/DamageCmd.cs` + `BlockCmd.cs`. STS2 uses decimal arithmetic with `floor` at the final step for both damage and block.

**Decision.**

Extract two pure helpers (no side effects, no state mutation):

```cpp
// engine/cpp/include/sts2/game/damage.h
namespace sts2::game::combat {

int compute_outgoing_attack(int base, int attacker_strength,
                            bool attacker_weak, int target_vulnerable);

int compute_outgoing_block(int base, int gainer_dexterity,
                           bool gainer_frail, bool is_powered_source);

}  // namespace sts2::game::combat
```

Multiplication order for `compute_outgoing_attack` (locked — matches STS canonical):

1. `v = base + attacker_strength`
2. If `target_vulnerable > 0`: `v = (v * 3) / 2` (×1.5, integer floor)
3. If `attacker_weak`: `v = (v * 3) / 4` (×0.75, integer floor)
4. `return std::max(0, v)`

For `compute_outgoing_block`:

1. `v = base + gainer_dexterity`
2. If `gainer_frail && is_powered_source`: `v = (v * 3) / 4` (×0.75, integer floor)
3. `return std::max(0, v)`

`is_powered_source` is `true` for card-based block (Defend, Survivor) and monster-move-based block (CURL_AND_GROW); `false` for baseline block from relics or other non-powered sources. Frail tax applies only to powered-source block per STS2 `FrailPower.ModifyBlockMultiplicative`.

All transition call sites use these helpers; no inline formula duplication.

**Cross-link.** Upstream `~/development/projects/godot/sts2/src/Core/Commands/DamageCmd.cs` is the canonical authority for multiplication order and rounding semantics.

**Consequences.**

- *Negative:* Multiplication order (strength → vulnerable → weak) is locked by this ADR; any upstream STS2 formula change requires an ADR amendment AND a regression-set rebuild (Q2-ADR-005 `algorithm_sha` flip).
- *Negative:* `is_powered_source` flag must be threaded through all block-gain call sites. Incorrect flag (powered source passed as unpowered) silently produces wrong block values. Mitigation: test_damage_formula.cc covers all flag combinations.
- *Positive:* Formula is testable in isolation without constructing full `CompactState`. `test_damage_formula.cc` covers ≥10 cases including boundary rounding (e.g., base=1, strength=0, vulnerable=true gives floor(1.5)=1 not 2).
- *Positive:* Frail's block-tax is correctly scoped to powered sources only (matching STS2 semantics, not STS1). A single implementation site prevents per-call-site divergence.
- *Positive:* Future encounter powers (Dexterity, additional Vulnerable stacking) extend the helpers at one site; all call sites benefit automatically.

**Origin.** Wave-16 plan §Framework design §5. Upstream authority `DamageCmd.cs` + `BlockCmd.cs` (STS2 canonical floor-rounding).

---

## Q2-ADR-009 — LouseProgenitor Port (first encounter via Q2-ADR-006 framework)

**Status:** Accepted (2026-05-17).

**Context.** Wave-17 ports LouseProgenitor as the first non-cultist encounter exercising the Q2-ADR-006 power-hook framework and Q2-ADR-007 data-driven `MonsterMoveTable`. Q1's `CurlUpPower` and `FrailPower` are behavioral stubs (no hooks implemented in the C# headless engine — they carry the correct power IDs and stacks on the wire but perform no semantic computation during combat resolution). Q2 implements STS2-canonical semantics per upstream `CurlUpPower.cs:14-71` and `FrailPower.cs:22-41` directly, not derived from Q1's stubs. This is the first exercise of the end-to-end framework path (enum reservation wave-16 → hook implementation + table population + adapter projection wave-17).

**Decision.**

1. **LouseProgenitor `MonsterMoveTable` entry** populated in `monster_moves.cc`: HP 134–136 (A0); 3-move rotation starting at WEB_CANNON; follow-up chain WEB_CANNON → CURL_AND_GROW → POUNCE → WEB_CANNON (index 0 → 1 → 2 → 0). Move effects:
   - `WEB_CANNON`: `kAttack(9)` + `kDebuffPlayer(Frail, 2)`.
   - `CURL_AND_GROW`: `kDefend(14)` + `kBuffSelf(Strength, 5)`.
   - `POUNCE`: `kAttack(16)`.
   Spawn-power entry: `{kCurlUp, 14}` applied at `kOnSpawn`.

2. **`CurlUpPower` hook** implemented per upstream `CurlUpPower.cs:14-71`: two-trigger pattern. `kAfterDamageReceived` — if the damage source is a powered attack AND no card-source has been recorded yet, store the card source. `kAfterCardPlayedFinished` — if the just-played card matches the stored source, owner gains block (amount = stacks), set `LouseProgenitor.Curled=true`, remove this power. Triggers once per combat. Multi-hit cards (Twin Strike) trigger only once; block applied after the card fully resolves, not mid-card.

3. **`FrailPower` hook** implemented per upstream `FrailPower.cs:22-41`: `kBeforeBlockGain` (`ModifyBlockMultiplicative`) — returns `(v * 3) / 4` when the block-gainer is the Frail-owner and `is_powered_source=true`. `kAtEnemyTurnEnd` — `tick_down_power(player, kFrail, 1)` (Frail on the player decrements once per enemy-turn-end per STS2 `FrailPower.AfterTurnEnd(side=Enemy)`).

4. **Adapter projection** `louse_progenitor_projection.cc` recognizes the `LouseProgenitorNormal` encounter signature (already in `adapter.cc` encounter_map from wave-16 framework). Synthesizes spawn-power `CurlUp(14)` per Q2-ADR-005 silent-drop pattern if the wire blob omits it (Q1 drops spawn-powers at boot per D3 fixture README). Sets `kind_ = MonsterKind::kLouseProgenitor`; computes `move_index_` via `find_move_index(kLouseProgenitor, wire_intent_move_id)`.

5. **Pinned-seed gtest** added for `LouseProgenitorNormal` seed=42 (D3 fixture #5). `algorithm_sha` regenerated via `seed-pinner`; cultist pinned values numerically unchanged (same behavior, same oracle output — only the `algorithm_sha` stamp rotates because `transition.cc` and `monster_moves.cc` changed).

**Consequences.**

- *Negative:* Q1's `CurlUpPower` and `FrailPower` are behavioral stubs; Q2's expectimax produces oracle values incorporating real semantic behavior that Q1's combat simulation does not currently mirror. Differential testing (when applicable) may show divergences between Q1 combat output and Q2 oracle labels for LouseProgenitor states. Acceptable for Phase-1: Q2 is the verifier, Q1 is the engine substrate. Divergences surface in the oracle-agreement signal (Q2-ADR-004) and are not silent.
- *Negative:* The wave-17 shape refactor (`EnemyState` + `CompactState` + `transition.cc` rewrite) invalidates pre-wave-16 cultist hash bytes — `algorithm_sha` flips per Q2-ADR-005. Existing cultist pinned seeds are re-stamped with the new `algorithm_sha`; numerical values (expected_hp, expected_rounds) are UNCHANGED because cultist behavior is preserved bit-identical.
- *Negative:* q2-ci wall-clock grows from ~18 min to approximately 25–35 min with the LouseProgenitor pinned seed added (slow expectimax solve over LouseProgenitor state space). The new pinned-seed gtest is gated under `DISABLED_` prefix (like the cultist slow-regression test) and runs only via `--gtest_also_run_disabled_tests` in the `make q2-ci` slow-regression target.
- *Positive:* First non-cultist encounter SHIPPED post-Q2-ADR-002 supersession; the Q2-ADR-006/007/008 framework validated end-to-end on a non-cultist encounter. D3 fixture #5 (LouseProgenitorNormal) transitions from reject-with-diagnostic to full round-trip.
- *Positive:* Future encounters (GremlinMerc, HauntedShip, FossilStalker, SmallSlimes, ... per ADR-029 Path A roadmap) add as DATA only — one `MonsterMoveTable` entry + optional new `PowerKind` entries + hook functions. Marginal code cost per additional encounter is low.
- *Positive:* CurlUp + Frail are real player-decision-shaping mechanics (block gain contingent on attack choice; block penalty from debuff). Oracle values for LouseProgenitor states now meaningfully differ from a naive damage simulator, providing genuine training signal for Q10.

**Cross-references.** Q2-ADR-006 (polymorphic power-hook framework). Q2-ADR-007 (data-driven `MonsterMoveTable`). Q2-ADR-008 (damage/block formula). Pipeline ADR-029 (Path A campaign roadmap; LouseProgenitorNormal row checked off).

**Origin.** Wave-17. Plan `~/.claude/plans/plan-the-q2-oracle-glittery-pony.md` §"Wave-17 absorbs the deferred work" + §"Wave-17 preview". Upstream authority: `~/development/projects/godot/sts2/src/Core/Models/Powers/CurlUpPower.cs:14-71`, `FrailPower.cs:22-41`, `Models/Monsters/LouseProgenitor.cs:36-122`.

### Amendment 2026-05-18 — POUNCE damage corrected

Wave-20.α corrected `kMonsterMoveTables[kLouseProgenitor].moves[POUNCE].effects[0].value` from `16` to `14`. Wave-18 picked the DeadlyEnemies (A11+) ascension value instead of the A0 baseline. Upstream source: `Models/Monsters/LouseProgenitor.cs:63`. Q2 ships Phase-1A = A0 per Q2-ADR-002.

This fix:
- Reduces oracle-agreement divergence with Q1 (Q1's LouseProgenitor port should have POUNCE = 14; converges post-fix).
- Rotates `algorithm_sha` automatically (wave-20.β's CMake build-graph hash sees `monster_moves.cc` change).
- Recovers the LouseProgenitor pinned-seed gtest deferred from wave-18 (added in wave-20.α; locks POST-FIX values).

---

## Q2-ADR-010 — Zobrist 128-bit Hash-Only Transposition Table

**Status:** Accepted (2026-05-18).

**Context.** Cultist solve peaks at **24.4 GB RSS** (baseline locked at `.claude/state/profiles/wave-19-pre.json`). A 16 GB peak RSS ceiling is required to hold through future slime encounters: MediumSlimes = 4 enemies; state space projected 150–400M entries. Structural shrink alone cannot hold the budget after `kMaxEnemies` bumps from 2 → 4 (`CompactState` grows to ~232 B even aggressively shrunk → ~23 GB cultist). The per-state key footprint of the existing `std::unordered_map<CompactState, SearchResult, CompactStateHash>` is the dominant factor. Memory must come from key compression — Zobrist hash-only TT is the only approach that scales linearly enough to hold cultist AND slime under a single 16 GB ceiling.

**Decision.** Replace `std::unordered_map<CompactState, SearchResult, CompactStateHash>` with `absl::flat_hash_map<ZobristKey, Score, ZobristKeyHash>` (container choice per Q2-ADR-011). `ZobristKey` is a 128-bit composite of two independent 64-bit Zobrist halves. The collision probability at 370M entries is ~10⁻²⁰ — eliminates silent-corrupted-new-encounter-pin risk. `SearchResult.best_action` and `SearchResult.terminal` are dropped from the cached value; `best_action` is re-derived on demand in `recommend.cc` via shared `chance.h` helpers; terminal is inferred from state via existing `transition::is_terminal`. The `peek()` API is replaced by `peek_score()` returning `std::optional<Score>`.

### §Key composition

Every state feature gets a dedicated key slot, indexed by `(feature_value, position)` to prevent positional aliasing (e.g., "Strength=5 on enemy slot 0" must hash differently from "Strength=5 on enemy slot 1"). The table below is locked — engineer encodes exactly this; deviation requires ADR amendment.

| Feature | Encoding | Keys | Bytes |
|---|---|---|---|
| Player HP | `key_player_hp[hp]`, hp ∈ [0, 255] | 256 | 2 KB |
| Player Block | `key_player_block[block]` | 256 | 2 KB |
| Player Energy | `key_player_energy[e]`, e ∈ [0, 7] | 8 | 64 B |
| Round | `key_round[r]`, r ∈ [0, 255] | 256 | 2 KB |
| Phase | `key_phase[p]`, p ∈ [0, 3] | 4 | 32 B |
| Player PowerInstance (per slot) | `key_player_power[slot][kind][stacks][flags]`; slot ∈ [0, kMaxPowersPerCreature); kind ∈ [0, kPowerKindCount); stacks ∈ [0, 100); flags ∈ [0, 4) | 6×10×100×4 = 24,000 | 192 KB |
| Player power_count | `key_player_power_count[n]`, n ∈ [0, 6] | 7 | 56 B |
| Enemy HP (per enemy slot) | `key_enemy_hp[enemy_slot][hp]` | 2×256 = 512 | 4 KB |
| Enemy Block (per enemy slot) | `key_enemy_block[enemy_slot][block]` | 512 | 4 KB |
| Enemy MonsterKind | `key_enemy_kind[enemy_slot][kind]` | 2×16 = 32 | 256 B |
| Enemy move_index | `key_enemy_move_idx[enemy_slot][idx]`; idx ∈ [0, 6] | 12 | 96 B |
| Enemy current_move | `key_enemy_current_move[enemy_slot][move_id]`; move_id ∈ [0, 8) | 16 | 128 B |
| Enemy alive bit | `key_enemy_alive[enemy_slot][bit]` | 4 | 32 B |
| Enemy performed_first_move | `key_enemy_pfm[enemy_slot][bit]` | 4 | 32 B |
| Enemy dark_strike_base | `key_enemy_dsb[enemy_slot][dsb]`; dsb ∈ [0, 32) | 64 | 512 B |
| Enemy ritual_amount | `key_enemy_ritual[enemy_slot][r]`; r ∈ [0, 32) | 64 | 512 B |
| Enemy PowerInstance (per enemy slot × per power slot) | `key_enemy_power[enemy_slot][power_slot][kind][stacks][flags]` | 2×6×10×100×4 = 48,000 | 384 KB |
| Enemy power_count | `key_enemy_power_count[enemy_slot][n]` | 14 | 112 B |
| Enemy count (kMaxEnemies) | `key_enemy_count[n]`, n ∈ [0, 3] | 3 | 24 B |
| CardCounts hand | `key_hand[card_id][count]`; card_id ∈ [0, 4); count ∈ [0, 16) | 64 | 512 B |
| CardCounts draw | same shape | 64 | 512 B |
| CardCounts discard | same shape | 64 | 512 B |

Total per hash table: ~600 KB. Two tables (lo, hi) = ~1.2 MB total. Static; allocated once.

XOR composition: `zobrist_of(s)` iterates all features and XORs the corresponding key into the running hash. Pure function, deterministic, no incremental update. Out-of-range value → assertion + abort (state corruption; not a runtime path).

### §Seeds

Committed seeds: `kZobristSeedLo = 0xC0FFEE12345678ULL`, `kZobristSeedHi = 0xDEADBEEF20260517ULL`. Tables initialized via Meyers singleton (`static const ZobristTables& tables()` — C++11 thread-safe one-time init) using `std::mt19937_64{seed}` per half. Seeds are treated as algorithm inputs per Q2-ADR-005 — any seed change requires ADR amendment + full cultist pin re-validation.

### §FP-determinism

`-fno-fast-math` is required on all Q2 TUs. The following flags are forbidden: `-ffinite-math-only`, `-fassociative-math`, `-freciprocal-math`, `-fno-signed-zeros`, `-march=native` (SIMD that varies FP rounding across machines). Search is single-threaded. Default FP rounding mode (round-to-nearest, ties-to-even). This contract is load-bearing for `recommend.cc` re-derivation correctness: the re-derivation walks the same FP computation graph as solve's argmax, so bit-equal score comparison holds IFF FP flags are identical. Any future change to Q2 build FP flags = ADR amendment + cultist pin re-validation.

### §Recovery

On observed collision (vanishingly unlikely at ~10⁻²⁰): (a) seed re-roll + global re-pin of all encounters via `seed-pinner`. Maintains 16 GB ceiling math. 192-bit key widening = fallback only if seed re-roll fails to find a collision-free hash for some pinned state.

**Consequences.**

- *Negative:* `algorithm_sha` flips per Q2-ADR-005 — cultist pin re-stamped via `seed-pinner` (numerical values unchanged; SHA stamp rotates).
- *Negative:* `recommend()` loses cached `SearchResult` — 1–2 ms re-derivation cost per call (1-ply argmax + PV walk via repeated `derive_best_action`); negligible vs solve (~3 min) but adds latency to every `recommend()` call.
- *Negative:* `Search` constructor commits ~14 GB upfront (slot array `tt_.reserve(kMaxTtEntries)`) — even for small encounters (cultist at 85M entries reserves 370M slots).
- *Negative:* `peek()` API replaced with `peek_score()` returning `std::optional<Score>` — breaking change for in-tree consumers (`recommend.cc`, `test_search_known.cc`); engineer audits + migrates all call sites.
- *Negative:* FP-determinism contract becomes load-bearing; CMake flag audit + lockdown required before wave merges.
- *Positive:* Cultist 24.4 GB → ~12 GB peak RSS; slime (MediumSlimes 4 enemies) fits under 16 GB ceiling with ~3–4× cap headroom.
- *Positive:* 128-bit collision probability (~10⁻²⁰ at cap) eliminates silent-corrupted-new-encounter-pin risk that plagues shorter hashes at high entry counts.

**Cross-references.** Q2-ADR-005 (algorithm-sha manifest; amended wave-19 to include `zobrist.cc` + seeds + `ABSL_VERSION_TAG`). Q2-ADR-011 (container choice + cap policy). ADR-029 (Path A roadmap; new encounters must add pin before merge).

**Origin.** Wave-19. Plan `~/.claude/plans/plan-the-q2-oracle-glittery-pony.md` §1 (key composition), §3 (best-action re-derivation), §3a (shared chance helper), §8 (FP-determinism), §Recovery.

---

## Q2-ADR-011 — `absl::flat_hash_map` Container + Hard TT Entry Cap + Flag-and-Early-Return

**Status:** Accepted (2026-05-18).

**Context.** With the Zobrist 128-bit key (Q2-ADR-010), TT entry payload is 16 B key + 16 B value = 32 B. Container choice determines per-entry overhead:

- `std::unordered_map`: node-based — ~56 B/entry at 32 B payload (node ptrs + cached hash + bucket share) → 286M cap @ 16 GB.
- `absl::flat_hash_map`: open-addressed — ~38 B/entry (32 B slot inline + 1 B control byte + ~5 B LF=7/8 slack) → 420M raw cap @ 16 GB.

Open-addressing wins decisively at this payload size: 50% more headroom, one contiguous allocation, dramatically less allocator fragmentation at 100M+ entries. Additionally, `absl::flat_hash_map::clear()` retains the slot-array allocation — reusing capacity across `solve()` calls avoids ~14 GB alloc/free churn per solve.

**Decision.** TT container = `absl::flat_hash_map<ZobristKey, Score, ZobristKeyHash>`. Hard cap `kMaxTtEntries = 370'000'000` (~14 GB at 38 B/entry; 2 GB margin for process baseline + scratch). Cap behavior: flag-and-early-return (NOT throw — deep expectimax recursion is not exception-safe; flag check per frame is cheap and preserves existing control flow). `SolveStatus` enum (`kConverged`, `kCapExceeded`) added; `SearchResult` gains `.status` + `.entries_at_cap` fields. Callers MUST check `.status == kConverged` before consuming `.score` or `.best_action`.

### §absl version pin

`20260107.1` (LTS). Version bumps require ADR amendment. Version is part of `algorithm_sha` input via `ABSL_VERSION_TAG` CMake variable propagated to `manifest.cc` (absl releases can change `flat_hash_map` iteration order or hash mixing, potentially affecting solve trajectories).

### §TT lifecycle

`Search()` constructor calls `tt_.reserve(kMaxTtEntries)` ONCE per `Search` object lifetime — commits ~14 GB slot array at construction. `solve()` calls `tt_.clear()` (absl retains capacity — no re-allocation); subsequent solves reuse the slot array without allocator churn. No rehash path is entered after construction: `absl::flat_hash_map` doubles on overflow, and a transient rehash from 256M → 512M slots holds both arrays (768M slots × 32 B = 24 GB — would violate ceiling). Reserving `kMaxTtEntries` upfront eliminates this spike.

### §Cap-hit behavior

When `tt_.size() >= kMaxTtEntries`: set `cap_hit_ = true`; insertion silently drops. Each recursion frame early-returns `Score{}` when `cap_hit_` is set. `solve()` returns `SearchResult{.status = kCapExceeded, .entries_at_cap = tt_.size()}`. `result.score` and `result.best_action` are **unspecified** when `status != kConverged`. Verification gates (D.1 / E.0) MUST check status before pin validation.

**Consequences.**

- *Negative:* Adds `absl` dependency (FetchContent at build; ~2–4 GB peak RAM during parallel absl compilation).
- *Negative:* `Search` constructor commits 14 GB even if `solve()` is never called — acceptable for per-encounter `Search` object lifecycle, but precludes lightweight `Search` construction for testing purposes without a smaller-capacity constructor variant.
- *Negative:* Cap-hit aborts solve; D.1 / E.0 verification gates must check `status` before pin validation; any consumer that ignores `status` silently gets unspecified values.
- *Negative:* `peek()` API breaking change (handled in Q2-ADR-010 — replaced by `peek_score()`).
- *Positive:* 50% more cap headroom vs `std::unordered_map` at the same RAM budget (420M raw vs 286M cap entries @ 16 GB).
- *Positive:* Contiguous slot array reduces allocator fragmentation dramatically at 100M+ entries — lower effective RSS than node-based map.
- *Positive:* `clear()` retains capacity → no ~14 GB alloc/free churn between solves on the same `Search` object.

**Cross-references.** Q2-ADR-010 (Zobrist key type). Q2-ADR-005 (algorithm_sha incl. `ABSL_VERSION_TAG`).

**Origin.** Wave-19. Plan `~/.claude/plans/plan-the-q2-oracle-glittery-pony.md` §2 (container choice rationale), §4 (cap behavior), §4a (TT lifecycle).

---

## Q2-ADR-012 — Slime prerequisites — kMaxEnemies 2→4 + MonsterKind/MoveId/FollowUpRule extensions + Zobrist table widening

**Status:** Accepted (2026-05-18).

**Context.** Wave-19 closed with `kMaxEnemies = 2` (reduced from the original wave-17 plan of 4 to control TT memory pressure prior to the Zobrist redesign). Post-Zobrist (wave-19), per-entry TT cost is 38 B regardless of `CompactState` size; the `kMaxEnemies` bump only widens the Zobrist key tables (~1.2 MB → ~2.4 MB static), not the TT itself. Slime encounters require `kMaxEnemies ≥ 4`: `SlimesNormal` (`MediumSlimes` wire name) is 4 enemies; `SlimesWeak` (`SmallSlimes` wire name) is 3 enemies. Porting these encounters is blocked until the substrate accommodates ≥4 enemy slots.

Additionally, slime move-tables require new `MonsterKind` and `MoveId` enum values, and the Goop / sticky behaviour of slime moves requires a branching follow-up rule that the existing `follow_up_index` scalar cannot express. Wave-21 resolves all three prerequisites before the wave-22 slime data port.

**Decision.**

### kMaxEnemies 2 → 4

`kMaxEnemies` raised from 2 to 4 in `engine/cpp/include/sts2/ai/state.h`. Enemy-slot-indexed Zobrist key tables (HP, Block, `MonsterKind`, `MoveId`, alive, `performed_first_move`, dark_strike_base, ritual_amount, PowerInstance arrays, power_count) widen from 2 slots per half to 4 slots per half. The mt19937_64 fill order is **APPEND-ONLY**: slots 0 and 1 consume the same PRNG outputs they did pre-wave-21, preserving cultist `ZobristKey` byte identity; slots 2 and 3 append at the end of the fill sequence.

### MonsterKind enum extension (APPEND-ONLY)

New values appended after existing entries:

```
kLeafSlimeS  = 3
kLeafSlimeM  = 4
kTwigSlimeS  = 5
kTwigSlimeM  = 6
```

`kMonsterKindCount` advances 3 → 7. All existing values (`kCultistCalcified=0`, `kCultistDamp=1`, `kLouseProgenitor=2`) are unchanged.

### MoveId enum extension (APPEND-ONLY)

New values appended after existing entries:

```
kTackleMove    = 5
kGoopMove      = 6
kClumpShot     = 7
kStickyShot    = 8
kPokeyPounce   = 9
```

All existing values (`kIncantation=0`, `kDarkStrike=1`, `kWebCannon=2`, `kCurlAndGrow=3`, `kPounce=4`) are unchanged.

### FollowUpRule schema extension (MonsterMove struct)

`MonsterMove` gains a `FollowUpRule` enum and associated per-branch fields:

```cpp
enum FollowUpRule : uint8_t {
  kStrict                    = 0,   // deterministic follow_up_index (existing behaviour)
  kRandomBranchCannotRepeat  = 1,   // uniform random among branches; cannot repeat last
  kWeightedRandomCannotRepeat = 2,  // weighted random among branches; cannot repeat last
};
```

Per-branch fields added to `MonsterMove`:
- `uint8_t branch_indices[kMaxBranchCount]` — move-table indices of candidate next moves.
- `uint8_t branch_weights[kMaxBranchCount]` — weights (used when rule = `kWeightedRandomCannotRepeat`; uniform otherwise).
- `uint8_t branch_cannot_repeat` — bit-mask of branch slots disallowed on repeat.
- `uint8_t branch_count` — number of valid entries in the branch arrays.

`kStrict = 0` MUST remain value 0. All existing `MonsterMove` entries for cultist and `LouseProgenitor` are zero-initialised for the new fields, which is semantically correct: `follow_up_rule = kStrict`, `branch_count = 0` — the existing `follow_up_index` scalar continues to govern advancement. No behaviour change for non-slime monsters.

### Factory stubs

`make_leaf_slime_s`, `make_leaf_slime_m`, `make_twig_slime_s`, `make_twig_slime_m` added in `engine/cpp/src/game/enemies.cc`. Each stub sets `kind_`, HP (rolled per upstream A0 ranges), and `alive = true`. Move-table population is deferred to wave-22.β; stubs are not reachable from any production path until the wave-22 adapter projection lands.

**§Cultist hash byte identity (verification mechanism).**

Wave-21 pre-flight (B.0) captured the cultist canonical-root `ZobristKey` at `engine/cpp/tests/seeds/cultist_zobrist_pin.h`:

```cpp
static constexpr uint64_t kCultistZobristKeyLo = 0xf812af56366b5548ULL;
static constexpr uint64_t kCultistZobristKeyHi = 0x2c51edb8b6bd404eULL;
```

Wave-21.β's verification gate adds `Zobrist.CultistRootKey_MatchesPreWave21Pin` synthetic test, which asserts `zobrist_of(canonical_cultist_state) == ZobristKey{kCultistZobristKeyLo, kCultistZobristKeyHi}`. **Failure = wave-21 rollback** — either the mt19937_64 fill order regressed (slots 0+1 shifted), or the `fold_enemy` loop bound changed to include the new slots in positions previously occupied by slots 0+1. This assertion is the strongest possible guarantee that the table widening preserved cultist behaviour bit-for-bit; it is stronger than a numerical oracle-value pin alone, which could in principle tolerate a Zobrist collision (vanishingly unlikely at ~10⁻²⁰ but theoretically non-zero).

**Consequences.**

- *Negative:* `algorithm_sha` rotates per Q2-ADR-005 (`state.h` and `zobrist.cc` both modified; both are in the canonical source list). Verify-server response `algorithm_sha` changes. All pinned scenario rows pick up the new hex stamp at next test run.
- *Negative:* Zobrist key tables grow ~1.2 MB → ~2.4 MB static. Expected peak_rss_gb post-wave-21 is ~6.3 GB (was 6.19 GB pre-wave-21); negligible delta relative to the 16 GB ceiling.
- *Negative:* APPEND-ONLY constraints on `MonsterKind`, `MoveId`, and `FollowUpRule` must be maintained in all future waves. Reordering any of these enum values breaks cultist and `LouseProgenitor` regression by shifting the Zobrist table slots their features map to.
- *Negative:* Cultist and `LouseProgenitor` `MonsterMove` initializer syntax may require conversion to designated initializers when the `FollowUpRule` fields are added to the struct (engineer choice during wave-21.α implementation — aggregate initialisation order must be preserved or converted to named fields).
- *Positive:* Unblocks slime encounter port: wave-22 can land `SmallSlimes` (`SlimesWeak`) data; wave-23 can land `MediumSlimes` (`SlimesNormal`) data.
- *Positive:* `FollowUpRule` schema makes `RandomBranch` monster moves expressible in data without any new struct shape in wave-22; the branching plumbing is laid here.

---

## Q2-ADR-013 — SmallSlimes port — Slimed card mechanics + Exhaust emulation + enemy-move RNG chance-node + CannotRepeat rule

**Status:** Accepted (2026-05-18).

**Context.** Wave-21 (Q2-ADR-012) landed the structural prerequisites for slime encounters: `kMaxEnemies` raised to 4, four new `MonsterKind` values, five new `MoveId` values, and the `FollowUpRule` schema. Wave-22 ports the `SmallSlimes` (`SlimesWeak`) encounter, which introduces three new categories of substrate work: (1) a status card that is injected into the player's discard pile mid-combat (Slimed), requiring a card-injection pathway and an Exhaust emulation mechanism; (2) enemies whose next-move selection is RNG-resolved after the current move resolves, requiring a new chance phase; and (3) the `CannotRepeat` branching rule governing that RNG. All three are tightly coupled to the slime port and are ratified together in a single ADR.

**Decision.**

### §Slimed-card

`CardId::kSlimed` is added at enum value 5, appended after `kStrike=1`, `kDefend=2`, `kNeutralize=3`, `kSurvivor=4`. The APPEND-ONLY constraint on `kCountedCardIds` is critical: the array grows from size 4 to size 5, with `kSlimed` at index 4. Inserting `kSlimed` at any lower index shifts existing entries and corrupts cultist + LouseProgenitor `CardCounts` Zobrist hashes.

A new `MoveEffectKind::kAddStatusCard` enum value is appended (APPEND-ONLY; rationale: an explicit semantic rather than overloading `kDebuffPlayer`). Slime status-move effects use `kAddStatusCard` with `value = N` (number of Slimed cards to inject).

Slimed card semantics per upstream `Slimed.cs:19,22,33`:
- Cost: 1 energy (Slimed.cs:22: `base(1, CardType.Status, CardRarity.Status, TargetType.None)`).
- Type: Status (non-upgradeable per Slimed.cs:15: `MaxUpgradeLevel => 0`).
- Keyword: Exhaust (Slimed.cs:19: `CanonicalKeywords => CardKeyword.Exhaust`).
- OnPlay: Draw 1 card (Slimed.cs:33: `CardPileCmd.Draw(choiceContext, base.DynamicVars.Cards.BaseValue, base.Owner)` where `DynamicVar` is `new CardsVar(1)`).

Status cards are materialized in the player's discard pile via `CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, N, null)` (CardPileCmd.cs:886-916). In Q2, `do_enemy_act` increments `state.discard[kSlimed] += effect.value` when a move has `MoveEffectKind::kAddStatusCard`.

### §Exhaust-emulation

Slimed is the FIRST Exhaust-keyword card in Q2. Survivor only discards another hand card on play (not Exhaust); Slimed is the first card that is deleted from the game on play rather than moved to the discard pile.

A `bool exhaust_on_play` flag is added at the END of the `CardEffect` struct, default `false`. All existing `CardEffect` entries pick up `exhaust_on_play = false` via the default, preserving their discard-on-play behaviour without modification.

The one-way deletion in `do_play_card`:
```
hand[id]--;
if (!effect.exhaust_on_play) discard[id]++;
// (if exhaust_on_play: card is simply removed; no discard increment)
```

No exhaust pile state is tracked in `CompactState`. Rationale: the exhaust pile is dead state from the expectimax perspective. A card in the exhaust pile cannot be redrawn or replayed within a combat encounter in the scenarios covered by Phase-1A. Tracking exhaust-pile counts would add Zobrist key dimensions and state-space cardinality without any influence on search decisions. Future ADR required if a card or power depends on exhaust-pile contents.

### §Enemy-move-RNG

`Phase::kAtEnemyMoveRng` is a new phase value, appended at value `= 2` to the existing `{kPlayerActing=0, kAtChanceDraw=1}`. APPEND-ONLY constraint: inserting at a lower value shifts `kAtChanceDraw` from 1 to a new slot, corrupting cultist Zobrist phase hashes and breaking all pinned seeds.

The chance node fires AFTER enemy resolution, BEFORE the player's next draw. Precise Phase ordering:

```
kPlayerActing
  ↓ (end turn)
resolve_end_turn_pre_draw
  ├─ player block clears
  ├─ enemy ticks (Ritual, Frail, etc.)
  └─ enemy resolves current_move (deterministic effect application)
  ↓
kAtEnemyMoveRng       (CHANCE NODE — only if any enemy's next-move follow-up is RandomBranch)
  ↓ (RNG enumerates next-move outcomes per enemy)
kAtChanceDraw          (CHANCE NODE — player draws cards)
  ↓ (draw enumeration)
kPlayerActing          (next round)
```

Two consecutive chance nodes per round arise when any enemy's just-resolved move has a `RandomBranch` follow-up. When all enemies have deterministic follow-ups, `kAtEnemyMoveRng` is skipped; the transition goes directly to `kAtChanceDraw`.

"Pending RNG" is DERIVED from the move table, not encoded as state. At `kAtEnemyMoveRng`, `chance.h::enumerate_chance_outcomes` walks alive enemies and looks up `kMonsterMoveTables[kind].moves[current_move_idx].follow_up_rule`. If the rule is a `RandomBranch` type, it enumerates per-branch outcomes. If `kStrict`, the next move is deterministic and was already assigned. No sentinel `MoveId` value; no additional fields in `CompactState`; no Zobrist key noise.

### §CannotRepeat-rule

The CannotRepeat exclusion-and-renormalization algorithm at `kAtEnemyMoveRng`:

1. Filter branches: exclude any branch `i` where `branch_cannot_repeat[i] && branch_indices[i] == current_move_idx`.
2. Sum remaining branch weights to get normalizer `N`.
3. For each remaining branch `i`: probability `p_i = branch_weights[i] / N`.
4. Yield outcome list: one `(p_i, child_state)` pair per remaining branch, with `enemies[e].current_move = branch_indices[i]`.

Worked examples:

- **LeafSlimeS post-TACKLE**: `kRandomBranchCannotRepeat`; both branches have `CannotRepeat`. CannotRepeat excludes TACKLE (current). Only GOOP_MOVE is eligible → `N = 1`, `p = 1.0`. Deterministic alternation.
- **LeafSlimeS post-GOOP**: CannotRepeat excludes GOOP_MOVE. Only TACKLE_MOVE is eligible → `N = 1`, `p = 1.0`. Deterministic alternation.
- **TwigSlimeM post-POKEY**: branches `{POKEY w=2, STICKY w=1+CannotRepeat}`. CannotRepeat on STICKY triggers only when `current = STICKY`; here `current = POKEY` → no exclusion → `N = 3` → POKEY `p = 2/3`, STICKY `p = 1/3`. 2 outcomes.
- **TwigSlimeM post-STICKY**: CannotRepeat excludes STICKY. Only POKEY (w=2) eligible → `N = 2` → POKEY `p = 1.0`. 1 outcome (deterministic).

### §Slime-monsters

Upstream-verified HP ranges and move sets (A0 ascension baseline; ToughEnemies threshold applies at A7+, DeadlyEnemies at A11+; Q2 targets A0 per Q2-ADR-002/006):

**LeafSlimeS** (LeafSlimeS.cs:20-39):
- HP A0: 11–15 (MinInitialHp line 20: `GetValueIfAscension(ToughEnemies, 12, 11)` → A0=11; MaxInitialHp line 22: `GetValueIfAscension(ToughEnemies, 16, 15)` → A0=15).
- Moves: TACKLE_MOVE — `kAttack`, 3 dmg (TackleDamage line 24: `GetValueIfAscension(DeadlyEnemies, 4, 3)` → A0=3); GOOP_MOVE — `kAddStatusCard`, 1 Slimed (LeafSlimeS.cs:55: `AddToCombatAndPreview<Slimed>(..., 1, null)`).
- Resolver: `RandomBranchState` with `MoveRepeatType.CannotRepeat` on both branches (LeafSlimeS.cs:34-35). Initial state: `RandomBranch` (turn-1 move is RNG-resolved; no fixed start — state machine begins at the `randomBranchState` per line 39).

**LeafSlimeM** (LeafSlimeM.cs:22-40):
- HP A0: 32–35 (MinInitialHp line 22: `GetValueIfAscension(ToughEnemies, 33, 32)` → A0=32; MaxInitialHp line 24: `GetValueIfAscension(ToughEnemies, 36, 35)` → A0=35).
- Moves: CLUMP_SHOT — `kAttack`, 8 dmg (ClumpDamage line 26: `GetValueIfAscension(DeadlyEnemies, 9, 8)` → A0=8); STICKY_SHOT — `kAddStatusCard`, 2 Slimed (LeafSlimeM.cs:73: `AddToCombatAndPreview<Slimed>(..., 2, null)`).
- Resolver: strict alternation via `FollowUpState` chain (LeafSlimeM.cs:34-37: `moveState.FollowUpState = new MoveState("STICKY_SHOT") { FollowUpState = moveState }`). Initial: STICKY_SHOT (line 40: `MonsterMoveStateMachine(list, moveState2)`).

**TwigSlimeS** (TwigSlimeS.cs:15-27):
- HP A0: 7–11 (MinInitialHp line 15: `GetValueIfAscension(ToughEnemies, 8, 7)` → A0=7; MaxInitialHp line 17: `GetValueIfAscension(ToughEnemies, 12, 11)` → A0=11).
- Moves: TACKLE_MOVE — `kAttack`, 4 dmg (TackleDamage line 19: `GetValueIfAscension(DeadlyEnemies, 5, 4)` → A0=4).
- Resolver: self-loop (`moveState.FollowUpState = moveState` per TwigSlimeS.cs:27). Initial: TACKLE_MOVE.

**TwigSlimeM** (TwigSlimeM.cs:23-42):
- HP A0: 26–28 (MinInitialHp line 23: `GetValueIfAscension(ToughEnemies, 27, 26)` → A0=26; MaxInitialHp line 25: `GetValueIfAscension(ToughEnemies, 29, 28)` → A0=28).
- Moves: POKEY_POUNCE_MOVE — `kAttack`, 11 dmg (ClumpDamage line 27: `GetValueIfAscension(DeadlyEnemies, 12, 11)` → A0=11); STICKY_SHOT_MOVE — `kAddStatusCard`, 1 Slimed (TwigSlimeM.cs:75: `AddToCombatAndPreview<Slimed>(..., 1, null)`).
- Resolver: `RandomBranchState` weighted; POKEY weight=2, STICKY weight=1 with `CannotRepeat` (TwigSlimeM.cs:37-38: `randomBranchState.AddBranch(moveState, 2); randomBranchState.AddBranch(moveState2, MoveRepeatType.CannotRepeat)`). Initial: STICKY_SHOT_MOVE (line 42: `MonsterMoveStateMachine(list, moveState2)`).

### §SmallSlimes-encounter

Upstream `SlimesWeak.cs:48-59` defines the spawn logic for `SmallSlimes` (`SlimesWeak`). Three enemies; 2 consecutive `Rng.NextItem` calls produce 4 possible compositions (2 × 2 = 4 RNG variants):

```
_smallSlimes = [LeafSlimeS, TwigSlimeS]
_mediumSlimes = [LeafSlimeM, TwigSlimeM]

Slot 0: monsterModel  = Rng.NextItem(_smallSlimes)         // RNG #1: LeafSlimeS or TwigSlimeS
Slot 1:               = Rng.NextItem(_mediumSlimes)         // RNG #2: LeafSlimeM or TwigSlimeM
Slot 2: monsterModel2 = the OTHER small (not picked at slot 0)
```

Wire signatures for adapter detection (sorted alphabetical per existing `encounter_map` convention):

| Variant | Wire signature (sorted) | encounter_id |
|---|---|---|
| Leaf-medium | `{LeafSlimeM, LeafSlimeS, TwigSlimeS}` | `SmallSlimes` |
| Twig-medium | `{LeafSlimeS, TwigSlimeM, TwigSlimeS}` | `SmallSlimes` |

Both wire signatures map to `encounter_id="SmallSlimes"` in `adapter.cc::encounter_map`. Two separate entries are required because the set of monster names differs between variants. The single `project_small_slimes(...)` function handles both variants: it dispatches on the wire-name presence of `"LeafSlimeM"` vs `"TwigSlimeM"` to assign each slime to the correct `CompactState` enemy slot.

### §Q1-divergence-acknowledgment

Q2 implements REAL upstream STS2 Slimed semantics: when a slime uses its status move, `CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, N, null)` (CardPileCmd.cs:886-916) materializes N Slimed cards in the player's discard pile mid-combat. In Q2, `do_enemy_act` mirrors this by incrementing `state.discard[kSlimed] += effect.value`.

Q1's `Intent.Status(N)` is an intent-only stub (per Q2-ADR-009 precedent; `Phase1Monsters.cs:21` defines `Intent.Status(N)` as a display intent with no card injection). The Q1 stub does NOT materialize Slimed cards in the player's discard pile.

Oracle-agreement gate will surface divergence for SmallSlimes fights where Slimed has been generated (Q1 discard contains no Slimed cards; Q2 discard contains N Slimed cards). Per Q2-ADR-029 and Q2-ADR-009, this divergence is acceptable and surfaced, not silent. Q12 evaluation harness's agreement-rate metric will trail until Q1 ports Slimed injection in a future Q1 wave.

Informational interaction notes (not exercised in wave-22; no slime+Louse cross-encounter):
- Slimed lacks the `ValueProp.Move` flag → `IsPoweredAttack()` returns false (ValuePropExtensions.cs:5-11) → Slimed-gained block does NOT trigger `CurlUpPower.AfterDamageReceived`.
- Slimed-gained block also does NOT suffer `FrailPower`'s 0.75 multiplier (FrailPower.cs:22-31: `ModifyBlockMultiplicative` guards on `IsPoweredCardOrMonsterMoveBlock()` which also requires `ValueProp.Move`).

**Consequences.**

- *Negative:* `algorithm_sha` rotates per Q2-ADR-005. `transition.cc`, `chance.cc`, `card_effects.cc`, and `monster_moves.cc` are all in the canonical source list; all are touched by wave-22. All downstream Q10/Q12 consumers must filter oracle rows by `algorithm_sha` to avoid cross-wave comparisons.
- *Negative:* Oracle-agreement DIVERGES for slime fights where Slimed has been generated. Q1 emits intent-only stubs; Q2 emits real discard mutations. Divergence rate increases as SmallSlimes fights accumulate in the Q3 experience store. Per Q2-ADR-029, this is acceptable and surfaced; it will remain until Q1 ports Slimed injection in a future wave.
- *Negative:* `Phase`, `CardId`, and `MoveEffectKind` enums all gain new APPEND-ONLY entries. All future waves must respect the append constraint. Auditing new enum-dependent code sites (Zobrist table fills, switch statements) is mandatory at each wave dispatch.
- *Negative:* Wave-22 LOC estimate ~1500–1800; substantial substrate change. If engineer reports complexity beyond session budget mid-dispatch, split into 22.α₁ (substrate types + Exhaust) → 22.α₂ (enemy-move RNG chance node). Both touch `transition.cc` → serialize on the same worktree branch, not parallel streams.
- *Negative:* SmallSlimes pin solve has unknown state-space cost. If `Search::solve()` returns `SolveStatus::kCapExceeded` (state count exceeds 370M entry cap), the pin test commits as `DISABLED_DISABLED_...` and wave-22 closes with a documented gap. A cap-recovery wave (LRU eviction or structural shrink) follows.
- *Positive:* First non-cultist, non-LouseProgenitor encounter shipping. SmallSlimes (3 enemies + Slimed mechanics) unblocks the slime campaign.
- *Positive:* Q2-ADR-013 framework is reusable for future Phase-1+ encounters that emit status cards (Acid Slime variants in future ascensions; any encounter using `CardPileCmd.AddToCombatAndPreview`). The `exhaust_on_play` flag, `kAddStatusCard` effect kind, and `kAtEnemyMoveRng` chance phase generalize beyond slimes.

**Cross-references.** Q2-ADR-002 (Path A scope), Q2-ADR-005 (algorithm_sha), Q2-ADR-009 (LouseProgenitor port; precedent for upstream-vs-Q1 divergence), Q2-ADR-010 (Zobrist hash-only TT; cap-recovery path), Q2-ADR-011 (absl + cap policy), Q2-ADR-012 (kMaxEnemies bump + slime prerequisites), ADR-029 (Path A roadmap).

### Q2-ADR-013 — Amendment 1 (wave-23-prep, 2026-05-18) — Enemy.kind dispatch fix + algorithmic non-convergence surfaced

**Trigger.** SmallSlimes synthetic solve SIGSEGVed in Release (~1 sec wall-clock, peak RSS 529 MB) blocking the C.4-δ pin capture in wave-22.

**Root cause.** The wave-22.α C.2-α substrate keystone added `MonsterKind kind_` and `uint8_t move_index_` fields to `EnemyState` and routed `do_enemy_act` + `do_roll_next_move` dispatch through them, but the `sts2::game::Enemy` struct (the headless engine's per-enemy model) had no corresponding `kind` field. The slime factories (`make_leaf_slime_s/m`, `make_twig_slime_s/m`) only set `e.name = "Leaf Slime (S)"` etc. — never communicating MonsterKind to the AI projection. The `build_enemy_state` projector in `state.cc` consequently never called `.kind(...)` on the builder; `EnemyState.kind_` defaulted to `MonsterKind::kCultistCalcified` for all slime enemies coming through `from_combat`. With slime kind wrongly reported as cultist:
- `do_enemy_act` skipped `kind_is_slime` → `kind_is_slime(MonsterKind::kCultistCalcified) == false` → fell through to `act_on_intent(current_move)`, which is a silent no-op for slime MoveIds (`kTackleMove`, `kStickyShot`, `kGoopMove`, `kClumpShot`, `kPokeyPounce`).
- `do_roll_next_move` only dispatched LouseProgenitor through `advance_intent_table`; slimes fell through to cultist `advance_intent`, which is also a silent no-op for slime MoveIds.

Net effect: slimes were entirely passive in `CompactState`. Combat never terminated from player death; the search recursed via `solve_player → solve_chance → solve_player → ...` indefinitely, incrementing `state.round_` each iteration. Once `round_` exceeded `kMaxRound=256`, the Zobrist `at(t.round, idx=round)` lookup read past the round-key table. In Debug this fired the cardinality-bound assertion at `zobrist.cc:518`; in Release (NDEBUG strips asserts) the OOB read produced garbage XOR contributions which eventually corrupted control-flow → SIGSEGV.

**Fix (this amendment).**
1. Added `MonsterKind kind = MonsterKind::kCultistCalcified;` field to `sts2::game::Enemy` (defaults preserve cultist Zobrist byte identity — cultist factories leave the field at the default).
2. Slime factories set `e.kind` to the appropriate enum value (`make_leaf_slime_s` → `kLeafSlimeS`, etc.).
3. `build_enemy_state` in `state.cc` passes `e.kind` to the builder via `.kind(e.kind)` and resolves `move_index_` via `monster_moves::find_move_index(e.kind, e.current_move)`. Falls back to 0 when the lookup returns the sentinel `0xFF` (defensive — should not happen in well-formed combats).
4. `do_roll_next_move` in `transition.cc` dispatches ALL kinds through `advance_intent_table`. Cultist semantics are unchanged because cultist's MonsterMoveTable encodes the legacy `advance_intent` sequence exactly: `moves[0]` (Incantation) has `follow_up_index=1`, `moves[1]` (DarkStrike) has `follow_up_index=1` (self-loop). LouseProgenitor was already routed through `advance_intent_table`; slimes now route through it.

**Verified bit-identical post-fix.** All three regression pins hold:
- `Zobrist.CultistRootKey_MatchesPreWave21Pin` → `lo=0xf812af56366b5548 hi=0x2c51edb8b6bd404e`.
- `CultistsSearchPins.DISABLED_StarterCombatSeedC0ffee_PinnedAgreement` → `expected_hp=40.90829202578665 expected_rounds=6.4579809748486445`.
- `LouseProgenitorSearchPins.DISABLED_LouseProgenitorNormalFixture5_PinnedAgreement` → `expected_hp=0.040793122639484494 expected_rounds=10.151992676894496`.
- `AiStateParity.RandomWalk_CompactStateMatchesCombat` still passes — `do_roll_next_move` now keeps `move_index_` in sync with `current_move_` so `from_combat(combat) == compact` after each step.

**Second blocker surfaced by the fix (algorithmic, not implementation).** With slimes correctly dispatching, the SmallSlimes Variant A search still does not terminate. The slime damage budget at A0 (TwigSlimeS Tackle 4 deterministic + LeafSlimeM alternating CLUMP 8 / STICKY 0 + LeafSlimeS alternating TACKLE 3 / GOOP 0 → average ≈ 9.5 dmg/turn) is below the player's chained-Defend block budget (3 × Defend 5 = 15 block/turn). The "all-defend" sub-branch of the search tree has no terminal state: player blocks all damage, slimes never die, `round_ → ∞`. Combined with `kAddStatusCard` accumulating Slimed cards each turn (`CardCounts.uint8_t` wraparound past 16 → Zobrist `kMaxCountPerCardZone` OOB) AND `probability::enumerate_draws` asserting `pool.total() ≤ kMaxN=12` (sized for Silent starter only), the search hits one of three downstream failure sites depending on which fires first: `probability.cc:66`, `zobrist.cc:518`, or stack overflow in `solve_player`. All three are manifestations of the same unbounded state-space issue. Speculative widening of `kMaxN` / `kMaxRound` / `kMaxCountPerCardZone` with APPEND-only Zobrist fill discipline was prototyped during wave-23-prep but reverted: widening alone is insufficient because cycles emerge in the saturated state graph (expectimax post-order TT insertion can re-enter an un-inserted state via a chance-node child, blowing the stack before TT dedup kicks in).

**Resolution out of scope for wave-23-prep.** Three approaches considered, all requiring substrate-semantic ratification (new Q2-ADR or amendment):
- **(A) Explicit search horizon.** Add a `depth` parameter (or `Search::depth_` member) and a `kMaxDepth` constant; when exceeded, `solve_player`/`solve_chance` return a horizon-score (e.g. `Score{state.player_hp, 0}` interpreted as "combat ends in stalemate with current HP"). Trades exact expected-value guarantee for bounded recursion. Smallest LOC delta but breaks the `Score::better_than` ordering invariant for player-tank branches (horizon-score depends on entry HP, not optimal play).
- **(B) State-space saturation + cycle-aware expectimax.** Saturate `state.round_` (cap at, say, 32) AND saturate per-zone `CardCounts` (cap at, say, 31) in the substrate's transition functions (not just at Zobrist lookup time). This bounds the state space finite. Then redesign `solve_player` to insert a sentinel into TT BEFORE recursing on children (instead of after), and return the sentinel score on re-entry. Treats cycles as "loop" outcomes with score equal to the current state's expected value. Larger LOC delta and changes the expectimax algorithm contract.
- **(C) Encounter reshape.** Modify SmallSlimes data (e.g. give LeafSlimeS a Strength buff per round, or convert LeafSlimeM's STICKY to deal residual damage) to ensure the slime damage budget exceeds the maximum block budget in finite turns. Preserves the search algorithm but diverges from upstream STS1 data (and from the existing Q4 SmallSlimes fixture).

Project-lead to ratify the approach choice + open a follow-up wave (wave-23 proper or wave-24). The SmallSlimes pin test stays `DISABLED_` with `GTEST_SKIP()` and a BLOCKER #3 message documenting the situation. Cultist + LouseProgenitor pins are NOT affected; only encounters with the "non-damaging move + sub-tank-budget total damage" pathology surface the blocker. Future encounters with similar profiles (e.g. Acid Slime Small in higher ascensions, where STS1 has a comparable Tank/Goop pattern) must check the encounter's damage budget against the worst-case block budget before pinning.

**Files touched in this amendment.**
- `engine/cpp/include/sts2/game/enemy.h` — added `MonsterKind kind` field.
- `engine/cpp/src/game/enemies.cc` — slime factories set `e.kind`.
- `engine/cpp/src/ai/state.cc` — `build_enemy_state` calls `.kind()` + `.move_index()`.
- `engine/cpp/src/ai/transition.cc` — `do_roll_next_move` table-driven for all kinds.
- `engine/cpp/tests/oracle/test_small_slimes_search_pins.cc` — GTEST_SKIP message updated.

**Verification gate (wave-23-prep close).** Cultist Zobrist byte identity passes; cultist + LouseProgenitor search pins bit-identical; AiStateParity passes; SmallSlimes test SKIPs cleanly with BLOCKER #3 message. Net LOC delta ≈ 80 (under the 500-LOC "major substrate rewrite" re-surface threshold).

**Cross-references.** Q2-ADR-002 (Path A scope). Q2-ADR-006 (polymorphic power-hook framework — wave-21 does not touch power hooks; only the `monster_moves` table shape). Q2-ADR-010 (Zobrist hash-only TT; §Recovery seed re-roll path remains the recourse if cultist hash byte identity ever needs re-capture). Q2-ADR-011 (`absl::flat_hash_map` + cap).

### Q2-ADR-013 — Amendment 2 (2026-05-18) — Search horizon cap (resolves Blocker #3 recursion; surfaces cap-bust contingency)

Wave-22-fix-2 ratifies resolution candidate (a) from Amendment 1: an explicit
search horizon at `kSearchHorizonRounds = 50` rounds. States with
`state.get_round() > 50` return a horizon-truncated Score
`{expected_hp = state.player_hp.value(), expected_rounds = 0.0}` from
`Search::solve_player` + `Search::solve_chance` entry.

**Why option (a)** (chosen over (b) state saturation + cycle-aware expectimax
OR (c) encounter reshape):
- Simplest implementation (~10 LOC); minimal substrate disruption.
- Matches chess-engine convention (depth-limited search with horizon score).
- Preserves cultist + LouseProgenitor pins BIT-IDENTICAL (neither exceeds 50).
- (b) was deemed too architecturally invasive — cycle-aware expectimax has
  subtle correctness traps and would require its own ADR.
- (c) diverges from STS2 upstream (no precedent for buffing LeafSlimeS
  Strength per round) — would compromise Q2's role as a faithful oracle.

**Horizon-score semantics**: `expected_hp = player_hp` (optimistic; "if
you survive to round 50, you survive") + `expected_rounds = 0.0` (matches
is_terminal short-circuit convention; the caller chance-node adds +1 if
applicable). The implicit assumption is that defensive-block branches at
horizon represent acceptable outcomes for the player. Not inserted into TT —
horizon scores are state-specific by player_hp.

**Cap-bust contingency (surfaced by wave-22-fix-2 empirical run)**:
`kSearchHorizonRounds=50` bounds the DEPTH of each path but not the BREADTH
of the SmallSlimes state space. Empirical run (2026-05-18):
- `entries_at_cap = 370,000,000` (kMaxTtEntries exhausted)
- Wall-clock: ~7m51s; peak RSS: ~22.8 GB
- Status: `kCapExceeded`

SmallSlimes pin test reverts to `DISABLED_` + `GTEST_SKIP()` with a CAP-BUST
message. The horizon cap is correct and necessary (prevents infinite recursion)
but insufficient alone for SmallSlimes pin convergence.

**Consequences (lead with negatives)**:
- *Negative*: SmallSlimes pin capture STILL BLOCKED — horizon cap prevents
  non-termination but state-space breadth (3 slimes × alternating intents ×
  Slimed card accumulation across 3 zones) exceeds the 370M-entry TT cap.
  Q2-ADR-013 Amendment 3+ required to resolve pin capture.
- *Negative*: oracle's `expected_rounds` for SmallSlimes is now an
  underestimate for any defensive-play branch that would actually run
  longer than 50 rounds.
- *Negative*: future Phase-2+ encounters with legitimately deep search
  needs (>50 rounds) must revisit kSearchHorizonRounds via further
  amendment.
- *Negative*: cap is hard-coded; not encounter-specific. Future flexibility
  may need a per-encounter horizon parameter.
- *Positive*: horizon cap resolves the infinite-recursion / SIGSEGV / stack-
  overflow failure mode permanently for all encounters, not just SmallSlimes.
- *Positive*: cultist + LouseProgenitor pins verified BIT-IDENTICAL post-cap
  (both encounters solve in <50 rounds; horizon check is dead code for them).
- *Positive*: two new unit tests `Search.HorizonCap_RoundOverLimit_*` +
  `Search.HorizonCap_RoundAtLimit_*` lock the `>` vs `>=` boundary behavior.

**Files touched in this amendment.**
- `engine/cpp/include/sts2/ai/search.h` — `kSearchHorizonRounds = 50` constant.
- `engine/cpp/src/ai/search.cc` — horizon-cap check at entry of `solve_player`
  + `solve_chance` (AFTER cap_hit_ + TT lookup, BEFORE legal_actions / resolve).
- `engine/cpp/tests/ai/test_search_known.cc` — two new horizon-cap unit tests.
- `engine/cpp/tests/oracle/test_small_slimes_search_pins.cc` — GTEST_SKIP
  message updated to CAP-BUST contingency with empirical measurements.

**Verification gate (wave-22-fix-2 close).** Cultist Zobrist byte identity
passes; cultist + LouseProgenitor search pins bit-identical; new horizon-cap
unit tests pass; SmallSlimes test SKIPs cleanly with CAP-BUST message.

**Cross-references.** Q2-ADR-011 (kMaxTtEntries cap policy + kCapExceeded
flag-and-early-return). Q2-ADR-012 (slime prerequisites). Q2-ADR-013
Amendment 1 (type-system fix + original algorithmic non-convergence analysis).

### Amendment 3 (2026-05-18) — Horizon reduction 50 → 25 (Blocker #4 persists)

Wave-22-fix-3 reduces `kSearchHorizonRounds` from 50 to 25 in response to
SmallSlimes synthetic solve hitting `kCapExceeded` at horizon=50
(370M TT entries; ~7m51s wall-clock; ~22.8 GB peak RSS — over the 16 GB
ceiling via reserve + transient growth + recursion stack).

**Rationale**: horizon-cap bounds search depth but not state-space breadth.
3 enemies × 2-move alternation × Slimed accumulation produces exponential
state-space growth with depth. Halving depth from 50 → 25 produces
~√(state-space) reduction in reachable distinct states — heuristic, but
likely fits under the 370M TT cap for SmallSlimes.

**Safety**: cultist + LouseProgenitor solve in 7 + 10 rounds respectively;
horizon=25 leaves comfortable headroom (18 + 15 round margins). Cultist
+ Louse pins remain BIT-IDENTICAL post-reduction; Zobrist byte identity
preserved (Zobrist key tables unaffected by horizon constant).

**Consequences (lead with negatives)**:
- *Negative*: SmallSlimes oracle's `expected_rounds` is now an underestimate
  for any defensive-play branch that would actually run longer than 25
  rounds (more pronounced bias than Amendment 2's horizon=50).
- *Negative*: Phase-2+ encounters with legitimate solve depth >25 rounds
  must bump horizon (Amendment 4+).
- *Negative*: Amendment 3 may STILL not unblock SmallSlimes if state-space
  breadth at depth 25 still exceeds cap. Amendment 4 candidates remain
  available: LRU eviction (c), CompactState compression (d), Slimed
  count cap (e), encounter pruning (f), or alternate variant (g).
- *Positive*: simplest possible fix (single constant change ~3 LOC);
  preserves all existing regression baselines; defensible reduction given
  cultist + Louse safety margins.

**Empirical outcome** (Path B): cap-bust persists at horizon=25
(entries_at_cap=370M, elapsed_wall=7m17s, peak_rss_gb=22.4). Amendment 4
required.

**Cross-references**: Q2-ADR-013 Amendment 1 (Blocker #3 surfacing);
Amendment 2 (horizon=50 cap mechanism); Q2-ADR-010 (Zobrist hash-only TT
cap interaction).

### Amendment 4 (2026-05-18) — Layered Method (h) + SmallSlimes deprecation — partial resolution of Blocker #4

Wave-22-fix-4 implemented Method (h) (layered c+d+e): Slimed accumulation
cap, LRU eviction, CompactState compression. The fix RESOLVED the
`kCapExceeded` failure mode (TT no longer hard-aborts; LRU evicts deterministic-
ally) BUT exposed a residual wall-clock failure: SmallSlimes synthetic Variant
A solve runs >40 min at 19.2 GB peak RSS with 99% sustained CPU (LRU thrashing)
without converging. Per-entry TT footprint measured ~96 B/entry (vs 70 B
projection at design time).

#### §Empirical-cap-bust-analysis

Wave-22-fix-3 reduced search horizon 50→25 and re-ran SmallSlimes synthetic:

| Horizon | Wall-clock | Peak RSS | Status |
|---|---|---|---|
| 50 | 7m51s | 22.8 GB | kCapExceeded @ 370M |
| 25 | 7m17s | 22.4 GB | kCapExceeded @ 370M |

Conclusion: breadth (not depth) is the binding constraint. Method (h) tackled
breadth via Slimed cap (e) + LRU safety net (c) + CompactState compression (d).

#### §Slimed-cap (layer e)

`kMaxSlimedAccumulation = 8` in `do_enemy_act` kAddStatusCard branch
(transition.cc). Bounds `discard[CardId::kSlimed]` dimension. Semantic
divergence from upstream STS2 (no cap canonically); minor oracle-correctness
hit acceptable per Q2-ADR-029.

#### §LRU-eviction (layer c)

Replaces hard-abort cap policy. `tt_insert` at cap evicts oldest entry
(deterministic LRU); `derive_best_action` re-solves children on TT miss
(re-solve correct via pure-function search; cost bounded by remaining subtree).
`Search.LRU_*` unit tests verify eviction + dual-run determinism.

#### §LRU-memory-tradeoff

Per-entry footprint design estimate 38B → 70B (list-node + iterator overhead).
Empirical measurement ~96B/entry → exceeded the 70B projection. `kMaxTtEntries`
reduced 370M → 200M to stay under 16 GB ceiling at projected footprint; actual
RSS at 200M cap ≈ 19.2 GB. Future amendment may reduce cap further OR accept a
larger ceiling.

#### §Compression (layer d)

PowerKind backing type `int → uint8_t`; PowerInstance 8B → 6B (the `_pad` field
remains LOAD-BEARING — stores CurlUp card-stamp via transition.cc's
get/set_curl_up_stored_card accessors; cannot be removed); SpawnPowerEntry 8B
→ 4B (`_pad` dropped); kMaxPowersPerCreature 6→4. Zobrist key_enemy_dsb +
key_enemy_ritual tables dropped (constant-per-MonsterKind dimensions; kind XOR
already distinguishes; ~2 KB static table savings). Stack-frame reduction in
recursion → ~5-10% solve speedup. Did NOT reduce state-space dimensionality for
SmallSlimes (the dropped fields were constant-zero for slimes); cap-bust
contribution marginal.

#### §Power-array-bound

`kMaxPowersPerCreature 6 → 4` ratified. Phase-1 monsters use ≤1 power (cultist
Ritual; Louse CurlUp; slimes zero). Engineer audit of
`monster_moves.cc::kSpawnPowers` data confirmed ≤4 max spawn count.

#### §Cultist-byte-rotation

Compression altered Zobrist mt19937_64 consumption order (key_enemy_dsb +
key_enemy_ritual tables removed). Cultist Zobrist BYTE IDENTITY rotated per
Q2-ADR-010 §Recovery. `cultist_zobrist_pin.h` re-stamped via
`dump_cultist_zobrist_key` test:
- Pre-fix-4: `kCultistZobristKeyLo = 0xf812af56366b5548`, `Hi = 0x2c51edb8b6bd404e`
- Post-fix-4: `kCultistZobristKeyLo = 0x471665c4838c298d`, `Hi = 0x770eab2147499e6c`

Cultist + Louse SOLVE values BIT-IDENTICAL (search algorithm invariant to hash
byte values; Q2-ADR-010 invariant preserved).

#### §SmallSlimes-deprecation

H.δ pin-capture attempt empirically demonstrated that Method (h)'s breadth
reductions are insufficient to make SmallSlimes Variant A synthetic solve
tractable within the 16 GB / 30-min envelope. Per project-lead SECOND-decision,
SmallSlimes is **REMOVED from the oracle's supported encounters** as of this
ratification. Affected:
- Adapter dispatch: SmallSlimes encounter_map entries + `is_small_slimes`
  dispatch branch removed; fixtures (e.g., fixture #6) now route through the
  AdapterReject path with `reason = "encounter_not_in_cpp_engine"`.
- Projection module: `small_slimes_projection.{h,cc}` + projection test deleted.
- Search-pin test: `test_small_slimes_search_pins.cc` tombstoned with GTEST_SKIP.
- Q2-ADR-029 roadmap row updated to DEPRECATED-IN-Q2.

The Method (h) infrastructure (Slimed cap + LRU + compression) is RETAINED on
main — it remains correctness-preserving + applicable to future encounters
that may benefit. The cap on LRU eviction count is informational; future
encounters that don't exhibit SmallSlimes' all-Defend tractability trap should
not trigger thrashing.

A different Phase-1 encounter (TBD; HauntedShip, GremlinMerc, ThreeSlimes
elite, or SlimeBoss are roadmap candidates) will be selected for the next
port wave. Selection criteria: bounded combat duration; no unbounded status-card
accumulation; player-defensive-block does NOT dominate the enemy damage budget;
Q1 has fixture support.

#### Consequences (lead with negatives)

- *Negative*: SmallSlimes coverage removed from oracle. The 2-medium-variant
  signature ({LeafSlimeM/S, LeafSlimeS/TwigSlimeM, TwigSlimeS}) now rejects
  through the AdapterReject path; Q12 oracle-agreement rate for SmallSlimes
  encounters drops to 0% (was already lagging per Q2-ADR-029).
- *Negative*: Wave-22.γ adapter projection + wave-22-fix-3 horizon work +
  wave-22-fix-4 layered (h) implementation are partially-credited investments:
  the infrastructure ships + benefits future encounters, but the originating
  SmallSlimes regression-pin coverage goal is unmet.
- *Negative*: Cultist Zobrist BYTE IDENTITY rotation event; future reviewers
  must verify SOLVE VALUES (not byte values) for cultist regression baseline.
- *Negative*: LRU memory footprint ~96B/entry vs 70B projection — TT cap
  budget tighter than expected.
- *Positive*: Method (h) infrastructure (Slimed cap + LRU eviction +
  compression) retained + correct; benefits future encounters.
- *Positive*: Cultist + Louse SOLVE VALUES BIT-IDENTICAL — zero risk to
  pinned regression baseline.
- *Positive*: `kCapExceeded` no longer a possible failure mode in Search — LRU
  is a safety net for any future encounter approaching the TT cap.
- *Positive*: Sustainable encounter-selection criteria now documented: bounded
  combat duration; no unbounded status-card accumulation; not-block-dominated.

#### Cross-references

- Q2-ADR-013 Amendment 1 (Blocker #3 surfacing)
- Q2-ADR-013 Amendment 2 (horizon-cap mechanism)
- Q2-ADR-013 Amendment 3 (horizon 50→25; empirical depth-cap-exhaustion)
- Q2-ADR-010 (Zobrist hash-only TT + §Recovery for byte-identity rotation)
- Q2-ADR-011 (TT cap-policy + lifecycle — kCapExceeded retired by Amendment 4)
- Q2-ADR-005 (algorithm_sha source-list audit)
- Q2-ADR-029 (Q1-divergence-acknowledged philosophy; SmallSlimes row → DEPRECATED-IN-Q2)

### Amendment 5 (2026-05-18) — LRU eviction reverted

Wave-23 reverts the LRU eviction policy introduced by wave-22-fix-4 / H.β.
Rationale: H.ε deprecated SmallSlimes from oracle support — the only
encounter that would have benefited from eviction. For retained pinned
encounters (cultist + Louse), the LRU bookkeeping introduced pure overhead:

- Cultist peak RSS regressed 6.2 GB → 10.3 GB (+4 GB; per-entry footprint
  measured ~96 B vs 70 B design projection — std::list node + iterator-
  stored-in-map overhead).
- Cultist wall-clock 46.5 s → 56.7 s (+22%).
- TT never approaches the 200M cap for these encounters (cultist tt_size
  ~85M).

Restored:
- `Search::TtData = absl::flat_hash_map<ZobristKey, Score, ZobristKeyHash>`
  (dropped `std::list<ZobristKey>` LRU structure + pair<Score, iterator>
  value type).
- `tt_insert` hard-abort: sets `cap_hit_` flag + returns false at cap.
- kMaxTtEntries 200M → 370M (back to wave-22-fix-3 baseline).
- `derive_best_action(const Search&, ...)` signature (was non-const Search&
  for re-solve-on-miss).
- `cap_hit_` flag + `cap_hit()` accessor; `SolveStatus::kCapExceeded`
  re-instated as a possible solve outcome.
- `action_value`'s peek_score-assert pattern (re-solve fallback deleted).

Retired:
- `eviction_count_` field + accessor.
- `peek_score_by_key_for_testing` + `tt_insert_for_testing` public test
  hooks.
- `Search.LRU_EvictionFiresAtCap` + `Search.LRU_DeterminismDualRun` tests
  (deleted; reference data structures that no longer exist).

#### Consequences (lead with negatives)

- *Negative*: `SolveStatus::kCapExceeded` re-introduced as a failure mode.
  Mitigation: future encounter selection per Q2-ADR-013 Amendment 4
  §SmallSlimes-deprecation + the 4-criterion screen (bounded combat
  duration, no unbounded status-card accumulation, player-block does NOT
  dominate enemy damage budget, Q1 fixture support). If a future encounter
  triggers cap, project-lead decides between Amendment 5-rebuttal (re-
  introduce LRU) vs encounter-side mitigation.
- *Positive*: cultist peak RSS recovered (~6.5 GB at wave-23 close vs 10.3
  GB at fix-4 close); ~25% wall-clock improvement; substrate simplicity
  restored.

#### Verification

- Cultist pin VALUES BIT-IDENTICAL (`expected_hp=40.9083`,
  `expected_rounds=6.45798`, `tt_size=84790480`).
- Louse pin VALUES BIT-IDENTICAL (`expected_hp=0.0407931`,
  `expected_rounds=10.152`).
- `Search.LRU_*` filter returns 0 tests.
- `Transition.SlimedCap_*` retained (3 PASS).
- sts2_simulator_tests: 517 PASSED / 3 SKIPPED / 0 FAILED.
- sts2_oracle_tests: 55 PASSED / 0 FAILED.

#### Cross-references

- Q2-ADR-013 Amendment 4 §SmallSlimes-deprecation (motivating context).
- Q2-ADR-011 (TT cap-policy + lifecycle — kCapExceeded restored).
- `[[project-encounter-tractability]]` memory (encounter selection criteria).

---

## Q2-ADR-014 — Restore upstream stat widths (wave-23, 2026-05-18)

**Status**: Accepted (2026-05-18).
**Driver**: Q2 narrowed several stat-storage types (`int16_t` for damage +
power stacks; `uint8_t` for HP via Stat::pack8 + card counts; `uint16_t` for
round) vs upstream STS2 (Godot/C# at `/home/clydew372/development/projects/godot/sts2/src/Core/`)
which uses uniform `int` (32-bit signed) for all combat stats. Phase-1
encounters fit the narrow widths, but the divergence is obscure + would
silently truncate or assert for Phase-2+ encounters with larger values
(e.g., SlimedBerserker A0 HP 261-281 already exceeds Stat::pack8's [0,255]
bound).

### Decision

Widen all stat-storage types to `int32_t` to match upstream `int` exactly.
Widen Stat::pack8 → pack16 (uint16_t; assert v ∈ [0, 65535]). Widen Zobrist
key tables correspondingly. The widening rotates the cultist Zobrist BYTE
identity (third rotation since pre-wave-21; see chain below) but preserves
all pin VALUES (search algorithm is invariant within reachable stat range).

### Upstream audit (Phase-1 Explore against /home/clydew372/development/projects/godot/sts2/src/Core/)

| Stat | Upstream type | Q2 pre-(j) | Q2 post-(j) | Divergence resolved? |
|---|---|---|---|---|
| HP (current + max) | int | Stat (int32 + pack8) | Stat (int32 + pack16) | YES |
| Block | int | uint8_t direct | int32_t direct | YES |
| Energy | int | int | int | (already aligned) |
| Power stacks (Amount) | int | int16_t | int32_t | YES |
| Card cost (energy) | int | int | int | (already aligned) |
| Damage values | int | int16_t | int32_t | YES |
| Round counter | int | uint16_t | int32_t | YES |
| Status card count | int | uint8_t | int32_t | YES |

No `uint*` types in upstream combat stats. No Stat wrapper class in
upstream — Q2 retains its Stat wrapper (clamping + ostream semantics over
int32_t backing).

### Sites widened

- `engine/cpp/include/sts2/game/stat.h`: `pack8() → pack16()`.
- `engine/cpp/include/sts2/game/monster_moves.h`:
  - `MoveEffect.value`: int16 → int32; sizeof 6B → 8B.
  - `SpawnPowerEntry.stacks`: int16 → int32; sizeof 4B → 8B.
  - `MonsterMoveTable.min_hp` / `max_hp`: uint8 → int32.
- `engine/cpp/include/sts2/ai/state.h`:
  - `PowerInstance.stacks`: int16 → int32; reordered for natural alignment;
    sizeof 6B → 8B.
  - `CardCounts.counts[]`: uint8 → int32; sizeof 5B → 20B (+15B per zone).
  - `CompactState.round_`: uint16 → int32.
- `engine/cpp/src/ai/zobrist.cc`:
  - `kMaxHp` 256 → 1024 (covers SlimedBerserker 281 + headroom).
  - `kMaxBlock` 256 → 1024 (symmetric).
  - `kMaxStacks` 100 → 256 (modest headroom).
  - `kMaxCountPerCardZone` 16 → 64 (Slimed cap=8 + Silent starter 12 + headroom).
- `cmake/AlgorithmSha.cmake`: `engine/cpp/include/sts2/game/stat.h` ADDED to
  `ALGORITHM_SHA_SOURCES` per Q2-ADR-005 amendment (this change).

### pack_counts retirement

`engine/cpp/include/sts2/ai/state.h`'s `static_assert(std::size(kCountedCardIds) <= 8, "pack_counts uint64 packing limit (8 bits per slot)")` was incompatible with int32 counts (32 bits × 5 slots = 160 bits > 64-bit pack target). Engineer audit found NO consumers of pack_counts in `src/` or `tests/` — the static_assert guarded a hypothetical packing helper that was never implemented. The static_assert was DELETED (no widening of a non-existent helper required).

### Narrow-arithmetic audit

J.β engineer ran `grep` audits for `static_cast<int16_t>(`, `static_cast<uint8_t>(`, `int16_t{`, `uint8_t{` across src/ + tests/ + include/. Findings:
- Total hits: ~65 (22 static_cast + 43 brace-init).
- STAT-ARITHMETIC widened: 19 (powers helpers in state.h, builder casts, transition mutators, projection casts in louse_progenitor_projection.cc + cultists_projection.cc, probability.cc, test_helpers.h, test_probability.cc, test_zobrist.cc, test_louse_progenitor_projection.cc, test_monster_moves_table.cc literals, test_stat.cc pack8→pack16).
- INDEX/PROTOCOL/ENUM-CAST preserved: 31 (uint8_t bitmasks `~0x01U`, MoveId/MonsterKind/Phase/MoveEffectKind/CardId enum→uint8_t indexing for Zobrist + curl-up-stamp, `find_move_index` 0xFF sentinel, `follow_up_index`/`branch_weights` protocol fields, `enemy_count_` uint8_t).

### Cultist Zobrist BYTE rotation chain (cite Q2-ADR-010 §Recovery)

The wave-23 stat widening is the THIRD cultist Zobrist BYTE rotation in two weeks. Reviewers must distinguish BYTE drift (cosmetic; recoverable via re-stamp) from VALUE drift (regression; bisect):

| Rotation event | kCultistZobristKeyLo | kCultistZobristKeyHi |
|---|---|---|
| Pre-wave-21 (initial pin) | 0xf812af56366b5548 | 0x2c51edb8b6bd404e |
| Post-wave-22-fix-4 / H.γ (compression: dsb+ritual table drop + PowerKind uint8_t) | 0x471665c4838c298d | 0x770eab2147499e6c |
| Post-wave-23 / J.β (stat widening: pack8→pack16 + kMax* widening) | **0x569115efa81a95dc** | **0x9a06f1e505846a80** |

Each rotation re-stamped `engine/cpp/tests/seeds/cultist_zobrist_pin.h` via the `WaveDiagnostic.DISABLED_DumpCultistZobristKey` diagnostic. Per Q2-ADR-010 §Recovery, BYTE rotation is a recoverable event; VALUE invariance is the load-bearing regression baseline.

### Consequences (lead with negatives)

- *Negative*: `algorithm_sha` rotates AGAIN (third time in wave-22-fix-4/H.γ + wave-23/J.β); any consumer hardcoding the prior SHA breaks. Mitigation: per fix-4 grep findings, the sole consumer is the manifest stamp inside pin tests, which dereferences via `current_manifest().algorithm_sha` at runtime — no hardcoded value to update.
- *Negative*: cultist Zobrist BYTE rotates AGAIN. Reviewer confusion risk (BYTE vs VALUE). Mitigation: this ADR section documents the rotation chain explicitly.
- *Negative*: CardCounts size grows 5B → 20B; CompactState gains +45B per state (3 CardCounts × +15B); stack frames during recursion ~10-15% larger. Cultist's wall-clock recovery (~25% vs post-(h)) was driven by J.α LRU revert, not J.β widening — widening alone may be a slight wash. Acceptable tradeoff per upstream-emulation directive.
- *Negative*: Zobrist key tables grow ~4 MB in static memory (still trivial vs TT working set).
- *Positive*: Q2 stat storage now matches upstream byte-for-byte semantics; no risk of silent truncation for Phase-2+ encounters with larger stat values.
- *Positive*: Stat::pack16 with assert [0, 65535] gives headroom up to 65535 (vs prior 255); covers SlimedBerserker + realistic Phase-2 bosses.
- *Positive*: Cultist + Louse SOLVE VALUES BIT-IDENTICAL — zero risk to pinned regression baseline.
- *Positive*: pack_counts retired (latent dead static_assert removed).
- *Positive*: narrow-arithmetic audit found + classified ~65 sites cleanly; no silent truncations lurked.

### Cross-references

- Q2-ADR-005 (algorithm_sha source list; this ADR is the source of the stat.h addition).
- Q2-ADR-010 (Zobrist hash-only TT + §Recovery for byte-identity rotation).
- Q2-ADR-013 Amendment 4 §Compression (prior compression which rotated cultist BYTE for unrelated reasons).
- Q2-ADR-013 Amendment 5 (LRU revert; concurrent wave-23 stream).
- Upstream STS2 src at `/home/clydew372/development/projects/godot/sts2/src/Core/` (Phase-1 Explore audit source).
