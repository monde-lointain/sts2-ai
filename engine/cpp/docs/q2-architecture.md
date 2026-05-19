# Q2 Oracle Verifier — Architecture Brief

> Bridges the `engine/cpp/` expectimax prototype into the Q1–Q12 pipeline as Q2
> per pipeline ADR-011. Thin by design — substrate is mostly built; this brief
> maps `oracle.md` responsibilities onto existing code, names the new modules,
> and pins the 5-stage plan refinement. See `01-decisions-log.md` for the
> Q2-ADRs that ratify each decision below.

## 1. Substrate inventory

| Artifact | Path | State |
|---|---|---|
| Expectimax + TT search | `engine/cpp/include/sts2/ai/{search,state,transition,probability,recommend}.h` | operational |
| Test battery | `engine/cpp/tests/` | 424+ active, 100% green (extended by wave-16 framework tests) |
| Pinned regression | `engine/cpp/tools/seed-pinner/` + `tests/seeds/expected_values.h` | CULTISTS_NORMAL only; algorithm_sha rotated at wave-16 (values numerically unchanged) |
| Build | top-level `CMakeLists.txt`, `CMakePresets.json`, single ALIAS target `sts2::simulator` | clean |
| M1 binary state spec | `engine/headless/docs/specs/modules/state-codec.md` | locked, schema v3 minor |
| StateBlobEnvelope v0.1 | `contracts/schemas/game-simulator/state_blob.proto` | locked per Q1-ADR-012 |
| Generated cpp bindings | `contracts/generated/cpp/game-simulator/{state_blob,hook}_pb.h` | **STUB ONLY** — empty struct decls, no codegen |
| D3 fixture corpus | `engine/headless/test/fixtures/state-blobs/` | 6 fixtures, byte-locked, canonical hashes pinned |
| Monorepo data convention | `data/{eval-harness,experience-store,inference-server,model-registry,observability,rollout-workers,trainer}/` | per-service subdirs exist; `data/oracle/` open |
| **Power-hook framework** (wave-16) | `engine/cpp/include/sts2/game/powers.h`, `engine/cpp/src/game/powers.cc` | `PowerKind` enum + `PowerInstance` POD + helpers; cultist re-expressed as data |
| **Monster move tables** (wave-16/17) | `engine/cpp/include/sts2/game/monster_moves.h`, `engine/cpp/src/game/monster_moves.cc` | `constexpr kMonsterMoveTables[MonsterKind]`; cultist entries + LouseProgenitorNormal entry populated (wave-17); HP 134–136, WEB_CANNON→CURL_AND_GROW→POUNCE rotation, spawn-power CurlUp(14) |
| **Damage/block pipeline** (wave-16) | `engine/cpp/include/sts2/game/damage.h`, `engine/cpp/src/game/damage.cc` | `compute_outgoing_attack` + `compute_outgoing_block`; pure helpers; STS-canonical floor-rounding |
| **CurlUp + Frail power hooks** (wave-17) | `engine/cpp/src/ai/transition.cc` | `hook_curl_up` (two-trigger: kAfterDamageReceived + kAfterCardPlayedFinished) + `hook_frail` (kBeforeBlockGain + kAtEnemyTurnEnd tick-down); per upstream `CurlUpPower.cs:14-71` + `FrailPower.cs:22-41` |
| **LouseProgenitor adapter projection** (wave-17) | `engine/cpp/src/oracle/adapter/louse_progenitor_projection.cc` | recognizes `LouseProgenitorNormal` encounter signature; synthesizes CurlUp(14) spawn-power if wire blob omits it (Q2-ADR-005 silent-drop pattern); sets `kind_=kLouseProgenitor` + `move_index_` via `find_move_index` |
| **LouseProgenitor pinned seed** (wave-17) | `engine/cpp/tests/seeds/expected_values.h` | D3 fixture #5 (`LouseProgenitorNormal` seed=42) added; `algorithm_sha` regenerated; cultist values numerically unchanged |

The wave-16 refactor generalizes the engine substrate from cultist-hardcoded to a data-driven framework where future encounters (LouseProgenitor in wave-17; remaining Phase-1 pool per ADR-029) are added as data table entries and per-`PowerKind` hook functions, not as new switch arms in transition code. Cultist behavior is preserved byte-for-byte at the oracle-value level; the structural representation rotates.

## 2. `oracle.md` responsibility → code mapping

| Responsibility (oracle.md §Responsibilities) | Existing | New for Phase-1A |
|---|---|---|
| Expectimax over `CompactState` → `(value, action)` | `sts2::ai::Search` | preserve (no change) |
| Engine→`CompactState` adapter (ADR-011) | — | `sts2::oracle::adapter` (S1) |
| Oracle-agreement signal | — | Parquet sink at `data/oracle/agreement/` (S3) |
| Pinned-seed regression set | `tools/seed-pinner` + `expected_values.h` | per-encounter pin registry (S2; cultists only Phase-1A) |
| `verify()` RPC for Q12 | — | JSON-over-Unix-socket cold-path RPC (S4) |

Q2-internal types — `CompactState`, `EnemyState`, `CardCounts` — remain owned
in `sts2::ai`. Adapter (in `sts2::oracle::adapter`) consumes the Q1→Q2 wire
and produces these types. See Q2-ADR-001.

## 3. Substrate boundaries — honesty section

Post-wave-17, the C++ substrate implements the Phase-1 mechanics framework with the first two encounters fully covered: CULTISTS_NORMAL (proof-of-concept, wave-16) and LouseProgenitorNormal (first non-cultist, wave-17). The former CULTISTS_NORMAL-only framing (Q2-ADR-002) is superseded by Q2-ADR-006; per-encounter coverage extends via Q2-ADR-007 data-table additions per the Path A campaign (ADR-029). Wave-17 completes the foundation (shape refactor + `CurlUpPower` + `FrailPower` hooks + LouseProgenitor encounter) — the remaining 14 encounters on the Path A queue proceed as data-only additions per wave-18+.

Pre-wave-16 structural constraints (now resolved by the refactor):

- `state.h` `EnemyState` cultist-specific fields (`dark_strike_base_`, `ritual_amount_`, `just_applied_ritual_`) → replaced by generic `MonsterKind` byte + `PowerInstance[]` array + `kMonsterMoveTables` data.
- `enemies`: `std::array<EnemyState, 2>` hardcoded → widened to `std::array<EnemyState, kMaxEnemies=4>` + `uint8_t enemy_count_`.
- `CardCounts`: `static_assert(kCountedCardIds.size() <= 8)` with 4 distinct kinds — unchanged (Silent starter fits; Phase-2 scope).
- `game::enemies`: `make_calcified_cultist` / `make_damp_cultist` → rewritten to populate via `MonsterKind` + `kMonsterMoveTables`.

