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
| Test battery | `engine/cpp/tests/` | 424 active, 100% green (lead's "252" figure is dated; substrate grew) |
| Pinned regression | `engine/cpp/tools/seed-pinner/` + `tests/seeds/expected_values.h` | CULTISTS_NORMAL only; pins are STL-impl-specific |
| Build | top-level `CMakeLists.txt`, `CMakePresets.json`, single ALIAS target `sts2::simulator` | clean |
| M1 binary state spec | `engine/headless/docs/specs/modules/state-codec.md` | locked, schema v3 minor |
| StateBlobEnvelope v0.1 | `contracts/schemas/game-simulator/state_blob.proto` | locked per Q1-ADR-012 |
| Generated cpp bindings | `contracts/generated/cpp/game-simulator/{state_blob,hook}_pb.h` | **STUB ONLY** — empty struct decls, no codegen |
| D3 fixture corpus | `engine/headless/test/fixtures/state-blobs/` | 6 fixtures, byte-locked, canonical hashes pinned |
| Monorepo data convention | `data/{eval-harness,experience-store,inference-server,model-registry,observability,rollout-workers,trainer}/` | per-service subdirs exist; `data/oracle/` open |

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

The C++ prototype is structurally CULTISTS_NORMAL-only. Three sources confirm:

- `state.h:66-78` `EnemyState`: 5/10 fields are cultist mechanics
  (`dark_strike_base`, `ritual_amount`, `just_applied_ritual`,
  `performed_first_move`, `current_move = MoveId::kIncantation`).
- `state.h:94` `enemies`: `std::array<EnemyState, 2>` hardcoded.
- `state.h:23-26` `CardCounts`: `static_assert(kCountedCardIds.size() <= 8)`,
  with 4 distinct kinds today (Strike, Defend, Neutralize, Survivor).
- `game::enemies`: `make_calcified_cultist`, `make_damp_cultist` only.
- `scaling-strategy.md` §1.3 enumerates all three as scaling breaks; refactor
  cost flagged "bounded effort." Not Phase-1A work.

### D3 fixture corpus implication

Of 6 fixtures shipped in `engine/headless/test/fixtures/state-blobs/`:

| # | Encounter | Adapter feasible? | S1 path |
|---|---|---|---|
| 1 | CultistsNormal (Calcified + Damp) | YES | adapter → expectimax → pinned `(action, value)` round-trip |
| 2 | FossilStalkerElite seed 42 | NO | reject with `UnsupportedEncounter` diagnostic |
| 3 | FossilStalkerElite seed 1337 | NO | reject with `UnsupportedEncounter` diagnostic |
| 4 | KaiserCrabBoss (Crusher + Rocket) | NO | reject + unknown-power diagnostic (Q2-ADR-005) |
| 5 | LouseProgenitorNormal | NO | reject with `UnsupportedEncounter` diagnostic |
| 6 | SmallSlimes | NO | reject (also B.1-ε DEFER per Q1 fixture README) |

This is a material refinement of the lead's S1 framing. See §6 below and
Q2-ADR-002.

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

| Stage | Starting | Refined | Driver |
|---|---|---|---|
| S1 | "each of 6 D3 fixtures → expectimax → pinned value" | Fixture #1 round-trip; #2–#6 reject-with-diagnostic | C++ engine is CULTISTS_NORMAL-only; non-cultist fixtures have no engine mechanics |
| S2 | "Q1's 16-encounter corpus (22+ post-Phase-1.5)" | CULTISTS_NORMAL only Phase-1A; per-encounter expansion deferred | same substrate boundary |
| S0 | "3–5 Q2-ADRs" | 5 Q2-ADRs | within budget |
| S3, S4 | (no change) | (no change) | — |

S1 and S2 diffs are **material**. Re-surface trigger #1 from boot directive
fires. Status report flags this; lead ratifies or directs Path A (expand
C++ engine into Q2 scope) before S1 work begins.

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

Both pins fire from the `make ci-slow` Release wave gate. Q2 surfaces
diagnostic-class divergence as a top-line concern in its next status to
the project lead — divergence is never silent.

**Forward-laid:** as Q1 Phase-1.5 adds the BowlbugsTrio encounter, the
parallel invariant extends per-encounter. The Q2 per-encounter pin
registry (S2-T2 onwards) is the data-shape vehicle; the dual-path
invariant scales 1:1 with each new pinned encounter that exists in
both Q1's direct-init helpers and the Q2 adapter scope (currently
CULTISTS_NORMAL only; Q2-ADR-002).
