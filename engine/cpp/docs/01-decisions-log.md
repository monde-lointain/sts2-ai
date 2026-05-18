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

**Cross-references.** Q2-ADR-002 (Path A scope). Q2-ADR-006 (polymorphic power-hook framework — wave-21 does not touch power hooks; only the `monster_moves` table shape). Q2-ADR-010 (Zobrist hash-only TT; §Recovery seed re-roll path remains the recourse if cultist hash byte identity ever needs re-capture). Q2-ADR-011 (`absl::flat_hash_map` + cap).