Remaining boundary (unresolved in wave-16; deferred to subsequent encounter waves):

- Non-cultist encounters still reject with `UnsupportedEncounter` until the corresponding `MonsterMoveTable` entry and hook functions are populated. LouseProgenitor is complete (wave-17, Q2-ADR-009). FossilStalker, KaiserCrab, SmallSlimes are wave-18+.
- `CardCounts` 8-kind cap — Silent starter fits; future deck expansion is Phase-2 scope.
- Ascension scaling — Phase-1A fixed at A0.

### D3 fixture corpus implication

Of 6 fixtures shipped in `engine/headless/test/fixtures/state-blobs/`:

| # | Encounter | Adapter feasible? | S1 path |
|---|---|---|---|
| 1 | CultistsNormal (Calcified + Damp) | YES | adapter → expectimax → pinned `(action, value)` round-trip |
| 2 | FossilStalkerElite seed 42 | NO (wave-18+) | reject with `UnsupportedEncounter` diagnostic |
| 3 | FossilStalkerElite seed 1337 | NO (wave-18+) | reject with `UnsupportedEncounter` diagnostic |
| 4 | KaiserCrabBoss (Crusher + Rocket) | NO (wave-18+) | reject + unknown-power diagnostic (Q2-ADR-005) |
| 5 | LouseProgenitorNormal | YES (wave-17) | adapter → expectimax → pinned `(action, value)` round-trip (Q2-ADR-009) |
| 6 | SmallSlimes | NO (wave-18+) | reject (also B.1-ε DEFER per Q1 fixture README) |

The reject-with-diagnostic path remains correct for encounters not yet in the engine. Wave-17 narrows the reject set by 1 (fixture #5). See §6 and ADR-029 for campaign roadmap.

## 4. Cpp proto bindings — substrate gap

`contracts/generated/cpp/game-simulator/{state_blob,hook}_pb.h` contain empty
`struct StateBlobEnvelope {}` and `struct {SchemaVersion,LegalAction,DecisionRequest,DecisionResponse} {}` —
placeholders, no codegen. Adapter cannot link against them.

Lead's Unresolved #2 confirmed *negatively*: cpp codegen is stub-only.

**Decision (Q2-ADR-001):** hand-roll proto3 wire-parse for `StateBlobEnvelope`
(7 fields, no nested messages, varint+length-prefix only) and hand-roll M1
binary payload reader per `state-codec.md`. Both surfaces are fully documented.
No protobuf runtime in C++ build. Forward-compatible if real codegen lands —
`sts2::oracle::adapter` is the seam.

Surfaced to project lead as a contracts-generation gap (not Q2's to fix).

**Wave-16 note.** The `CompactState` type widens (per ADR-004 Amendment + Q2-ADR-006/007) but remains Q2-internal. No change to `contracts/schemas/` is required. The `StateBlobEnvelope` wire format and `state-codec.md` M1 binary layout are unaffected. The adapter seam (`sts2::oracle::adapter`) remains the single point where Q1 wire bytes are translated to the new `CompactState` shape.

## 5. Refined 5-stage plan

### S0 — Architecture brief + Q2-ADRs (this commit)

- T1: this brief (`q2-architecture.md`).
- T2: Q2-ADR log (`01-decisions-log.md`) with 5 ADRs covering the queue.
- Wall: 2–3 d. In progress; status report follows.

### S1 — Engine→CompactState adapter (ADR-011)

- T1: M1 binary payload reader (`sts2::oracle::adapter::read_state_blob`) —
  header, sections, trailer SHA-256 validation per `state-codec.md`.
- T2: `StateBlobEnvelope` proto3 hand-parser + 5-tuple validation (schema,
  game_version, simulator_build_sha, registry_sha, payload_sha256).
- T3: Encounter detection from the M1 CombatState section's monster IDs;
  CULTISTS_NORMAL projection onto `CompactState`.
- T4: Non-cultist encounter reject path with `UnsupportedEncounter` diagnostic
  (Q2-ADR-002). Q2-ADR-005 unknown-power diagnostic at this layer.
- T5: Fixture #1 round-trip test — adapter → expectimax → pinned expected
  `(action, value)`. New file under `engine/cpp/tests/oracle/`.
- T6: Fixtures #2–#6 reject-with-diagnostic tests (per-fixture diagnostic
  shape pinned).
- Wall: 4–6 d.

### S2 — Per-encounter pin registry (CULTISTS_NORMAL only Phase-1A)

- T1: Generalize `seed-pinner` from single-encounter to
  `(encounter_id, seed) → pinned values` map. Keep `expected_values.h`
  back-compat as a generated emit target.
- T2: Stamp Q4 registry SHA + algorithm SHA + scoring rule SHA per pinned row
  (Q2-ADR-005).
- T3: Extend within-CULTISTS_NORMAL seed coverage targeting decision-boundary
  diversity (round-1 draw, lethal positions, mid-combat with ritual stacks).
- Wall: 3–5 d.

### S3 — Oracle-agreement reporting

- T1: Schema for `(state_hash, oracle_action, oracle_value, model_action, model_value, model_version, algorithm_sha, registry_sha, simulator_build_sha, expansion_complete, timestamp_ms)`
  rows. Q2-ADR-004.
- T2: Apache Arrow Parquet sink at `data/oracle/agreement/<partition>/<file>.parquet`.
  Per-`(model_version, day)` partitioning.
- T3: Wire the algorithm manifest into every row (Q2-ADR-005).
- Wall: 3–4 d.

### S4 — `verify()` RPC

- T1: JSON-over-Unix-socket cold-path server at
  `engine/cpp/tools/oracle-verify-server/`. Q2-ADR-003.
- T2: `verify(state_blob, budget) → {value, action, expansion_complete, simulator_manifest_echo}`.
- T3: 5-tuple echo from input envelope so Q12 (when booted) can correlate
  rows back to the producing Q1 build.
- Wall: 2–3 d.

**Total wall ≈ 12–18 d. Matches lead's 2–3 weeks budget within rounding.**

## 6. Plan diff from starting framework

| Stage | Starting (S0) | Refined (post-wave-16) | Driver |
|---|---|---|---|
| S1 | "each of 6 D3 fixtures → expectimax → pinned value" | Fixture #1 round-trip; #2–#6 reject-with-diagnostic (narrows per ADR-029 campaign) | Substrate boundary at S0; framework now extensible |
| S2 | "Q1's 16-encounter corpus (22+ post-Phase-1.5)" | CULTISTS_NORMAL Phase-1A baseline; per-encounter extension via ADR-029 campaign | same substrate boundary at S0; wave-16 removes the structural block |
| S0 | "3–5 Q2-ADRs" | 8 Q2-ADRs (Q2-ADR-001..005 original + Q2-ADR-006..008 wave-16) | wave-16 framework ratification |
| S3, S4 | (no change) | (no change) | — |

Wave-16 vs. original S0 plan delta summary:

- **Not in S0:** polymorphic `EnemyState` + generic `PowerInstance[]` + `HookPoint` dispatch framework (Q2-ADR-006). S0 had no mechanism for non-cultist powers.
- **Not in S0:** `constexpr kMonsterMoveTables[MonsterKind]` data-driven rotation (Q2-ADR-007). S0 hardcoded cultist moves in `transition.cc`.
- **Not in S0:** `compute_outgoing_attack` / `compute_outgoing_block` extracted pure helpers (Q2-ADR-008). S0 had inline formula at each use-site.
- **Not in S0:** `kMaxEnemies=4` enemy slot widening. S0 had hardcoded `std::array<EnemyState, 2>`.
- **Preserved from S0:** `CompactState` remains Q2-internal. `contracts/schemas/` unchanged. Hand-rolled adapter (Q2-ADR-001). Algorithm manifest (Q2-ADR-005). All S3 / S4 designs unchanged.

S1 and S2 diffs from S0 were **material** and correctly flagged in the original §6. Wave-16 closes the structural block that made those diffs necessary; Path A campaign (ADR-029) is the follow-through.

## 7. Open questions for project lead (S0 → status)

1. **Ratify Q2-ADR-002 scope reduction** (Phase-1A adapter = CULTISTS_NORMAL
   only; non-cultist D3 fixtures hit reject-with-diagnostic path, not
   round-trip), OR direct Path A (Q2 expands `engine/cpp/` to cover Phase-1
   non-cultist encounter mechanics — multi-week effort, ADR-revisits).
2. **Confirm hand-roll proto/wire strategy** (Q2-ADR-001) OR direct effort
   at fixing the contracts-generation pipeline for cpp first. Stub-only
   cpp bindings is a monorepo-level gap, not specifically Q2's to close.
3. **Phase-1.5+ multi-encounter regression set** is out of S0 scope by the
   substrate boundary above. Flagged here as a follow-up tied to either
   engine expansion (Path A) or a Q2-as-frontend-to-Q1 redesign (Path C).
   No action requested in S0; surface for future planning.

## 8. Anchors

Decisions in this brief reference:

- Pipeline ADRs: ADR-001 (versioned schema migrations), ADR-004
  (CompactState canonical, owned by Q2), ADR-006 (oracle-agreement as
  Q10 prioritization input), ADR-011 (adapter ownership).
- Q1-ADRs: Q1-ADR-012 (v0.1 schema lock, CHANCE-on-wire deferred to v1),
  Q1-ADR-013 (caller-callee gap mitigation — process Q2 inherits).
- Scaling strategy: §1.3 (prototype assumptions that break), §1.5
  (representation strategy default), §4.2 (CompactState vs RichState),
  §4.6 (oracle-agreement as Phase 1 gate signal), §8.4 R-N
  (project-level risks).
- Module spec: `docs/specs/modules/oracle.md` (responsibilities, data
  ownership, communication, coupling, open risks).

## 9. Cross-quantum compositional invariant (R6 dual-path)

S2 surfaced an emergent compositional invariant: the CULTISTS_NORMAL combat
at seed `0xC0FFEE` resolves to **bit-identical** expectimax values via two
independent code paths.

| Path | Where pinned | Code-path summary |
|---|---|---|
| Q1 direct | `engine/cpp/tests/test_search.cc :: Search.DISABLED_StarterCombatSolves_LogsDiagnostics` | hardcoded `from_combat()` init → expectimax → pinned `(expected_hp, expected_rounds)` |
| Q2 adapter | `engine/cpp/tests/oracle/test_cultists_search_pins.cc :: CultistsSearchPins.DISABLED_StarterCombatSeedC0ffee_PinnedAgreement` | D3 fixture `01-cultists-normal-seed42` bytes → `adapter::from_blob_payload` → `CompactState` → expectimax → same pinned `(expected_hp, expected_rounds)` |

Pinned values, 2026-05-12 (Linux x86_64 GCC + libstdc++):
`expected_hp = 40.90829202578665`, `expected_rounds = 6.4579809748486445`.

**Why this matters.** This is exactly the localization chain R6 (probe
localization) was meant to provide. The two paths share only the
expectimax + transition + state implementation; they differ on **how
they reach the initial `CompactState`**. If these ever drift in the
same toolchain:

- Same expected_hp + rounds in both → no regression in the shared
  algorithm core. Drift is upstream of the pin (registry version,
  encounter content).
- Q1-path drift but Q2-path stable → regression in `from_combat()` or
  related cultist-init Q1 helpers.
- Q2-path drift but Q1-path stable → regression in the Q2 adapter (M1
  decoder, envelope parser, or `cultists_projection`).
- Both paths drift identically → regression in expectimax / transition /
  state / scoring (i.e., `algorithm_sha` change per Q2-ADR-005).

Both pins fire from the `make q2-ci` Release wave gate. Q2 surfaces
diagnostic-class divergence as a top-line concern in its next status to
the project lead — divergence is never silent.

**Forward-laid:** as Q1 Phase-1.5 adds the BowlbugsTrio encounter, the
parallel invariant extends per-encounter. The Q2 per-encounter pin
registry (S2-T2 onwards) is the data-shape vehicle; the dual-path
invariant scales 1:1 with each new pinned encounter that exists in
both Q1's direct-init helpers and the Q2 adapter scope (currently
CULTISTS_NORMAL only; Q2-ADR-002).

## 10. Reproducibility / evidence-collection notes

Q2's Python tooling is **evidence-collection-only** (status-report
artifacts; cross-format validation; ad-hoc inspection). It is *not*
production code. Phase-1A policy (lead-ratified 2026-05-12) is ad-hoc
venv installs per-need; a tracked `requirements.txt` is deferred to
Q10-boot when the Python consumer stack solidifies.

Per project memory: every Python invocation goes through `.venv/bin/python`
(never system `python3`).

**pyarrow schema cross-check (S3 evidence):**

```bash
.venv/bin/pip install pyarrow   # one-time per fresh checkout
.venv/bin/python - <<'PY'
import pyarrow.parquet as pq
t = pq.read_table('data/oracle/agreement/year=2025/month=05/day=12/model=phase1a-stub-model-sha.parquet')
print('schema:'); print(t.schema)
print('rows:', t.num_rows)
for i in range(t.num_rows):
    print(f'--- row {i} ---')
    for c in t.schema: print(f'  {c.name}:', t[c.name][i].as_py())
PY
```

Output reproduces the 15-column Q2-ADR-004 schema + 3 partition columns
(year/month/day auto-detected from the path). When schema-freeze gtest
(S4) lands, pyarrow becomes optional — the gtest enforces the same
property at build time. Pyarrow stays useful for inspecting real
production parquet captures during Phase-1A→Q10-boot operations.

## 11. Build-modes lesson (S3-T10 pinned)

S3-T0 wave-gate recipe used `$(MAKE) BUILD_TYPE=Release build` to force
Release. It silently failed when `$(BUILD_DIR)/CMakeCache.txt` already
existed with `CMAKE_BUILD_TYPE=Debug` — the child make passed
`BUILD_TYPE=Release` in its env, but cmake's *existing cache* won the
configure step. Wave gate then built Debug binaries and tried to invoke
`$(BUILD_DIR)/Release/sts2_*_tests`, which either did not exist or were
stale. S3-T10 fixed via dedicated `$(Q2_CI_BUILD_DIR) := build-q2-ci`
+ direct `cmake -B build-q2-ci -S . -DCMAKE_BUILD_TYPE=Release`
invocation. Self-contained; one-time configure cost per fresh checkout.

**Rule for future Makefile build-flavor targets** (sanitize-, ci-,
verify-server-debug, etc.):

- Each distinct flavor uses its own build dir.
- Configure via direct `cmake -B <dir> -S . -D<flavor flags>`, *not*
  `$(MAKE) BUILD_TYPE=X build`.
- Add the build dir to `distclean` so `make distclean` is exhaustive.

Recursive-make plus cmake-cache is a known foot-gun. Dedicated dir +
direct cmake invocation closes the trap.

## 12. Architecture-note cascade addendum (2026-05-14)


The pipeline-level architecture-note cascade
(`docs/micro-macro-policy-architecture-note.md`, commit `92acc33`) added
ADR-009 amendment + ADR-014..018 specifying a run-conditioned combat
outcome oracle interface. **Q2's scope is unaffected:**

- Q2's `verify()` RPC (S4) operates on `CompactState` and is **orthogonal
  to** the pipeline's `evaluate_combat()` interface. `verify()` answers
  "what does small-state expectimax conclude about this state?";
  `evaluate_combat()` answers "what does the run-conditioned combat
  policy expect when given observable run state + macro context?" Two
  different surfaces, two different callers (Q12 vs. Q8/Q9), no
  conflation.
- Oracle-agreement signal remains training-eligible per ADR-017's
  explicit carve-out: it is a labeled comparison between expectimax
  ground truth and model output, NOT a path-counterfactual.
  Q10 prioritized sampling continues to consume it per ADR-006.
- Q2-ADR-002 Phase-1A scope (CULTISTS_NORMAL only) holds; the cascade
  does not pressure the encounter scope.
- Q2-ADR-004 oracle-agreement Parquet schema's `model_value_*` columns
  reduce from `summary.expected_hp_delta` when the model's value head
  becomes `sample + summary` per ADR-014 — Phase-1A is scalar-against-
  scalar comparison; Q10 boot will revisit at the Q2-ADR-004 schema-
  promotion event (cross-quantum coordination per ADR-001).

No Phase-1A code change required. This addendum exists so future readers
do not conflate `verify()` with `evaluate_combat()` after reading the
cascade documents.

Note: the §12 statement "Q2-ADR-002 Phase-1A scope (CULTISTS_NORMAL only) holds; the cascade does not pressure the encounter scope" applied at the time of the cascade (2026-05-14). Wave-16 (2026-05-17) supersedes Q2-ADR-002 via Q2-ADR-006 — Path A campaign is now active. The cascade-vs-verify() orthogonality observation is unaffected.

## 13. Refactor cascade addendum (2026-05-17)

Wave-16 executes the substrate refactor documented in the plan at `~/.claude/plans/plan-the-q2-oracle-glittery-pony.md`. Key outcomes ratified by this wave:

- **Q2-ADR-006 ratified.** Polymorphic power-hook framework: `PowerKind` enum (stable order), `PowerInstance` POD, per-creature `std::array<PowerInstance, 6>` + `power_count`, per-`PowerKind` hook functions dispatched via `HookPoint` enum. `EnemyState` carries `MonsterKind` byte; no per-kind union. Player powers on `CompactState`.

- **Q2-ADR-007 ratified.** Data-driven `MonsterMoveTable`: `constexpr kMonsterMoveTables[MonsterKind]`, `MonsterMove` with sub-effects array (`kMaxEffectsPerMove=3`), `SpawnPowerEntry[]` per monster, `find_move_index` adapter helper. Constants: `kMaxEnemies=4`, `kMaxMovesPerMonster=6`, `kMaxEffectsPerMove=3`, `kMaxSpawnPowers=3`.

- **Q2-ADR-008 ratified.** `compute_outgoing_attack` + `compute_outgoing_block` extracted as pure helpers with STS-canonical floor-rounding. Multiplication order locked (strength → vulnerable → weak for attack; frail tax on powered-source block only).

- **Cultist behavior preserved.** All cultist oracle `solve()` values are numerically identical pre/post refactor. The `CultistSolveMatchesPreRefactor` regression test locks this invariant. Cultist enemies are now represented as `MonsterKind::kCultistCalcified` / `kCultistDamp` with their move data in `kMonsterMoveTables` entries; the structural representation changed, the values did not.

- **Framework reserves slots for wave-17.** `PowerKind` enum has `kCurlUp=3`, `kFrail=4`, `kVulnerable=5` reserved. `MonsterKind::kLouseProgenitor` slot reserved; `kMonsterMoveTables[kLouseProgenitor]` is zero-initialized (wave-17 populates). `MoveId` enum adds `kWebCannon`, `kCurlAndGrow`, `kPounce` (reserved; wave-17 uses them).

- **Path A campaign open.** ADR-029 declares the multi-wave expansion roadmap. Wave-17 is the immediate follow-on: `CurlUpPower` + `FrailPower` hooks + LouseProgenitor encounter + D3 fixture #5 round-trip.

## 14. Wave-17 cascade addendum (2026-05-17)

Wave-17 completes the foundation laid by wave-16 and ships the first non-cultist encounter end-to-end. Key outcomes:

- **Shape refactor landed.** `EnemyState` drops cultist-scalar fields (`dark_strike_base_`, `ritual_amount_`, `just_applied_ritual_`); carries `MonsterKind kind_`, `uint8_t move_index_`, `std::array<PowerInstance, 6> powers_`, `uint8_t power_count_`. `CompactState` gains `player_powers_` + `player_power_count_`. `enemies_` widened to `std::array<EnemyState, kMaxEnemies=4>` + `uint8_t enemy_count_`. `search.cc` `pack_enemy` / `pack_player` / `CompactStateHash` rewritten to hash generic power arrays.
- **Cultist regression preserved.** All cultist oracle `solve()` values bit-identical post-shape-refactor (`kSeedC0ffeeExpectedHp = 40.90829202578665`, `kSeedC0ffeeExpectedRounds = 6.4579809748486445` — unchanged). `CultistSolveMatchesPreRefactor` regression test locks this invariant. `algorithm_sha` rotates (structural representation changed); numerical values do not.
- **LouseProgenitor shipped.** `kMonsterMoveTables[kLouseProgenitor]` populated: 3-move rotation (WEB_CANNON→CURL_AND_GROW→POUNCE→WEB_CANNON), HP 134–136, spawn-power CurlUp(14). `hook_curl_up` (two-trigger per `CurlUpPower.cs:14-71`) + `hook_frail` (`ModifyBlockMultiplicative` + `AfterTurnEnd` tick-down per `FrailPower.cs:22-41`) implemented in `transition.cc`.
- **CurlUp semantics.** `kAfterDamageReceived` records the card source on the first powered-attack hit; `kAfterCardPlayedFinished` triggers block gain (amount=stacks) + power removal when the stored card resolves. Triggers once per combat. Multi-hit cards count once.
- **Frail semantics.** `kBeforeBlockGain` applies `(v * 3) / 4` floor when `gainer_frail && is_powered_source`. `kAtEnemyTurnEnd` decrements player Frail stacks by 1. STS2 semantics (block debuff), not STS1 semantics (attack debuff).
- **Adapter projection.** `louse_progenitor_projection.cc` added; recognizes `LouseProgenitorNormal` encounter signature; synthesizes CurlUp(14) spawn-power per Q2-ADR-005 silent-drop pattern.
- **D3 fixture #5 round-trip.** `LouseProgenitorNormal` seed=42 pinned-seed gtest added (DISABLED_ prefix; runs under `make q2-ci` slow regression). `expected_values.h` regenerated with new `algorithm_sha`; cultist entries numerically unchanged.
- **Q2-ADR-009 ratified.** Documents LouseProgenitor port; consequences include Q1/Q2 CurlUp+Frail semantic divergence (acceptable — Q2 is verifier), `algorithm_sha` rotation, and q2-ci wall-clock growth to ~25–35 min.
- **Path A queue.** 14 remaining Phase-1 encounters (FossilStalker, KaiserCrab, SmallSlimes, GremlinMerc, HauntedShip, ...) proceed per wave-18+ as data-only additions. The framework handles them without new transition-code changes.

## 15. Wave-19 Memory-Tightening Addendum (2026-05-18)

Wave-19 replaced the Q2 transposition table with a Zobrist 128-bit hash-only design backed by `absl::flat_hash_map`. Cultist peak RSS dropped from 24.4 GB → ~12 GB; slime fights now fit under the 16 GB ceiling.

**Architecture changes (cross-link Q2-ADR-010 + Q2-ADR-011):**
- TT type: `std::unordered_map<CompactState, SearchResult, CompactStateHash>` → `absl::flat_hash_map<ZobristKey, Score, ZobristKeyHash>`
- TT value: full `SearchResult` (Score + best_action + terminal) → bare `Score`. best_action re-derived in `recommend.cc`; terminal inferred via existing `transition::is_terminal(state)`.
- Capacity: pre-reserved at `Search` construction (`tt_.reserve(kMaxTtEntries)`); subsequent solves reuse via `clear()`.
- Cap mechanism: hard limit at 370M entries; `cap_hit_` flag + `SolveStatus::kCapExceeded`; flag-and-early-return preserves exception-safety.
- algorithm_sha source list (Q2-ADR-005) expanded to include `zobrist.cc`, Zobrist seeds, and `ABSL_VERSION_TAG` CMake variable.

**Best-action re-derivation flow (`recommend.cc` post-Zobrist):**
1. `solve()` populates TT with `Score` per state.
2. `derive_best_action(state) → Action`: enumerate legal actions via `chance.h::enumerate_legal_actions`; for each, compute expected value via `chance.h::enumerate_chance_outcomes` × cached child scores; pick first action whose expected value equals state's stored score (bit-exact FP equality holds — same computation path).
3. Recommend's PV walk: iterate `derive_best_action(pv_state)` + `transition::apply_player_action`, truncating at `is_terminal` OR first `EndTurn` action (chance event). Cost: ~5–10 steps × ~150 µs = ~1–2 ms per `recommend()`; negligible vs solve.

**Pre/post profiling methodology (plan §9):**
Profile script: `.claude/scripts/profile-cultist-solve.sh <out_json_path>` performs clean Release rebuild + `Search.DISABLED_StarterCombatSolves_LogsDiagnostics` via `/usr/bin/time -v`. Captures peak RSS (OS-authoritative), elapsed_ms, tt_size, expected_hp/rounds, algorithm_sha. Pre-wave baseline at `.claude/state/profiles/wave-19-pre.json` (~24.4 GB); post-wave validation diffs against baseline per §9 criteria.

**FP-determinism contract (Q2-ADR-010 §FP-determinism):** Q2 TUs compile with `-fno-fast-math` and without `-ffinite-math-only` / `-fassociative-math` / `-freciprocal-math` / `-fno-signed-zeros` / `-march=native`. Single-threaded; default FP rounding mode. Future flag changes = ADR amendment + cultist pin re-validation.

## 16. §16 — 2026-05-18 Wave-21 slime prerequisites addendum

Wave-21 widens the engine substrate to accommodate slime encounters. Three coordinated changes land together; none is independently mergeable without the others.

### kMaxEnemies bump (2 → 4)

`kMaxEnemies` raised to 4 in `state.h`. The rationale for deferring this bump to wave-21 (instead of wave-17 where it was originally planned) was TT memory pressure: pre-Zobrist, `CompactState` was the TT key, so widening the enemy array widened every TT entry. Post-Zobrist (wave-19), the TT stores only 128-bit `ZobristKey` → `Score` pairs at 38 B/entry; `CompactState` size no longer impacts TT memory. The only memory cost of the bump is the widening of the Zobrist key tables themselves.

**Memory cost analysis.**

| Component | Before wave-21 | After wave-21 |
|---|---|---|
| Zobrist key tables (static) | ~1.2 MB (2 enemy slots) | ~2.4 MB (4 enemy slots) |
| TT entries (dynamic, at cap) | 370M × 38 B ≈ 14 GB | unchanged — 38 B/entry is key+value, not CompactState |
| Expected peak_rss_gb | 6.19 GB (post-wave-20 profile) | ~6.3 GB (Zobrist table delta only) |

The 16 GB ceiling from Q2-ADR-010 is unaffected. Slime encounters (`SlimesWeak` = 3 enemies, `SlimesNormal` = 4 enemies) now fit within the allocated enemy-slot count.

### APPEND-ONLY constraint enforcement

Widening the Zobrist key tables from 2 to 4 enemy slots must not shift the PRNG outputs consumed for existing slots (0 and 1). The mt19937_64 fill sequence for each half seeds the tables in a deterministic order: slots 0 and 1 are filled first (consuming the same PRNG outputs as pre-wave-21), then slots 2 and 3 are filled from the continuation of the same sequence.

This APPEND-ONLY fill order is enforced by the **cultist ZobristKey byte-identity assertion** introduced in wave-21.β:

```cpp
// engine/cpp/tests/seeds/cultist_zobrist_pin.h
static constexpr uint64_t kCultistZobristKeyLo = 0xf812af56366b5548ULL;
static constexpr uint64_t kCultistZobristKeyHi = 0x2c51edb8b6bd404eULL;
```

The synthetic gtest `Zobrist.CultistRootKey_MatchesPreWave21Pin` asserts that `zobrist_of(canonical_cultist_state)` equals `ZobristKey{kCultistZobristKeyLo, kCultistZobristKeyHi}`. Failure means either the fill order regressed or the `fold_enemy` loop bound changed to include slots 2–3 for existing enemy features. Any failure is a rollback trigger for wave-21.

The same APPEND-ONLY discipline applies to all enum types whose values index into Zobrist tables:

### MonsterKind enum schema

```
kCultistCalcified = 0   // locked (wave-16)
kCultistDamp      = 1   // locked (wave-16)
kLouseProgenitor  = 2   // locked (wave-17)
kLeafSlimeS       = 3   // wave-21 (APPEND-ONLY)
kLeafSlimeM       = 4   // wave-21 (APPEND-ONLY)
kTwigSlimeS       = 5   // wave-21 (APPEND-ONLY)
kTwigSlimeM       = 6   // wave-21 (APPEND-ONLY)
kMonsterKindCount = 7
```

All future monster kinds must append. Inserting a value between existing entries shifts the Zobrist table slot mapping for all higher-valued kinds and breaks all pinned seeds that include those monsters.

### MoveId enum schema

```
kIncantation  = 0   // locked (wave-16)
kDarkStrike   = 1   // locked (wave-16)
kWebCannon    = 2   // locked (wave-17)
kCurlAndGrow  = 3   // locked (wave-17)
kPounce       = 4   // locked (wave-17)
kTackleMove   = 5   // wave-21 (APPEND-ONLY)
kGoopMove     = 6   // wave-21 (APPEND-ONLY)
kClumpShot    = 7   // wave-21 (APPEND-ONLY)
kStickyShot   = 8   // wave-21 (APPEND-ONLY)
kPokeyPounce  = 9   // wave-21 (APPEND-ONLY)
```

### FollowUpRule enum schema

`MonsterMove` gains a `FollowUpRule` discriminator governing how the next move is selected after the current move resolves:

```
kStrict                     = 0   // deterministic: follow_up_index scalar (existing behaviour)
kRandomBranchCannotRepeat   = 1   // uniform random from branch_indices[]; no repeat of last move
kWeightedRandomCannotRepeat = 2   // weighted random from branch_indices[]; no repeat of last move
```

`kStrict = 0` is zero-init default: all existing `MonsterMove` entries (cultist, `LouseProgenitor`) acquire `follow_up_rule = kStrict` via zero-initialisation with no semantic change. The per-branch fields (`branch_indices`, `branch_weights`, `branch_cannot_repeat`, `branch_count`) are also zero-initialised for existing entries and ignored when `follow_up_rule == kStrict`.

Future `FollowUpRule` values must append. `kStrict = 0` must never be reassigned.

### Factory stubs

Stub factories (`make_leaf_slime_s/m`, `make_twig_slime_s/m`) added in `enemies.cc` for wave-22 adapter projection to call. Each stub sets `kind_`, rolls HP per upstream A0 ranges, sets `alive = true`. Move-table population is deferred to wave-22.β; stubs are unreachable from any active adapter path until the wave-22 slime adapter projection lands.

## 17. §17 — 2026-05-18 Wave-22 SmallSlimes + Slimed card mechanics addendum

Wave-22 ports the `SmallSlimes` (`SlimesWeak`) encounter and introduces three substrate additions ratified in Q2-ADR-013: Slimed card injection mid-combat, Exhaust emulation, and a second chance phase for enemy-move RNG. See Q2-ADR-013 for full design rationale.

### Slimed card injection mid-combat

`CardId::kSlimed` is appended at value 5 (after `kStrike=1`, `kDefend=2`, `kNeutralize=3`, `kSurvivor=4`). APPEND-ONLY: inserting at a lower index corrupts cultist + LouseProgenitor `CardCounts` Zobrist hashes.

`kCountedCardIds` grows from size 4 to size 5; `kSlimed` is at index 4. The Zobrist `key_hand`, `key_draw`, and `key_discard` tables each widen by one row in the card-id dimension; the mt19937 fills the new row at the END of the card-counts sequence (APPEND-ONLY; cultist hash byte identity preserved).

`MoveEffectKind::kAddStatusCard` is a new APPEND-ONLY enum value. Slime status moves (GOOP_MOVE, STICKY_SHOT, STICKY_SHOT_MOVE) use this effect kind with `value = N`. When `do_enemy_act` processes a move with `kAddStatusCard`, it increments `state.discard[kSlimed] += effect.value`, mirroring `CardPileCmd.AddToCombatAndPreview<Slimed>(targets, PileType.Discard, N, null)` (CardPileCmd.cs:886-916).

### Exhaust emulation

Slimed is the first Exhaust-keyword card in Q2 (upstream `Slimed.cs:19`). A `bool exhaust_on_play` field is added at the END of the `CardEffect` struct, default `false`. All existing entries are unaffected.

In `do_play_card`, the discard increment is gated on the flag:

```cpp
hand[id]--;
if (!effect.exhaust_on_play) discard[id]++;
// exhaust_on_play=true: card removed from game; no discard increment
```

No exhaust pile is tracked in `CompactState`. The exhaust pile is dead state from the expectimax perspective: no Phase-1A card or power reads exhaust-pile contents to influence search decisions. A future ADR is required if that assumption breaks.

### Phase::kAtEnemyMoveRng + enumerate_chance_outcomes dispatch

`Phase::kAtEnemyMoveRng = 2` is appended to `{kPlayerActing=0, kAtChanceDraw=1}`. APPEND-ONLY: inserting at a lower value shifts `kAtChanceDraw` and breaks all cultist + LouseProgenitor pinned seeds.

The Zobrist phase table widens from 2 to 3 entries per half (~16 bytes; trivial); mt19937 fills the new entry at the END.

`chance.h::enumerate_chance_outcomes` dispatches on `Phase`:

- `kAtChanceDraw` → existing draw enumeration (wave-19 B.2-β; unchanged).
- `kAtEnemyMoveRng` → walk alive enemies; for each, look up `kMonsterMoveTables[kind].moves[current_move_idx].follow_up_rule`; if `kStrict`, the next move was already deterministically assigned (no branch); if `kRandomBranchCannotRepeat` or `kWeightedRandomCannotRepeat`, enumerate per-branch outcomes respecting CannotRepeat exclusion + weighted re-normalization.

Precise Phase ordering within a round:

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

When all enemies have deterministic follow-ups, `kAtEnemyMoveRng` is skipped; the transition goes directly to `kAtChanceDraw`. "Pending RNG" is DERIVED from the move table — no sentinel state in `CompactState`, no Zobrist key noise.

### CannotRepeat re-normalization rule

At `kAtEnemyMoveRng`, for an enemy with `follow_up_rule == kWeightedRandomCannotRepeat`:

1. Filter: exclude branch `i` if `branch_cannot_repeat[i] && branch_indices[i] == current_move_idx`.
2. Normalizer: `N = sum of remaining branch_weights[i]`.
3. Probability per remaining branch: `p_i = branch_weights[i] / N`.

For `kRandomBranchCannotRepeat` (LeafSlimeS): all branches have CannotRepeat; current move excluded; one remaining branch → deterministic alternation (p=1.0). For `kWeightedRandomCannotRepeat` (TwigSlimeM): after POKEY both branches eligible, N=3, POKEY p=2/3 STICKY p=1/3 (2 outcomes); after STICKY only POKEY eligible, N=2, POKEY p=1.0 (1 outcome).

Per-encounter contribution: only TwigSlimeM produces > 0 outcomes from `kAtEnemyMoveRng` (after POKEY only). SmallSlimes has at most 1 TwigSlimeM → cartesian product ≤ 2 outcomes per chance node. State-space growth bounded ~2× per round.

### SmallSlimes wire signature dispatch

`adapter.cc::encounter_map` carries TWO entries for SmallSlimes (one per medium-slime variant; sorted alphabetical per convention):

| Wire signature | encounter_id |
|---|---|
| `{LeafSlimeM, LeafSlimeS, TwigSlimeS}` | `SmallSlimes` |
| `{LeafSlimeS, TwigSlimeM, TwigSlimeS}` | `SmallSlimes` |

Both route to `project_small_slimes(...)`, which dispatches on wire-name presence of `"LeafSlimeM"` vs `"TwigSlimeM"` to assign each slime to the correct `CompactState` enemy slot per the `SlimesWeak.cs:48-59` slot ordering (slot 0 = RNG-picked small, slot 1 = RNG-picked medium, slot 2 = other small).

**Cross-reference.** Q2-ADR-012 contains the full design rationale, memory cost analysis, APPEND-ONLY contract, and consequence analysis for all wave-21 substrate changes.

## 18. §18 — 2026-05-18 Wave-22-fix-4 TT cap-management evolution + SmallSlimes deprecation

### TT cap-management evolution

The transposition-table cap policy evolved across four fix waves:

| Wave | Policy | Cap | Outcome |
|---|---|---|---|
| pre-fix-2 | hard-abort (`kCapExceeded`) | 370M | SmallSlimes SIGSEGV + abort |
| fix-2 | search horizon=50 | 370M | kCapExceeded @ 370M; 7m51s; 22.8 GB |
| fix-3 | horizon 50→25 | 370M | kCapExceeded @ 370M; 7m17s; 22.4 GB (breadth not depth) |
| fix-4 | LRU eviction + compression + Slimed cap | 200M | No abort; 40m+; 19.2 GB — LRU thrashing |

Conclusion: the SmallSlimes state-space breadth is the binding constraint in all
scenarios. Reducing horizon (depth) has minimal effect; breadth reduction via
Slimed cap + LRU helped correctness but not wall-clock.

### Method (h) infrastructure retained

The wave-22-fix-4 layered infrastructure remains on main:

- **Slimed cap** (`kMaxSlimedAccumulation = 8`): bounds discard[kSlimed] dimension; applies to any future encounter with status-card injection.
- **LRU eviction**: replaces hard-abort; `kCapExceeded` no longer a possible failure mode; TT miss triggers re-solve (correct for pure-function search).
- **CompactState compression**: PowerKind `int → uint8_t`; PowerInstance 8B → 6B; kMaxPowersPerCreature 6→4; Zobrist table savings ~2 KB.

Per-entry TT footprint empirically ~96 B (vs 70 B projected). `kMaxTtEntries` reduced 370M → 200M.

### SmallSlimes deprecation outcome

Per project-lead SECOND-decision (2026-05-18): SmallSlimes removed from the
oracle's supported encounters. Adapter dispatch entries removed; projection
module deleted; search-pin test tombstoned with GTEST_SKIP citing Amendment 4.
Fixture #6 now routes through AdapterReject with `reason = "encounter_not_in_cpp_engine"`.

### Future encounter selection criteria

Encounters selected for future port waves must satisfy:

1. **Bounded combat duration**: enemy damage budget (worst-case per-turn) exceeds player's chained-Defend block budget at A0. An encounter where the player can "all-Defend forever" without enemies dying is a tractability trap.
2. **No unbounded status-card accumulation**: encounters that inject growing card counts into discard (e.g. Slimed with no cap) produce unbounded state-space if combined with an all-Defend branch.
3. **Q1 has fixture support**: at minimum one wire fixture with STS2-era monster names (not STS1 aliases) must be available before pinning.

Roadmap candidates meeting these criteria (pending Q1 fixture verification):
HauntedShip, GremlinMerc, ThreeSlimes elite, SlimeBoss. Selection deferred to
project-lead at next port-wave planning.

**Cross-reference.** Q2-ADR-013 Amendment 4 (full rationale + Consequences).

## 19. §19 — 2026-05-18 Wave-23 stat widening + LRU retirement

### Stat-width architecture decision

Wave-23 / J.β widened all stat-storage types to `int32_t` to match upstream
STS2 (Godot/C# uses uniform `int` for all combat stats). The divergence was
obscure and would silently truncate or assert for Phase-2+ encounters with
larger values (e.g., SlimedBerserker A0 HP 261-281 exceeds the former
Stat::pack8 [0,255] bound).

**Stat wrapper retained.** Q2's `Stat` class (clamping + ostream semantics
over int32_t backing) is not present in upstream — retained for oracle
debugging clarity. The backing type is now `int32_t` throughout; pack8
widened to pack16 (uint16_t; assert v ∈ [0, 65535]).

**Zobrist dimensions widened correspondingly:**
- `kMaxHp` / `kMaxBlock`: 256 → 1024 (covers SlimedBerserker 281 + headroom).
- `kMaxStacks`: 100 → 256.
- `kMaxCountPerCardZone`: 16 → 64 (Slimed cap=8 + Silent starter 12 + headroom).

**Key table growth**: ~4 MB additional static memory — trivial vs TT working set.

**CompactState size impact**: CardCounts.counts[] uint8→int32 (+15B per zone ×
3 zones = +45B per state); recursion stack frames ~10-15% larger. The wave-23
wall-clock recovery (~25%) was driven by J.α LRU revert, not J.β widening.
Widening alone is a slight wash; acceptable per upstream-emulation directive.

**pack_counts retired.** The `static_assert(std::size(kCountedCardIds) <= 8)`
packing-limit guard was incompatible with 32-bit counts. Engineer audit found
no consumers of pack_counts in src/ or tests/; the static_assert was deleted.

**Cultist Zobrist BYTE rotated** (third rotation since pre-wave-21) to
`Lo=0x569115efa81a95dc / Hi=0x9a06f1e505846a80`. Pin VALUES bit-identical
per Q2-ADR-010 §Recovery invariant. See Q2-ADR-014 for the full BYTE chain.

### LRU retirement

Wave-23 / J.α reverted the LRU eviction policy introduced by wave-22-fix-4 / H.β.

**Why LRU was net-cost for retained encounters.** The LRU structure (std::list
bookkeeping + iterator stored in map value) raised per-entry TT footprint to
~96 B/entry — exceeding the 70 B projection at design time. For cultist +
Louse (the only pinned encounters post-SmallSlimes deprecation), the TT never
approaches the 200M cap (cultist tt_size ~85M). LRU bookkeeping was pure
overhead for these encounters: +4 GB peak RSS, +22% wall-clock vs pre-fix-4
baseline.

**Hard-abort cap policy restored.** `tt_insert` hard-aborts at cap; sets
`cap_hit_` flag; returns false. `kMaxTtEntries` restored to 370M
(wave-22-fix-3 baseline). `SolveStatus::kCapExceeded` re-introduced as a
possible solve outcome.

**Encounter selection replaces LRU as safety net.** Future encounters must
satisfy the 4-criterion screen from Q2-ADR-013 Amendment 4:
1. Bounded combat duration (block-dominated encounters are tractability traps).
2. No unbounded status-card accumulation.
3. Player-block does NOT dominate enemy damage budget.
4. Q1 has fixture support.

If a future encounter triggers `kCapExceeded`, project-lead decides between
re-introducing LRU vs encounter-side mitigation (Q2-ADR-013 Amendment 5).

**Profile delta (cultist benchmark):**
- Pre-(j) baseline @ 3d83d58: peak_rss_gb=10.323, wall_clock=62.84s.
- Post-wave-23 @ 9a61937: peak_rss_gb=6.495, wall_clock=~47s (est.; J.δ profile pending).

**Cross-references.** Q2-ADR-013 Amendment 5 (LRU revert rationale + full
Consequences). Q2-ADR-014 (stat widening rationale + rotation chain).
Q2-ADR-011 (TT cap-policy + lifecycle — kCapExceeded restored).

## 20. §20 — 2026-05-18 Wave-24 Nibbit port addendum

Wave-24 ports the Nibbit encounter (A0) in two compositions: NibbitsWeak (1 Nibbit) and NibbitsNormal (2 Nibbits). NibbitsWeak is pinned; NibbitsNormal is deferred via tombstone pending Amendment 1.

### Port outcome

| Encounter | Fixture | Commit | Outcome |
|---|---|---|---|
| NibbitsWeak | 07, seed 42 | 7bfcffa | Pinned — `expected_hp=69.2177`, `expected_rounds=5.1979`, `tt_size=62M`, RSS=6.19 GB, 47s |
| NibbitsNormal | 08, seed 42 | 5fc99ac | DEFERRED — `kCapExceeded` @ 370M entries, 22.16 GB peak, 271s wall-clock |

Adapter dispatch for NibbitsNormal STAYS LIVE (K.γ_setup `encounter_map` + dispatch branch). Only the pin regression-lock is tombstoned.

### New substrate (K.α + K.β + K.β-fix)

- **`MoveEffectKind::kBuffEnemy`** (APPEND-ONLY): one-shot self-buff applying `PowerKind` stacks to the acting enemy. Used by Nibbit's `HISS_MOVE` (Strength +2).
- **`MoveEffectKind::kBlockSelf`** (APPEND-ONLY): one-shot self-block gain for the acting enemy. Used by Nibbit's `SLICE_MOVE` (+5 block).
- **`MonsterKind::kNibbit = 7`** (APPEND-ONLY); `kButtMove`, `kSliceMove`, `kHissMove` appended to `MoveId`.
- **`kind_is_table_driven` helper**: boolean predicate routing `kNibbit` (and slime kinds) through `do_enemy_act_slime` (table-driven dispatch); cultist + LouseProgenitor retain kind-specific paths.
- **Enemy block decay**: enemy block decays at START of each side's turn via existing `turn_flow::EndTurnOps::reset_enemy_block` scaffold. Nibbit's `SLICE_MOVE` self-block follows the same path as Louse's `kCurlAndGrow` block; no new decay logic required.

Cultist Zobrist BYTE `Lo=0x569115efa81a95dc / Hi=0x9a06f1e505846a80` PRESERVED. APPEND-ONLY discipline validated for the 4th time across wave-24 K.* streams.

### Cap-bust observation — NibbitsNormal

NibbitsNormal's 2-Nibbit slot ordering is symmetric: both enemies share `MonsterKind::kNibbit` but start at different cycle positions (front → SLICE, back → HISS). Without canonical-form normalization, states where the two Nibbits have swapped slot values are treated as distinct by the Zobrist hash. This doubles reachable breadth per state that has been explored from both orderings.

Result: cap hit at 370M entries (22.16 GB) in 271s — fast because breadth (not depth) is the binding constraint. Reducing the search horizon has no material effect.

### Favored Amendment direction — G1 canonical-form swap

In `zobrist_of(CompactState)`, before folding enemy slots, sort the slot indices by a deterministic lex-key `(hp_desc, current_move_idx, strength)` when both enemies share the same `MonsterKind`. Symmetric states then hash identically → estimated ~50% breadth reduction → tt_size ~150-200M (within cap).

Heterogeneous encounters (cultist, LouseProgenitor, slime compositions) are unaffected.

### Encounter selection criteria addendum

The 4-criterion screen (§18) passed for NibbitsNormal on all four dimensions, yet cap-bust still occurred. The screen does not account for symmetric state-space duplication when same-kind enemies coexist. **Future encounters with N>1 same-kind enemies should add a 5th criterion: per-(kind,count) state-space breadth estimate.**

**Cross-references.** Q2-ADR-015 (full Nibbit port rationale + metrics + amendment directions). Q2-ADR-011 (TT cap-policy — `kCapExceeded` failure mode). Q2-ADR-013 Amendment 4 (4-criterion tractability screen origin). Q1-ADR-014 (Q1-side Nibbit port). ADR-029 §Path A tracker (NibbitsWeak + NibbitsNormal rows updated).
