# Q1 Game Simulator — Orchestrator Prompt

> **Audience:** an implementation orchestrator agent. Read this file as your instruction set.
> You dispatch `Agent` subagents stage by stage, in parallel where the dependency graph permits.

---

## 1. Orchestrator Directive

You drive the Q1 game simulator project to **Phase-1 Gate (S13)**, then Phase 2 (S14–S16), then Phase 3+ (S17–S18) by dispatching `Agent` subagents. You do not write production code. You dispatch, verify, integrate, repeat.

### Hard rules

1. **One `Agent` per stage.** Never implement directly.
2. **Parallel dispatch in one tool-use block.** When a wave has ≥2 stages, emit all `Agent` calls inside a single assistant message (multiple `Agent` content blocks). Sequential calls defeat the point.
3. **Every agent prompt MUST require `superpowers:subagent-driven-development`.** The agent decomposes its stage into sub-tasks and runs implementer + spec-reviewer + code-quality-reviewer subagents per sub-task. TDD throughout. One commit per sub-task, message format `<type>(<scope>): <change> (S{N}-T{k})` where k indexes sub-tasks within stage N.
4. **Worktree isolation for parallel waves.** Pre-create one worktree per agent via `superpowers:using-git-worktrees`. Single-agent waves may use the main checkout.
5. **Verify before mark complete.** Run validation commands. Inspect deliverable files. Confirm `make ci` green. Only then `TaskUpdate` → completed.
6. **On failure: re-dispatch with concrete feedback OR escalate.** Never workaround. Failed validation → re-dispatch quoting exact failing command + actual vs expected output.
7. **Track stage state in `TaskCreate`/`TaskUpdate`.** One task per stage on startup. Fall back to TodoWrite if Task tools unavailable in environment.
8. **On startup, reconcile with repo state.** Scan `git log` for `S{N}-T{k}` commits. Mark gate-passing stages complete before dispatching new work. As of last manifest update: **S0 is complete** (commits `dbbb72e`, `0c629b2`, `20ce617`, `4630f94`); S1 work is in progress in untracked files.

### Upstream source paths (include in every agent prompt)

- **Slay the Spire 2 (C# / Godot 4 prototype):** `~/development/projects/godot/sts2/`
  Main code at `src/`. Primary port source for M5/M6/M7 stages.
- **Godot engine (C++):** `~/development/repos/godot/`
  Reference for understanding Godot APIs that M8 stubs must mock.

Stage-specific subtree hints are in §6.5 under each manifest entry's `Upstream pointers`.

---

## 2. Required Skills

**You (the orchestrator) invoke:**
- `superpowers:dispatching-parallel-agents` — parallel dispatch playbook.
- `superpowers:using-git-worktrees` — worktree creation before parallel waves.
- `superpowers:finishing-a-development-branch` — after S18.

**Every dispatched agent's prompt mandates:**
- `superpowers:subagent-driven-development` — pulls in `superpowers:test-driven-development` and `superpowers:requesting-code-review`.

---

## 3. Operating Procedure

```
Loop:
  1. Inspect repo state (git log, file presence, last `make ci`). Reconcile Task ledger.
  2. Compute unblocked set: stages whose Prereqs are all complete.
  3. Pairwise file-disjointness check using Files field of each unblocked stage.
       Disjoint → all in this wave.
       Overlap → dispatch highest-priority only; defer overlapping stages to next iteration.
  4. Wave size ≥ 2 → create one worktree per agent (using-git-worktrees).
  5. Construct each agent prompt from §4 template + manifest entry from §6.5.
  6. Emit all `Agent` calls in ONE assistant message (parallel tool-use blocks).
  7. Await all returns.
  8. For each returned agent:
       a. Read status (DONE / DONE_WITH_CONCERNS / NEEDS_CONTEXT / BLOCKED).
       b. Run validation commands from manifest.
       c. Green → merge worktree with explicit merge commit, re-run `make ci`, TaskUpdate → completed.
       d. Red → see §5 Coordination Protocol.
  9. Repeat until S13 Phase-1 Gate all-green, then continue to W9–W12.
```

---

## 4. Agent Prompt Template

Construct each `Agent` dispatch with this prompt. Interpolate fields from §6.5.

```
You are an implementation agent for Stage S{N} of the Q1 game simulator (sts2-headless).

== REQUIRED SKILL ==
Invoke `superpowers:subagent-driven-development` and follow it for the rest of this task.
Decompose this stage into per-component sub-tasks. Per sub-task: dispatch implementer +
spec-reviewer + code-quality-reviewer subagents. TDD throughout. Commit per sub-task with
message `<type>(<scope>): <change> (S{N}-T{k})` where k indexes sub-tasks within the stage.

== WORKING TREE ==
{worktree-path-or-"main checkout at /home/clydew372/development/projects/cs/sts2-headless"}

== STAGE GOAL ==
{goal}

== DELIVERABLE ==
{deliverable}

== FILES IN SCOPE ==
{files}

== VALIDATION GATES (must all pass before you return DONE) ==
{runnable commands + expected outcomes}

== SPEC REFERENCES ==
- Module spec: docs/specs/modules/{spec-file}.md
- ADRs:        docs/specs/01-decisions-log.md → Q1-ADR-{ids}
- Overview:    docs/specs/00-system-overview.md
- Plan:        docs/plans/q1-implementation-plan.md (this file — for context only; do not act as orchestrator)

== UPSTREAM SOURCE (read these to port behavior; do NOT copy verbatim where Godot-coupled) ==
- Slay the Spire 2 (C# / Godot 4): ~/development/projects/godot/sts2/
  Main code at src/. Relevant subtrees for this stage: {stage-specific hints}
- Godot engine (C++): ~/development/repos/godot/
  Consult only if you must understand a Godot API your stubs/ports interact with.

== RISK POINTERS ==
{risk IDs from §7 with one-line summary each}

== RETURN ==
One of DONE / DONE_WITH_CONCERNS / NEEDS_CONTEXT / BLOCKED.
On DONE include: git SHAs you produced + outputs of all validation commands.
```

---

## 5. Coordination Protocol

- **Wave gate.** A wave is complete only when every agent returns DONE *and* every validation gate is green. Partial completion blocks the next wave.
- **Worktree merge.** Merge agents in dependency order. Use explicit merge commits (no fast-forward) for multi-agent waves so the parallel structure stays visible in `git log`. Re-run `make ci` after each merge.
- **Agent return modes.**
  - `DONE_WITH_CONCERNS`: read concerns. Scope/correctness issues → re-dispatch with feedback. Pure observation → log and proceed.
  - `NEEDS_CONTEXT`: provide missing context, re-dispatch the same agent.
  - `BLOCKED`: diagnose — context gap (re-dispatch with more), reasoning gap (re-dispatch on stronger model), task too large (split into sub-stages), spec gap (escalate, consider ADR reopen).
  - Validation fails after DONE: re-dispatch with the exact failing command + actual vs expected output.
- **Human-escalation triggers (stop and ask):**
  - Any ADR conflict (e.g., R3: M2 cannot meet <500µs → reopen Q1-ADR-005).
  - Any §9 Open Question becomes load-bearing for the next stage.
  - 3 failed re-dispatches on the same stage.
  - `make ci` red after a merge (regression).
- **S12 sub-parallelism.** Two options:
  - (a) Single S12 agent chunks internally — 5 sequential category passes (cards / relics / powers / monsters / potions) via its own `subagent-driven-development` decomposition.
  - (b) Orchestrator splits S12 into 5 sub-stage agents (S12-cards, S12-relics, S12-powers, S12-monsters, S12-potions) and dispatches them as a parallel sub-wave. Files within each category are disjoint.
  - Pick (b) when wall-clock matters; pick (a) when a single coherent commit history matters.

---

## 6. Reference Data

### 6.1 Context

Architecture specs at `docs/specs/` (overview, ADR log, 12 module specs). Plan sequences module implementation by **prerequisite-blocking dependency**: a stage cannot begin until every stage it depends on is complete enough to compile against.

Three Q1 product phases (per pipeline spec): **Phase 1** combat-only / all encounters / one character; **Phase 2** full run; **Phase 3+** counterfactual rollout. Stages **0–13** complete Phase 1; **14–16** complete Phase 2; **17–18** complete Phase 3+.

### 6.2 Assumptions

- **Phase 1 character:** Silent (matches upstream prototype state in `~/development/projects/godot/sts2/`). Phase 1 covers all encounters Silent can face — Acts 1–3 normal/elite/boss combats.
- **Megacrit headless API:** assumed unavailable; M8 stub-based engine strip per Q1-ADR-004 (revisit if API ships).
- **Q2 Oracle adapter (pipeline ADR-011):** drafted in parallel with S7 against M1's binary blob.
- **Branchability pre-design:** state types in S6/S14 designed with cheap-clone in mind from day one (avoids costly retrofit in S17).
- **.NET 9.0** (matches upstream `godot/sts2`).

### 6.3 Dependency Graph

```
                M5 Determinism Kernel  ◄── leaf, blocks ~everything
                  │
       ┌──────────┼──────────┐
       ▼          ▼          ▼
      M7         M8         M6d
       │          │          │
       └──────────┴──┬───────┘
                    ▼
                M6c (smoke-test content)
                    │
                    ▼
                  M6a Combat Domain
                    │
                    ▼
                  M1 State Codec   ◄── bit-identical roundtrip CI gate
                    │
                    ▼
                  M9 Process Host (CLI loop, in-process)
                    │
       ┌────────────┼────────────┐
       ▼            ▼            ▼
      M2           M3           M4
   Hook IPC    Replay        Control RPC
       │            │            │
       └────────────┴──────┬─────┘
                           ▼
                M6c full Silent content
                           │
                           ▼
                Determinism Probe + Phase-1 Gate
                           │
                ───── Q1 Phase 1 ✓ ─────
                           │
                       M6b Run Domain → … → Phase 2/3
```

**Critical path:** S0 → M5 → M6c(smoke) → M6a → M1 → M9 → M2 → ... → M6c(full) → Probe → Gate.

**Parallel-safe edges (orchestrator must exploit):**
- After S0: S1 ∥ S2 (different namespaces; M5 leaf and M8 stubs are independent).
- After S1: S3 ∥ S4 (Content namespace vs Actions namespace).
- After S7 + S8: S9 ∥ S10 ∥ S11 (three independent adapter namespaces).
- S12 may run alongside S9/S10/S11 if dispatched as separate sub-stage agents per category.
- After S14: S15 ∥ S16 (message types vs replay format).

### 6.4 Execution Waves

| Wave | Stages                  | Parallel | Worktrees | Unblocks after                             |
|------|-------------------------|----------|-----------|---------------------------------------------|
| W0   | S0                      | —        | —         | (complete)                                  |
| W1   | S1, S2                  | yes      | 2         | W0                                          |
| W2   | S3, S4                  | yes      | 2         | S1 done                                     |
| W3   | S5                      | no       | —         | S2, S3, S4 done                             |
| W4   | S6                      | no       | —         | S5 done                                     |
| W5   | S7                      | no       | —         | S6 done                                     |
| W6   | S8                      | no       | —         | S7 done                                     |
| W7   | S9, S10, S11, S12 (split or single) | yes | 4–8     | S7+S8 done (S12 also needs S5+S6)           |
| W8   | S13 Phase-1 Gate        | no       | —         | S9, S10, S11, S12 done                      |
| W9   | S14                     | no       | —         | Phase-1 Gate held                           |
| W10  | S15, S16                | yes      | 2         | S14 done                                    |
| W11  | S17                     | no       | —         | S14–S16 done                                |
| W12  | S18                     | no       | —         | S17 done                                    |

### 6.5 Stage Manifest

Each entry is the data interpolated into the §4 prompt template.

#### S0 — Repo skeleton, CI, namespace analyzer

**Status:** complete. Commits `dbbb72e`, `0c629b2`, `20ce617`, `4630f94`. Do not re-dispatch.

#### S1 — M5 Determinism Kernel

- **Goal:** seeded RNG primitives, deterministic clock, RNG state codec interface (Q1-ADR-003).
- **Prereqs:** S0.
- **Files:** `src/Sts2Headless.Domain/Determinism/**`, `test/Sts2Headless.Domain.Tests/Determinism/**`. (Note: untracked work already present here from prior session — agent must reconcile, not overwrite.)
- **Deliverable:** `Sts2Headless.Domain.Determinism` namespace: `Rng`, `RunRngSet`, `PlayerRngSet`, `IClock`, `IRngSource`, `IRngStateSerializer`, `CanonicalHash`.
- **Validation gates:**
  - `dotnet test --filter FullyQualifiedName~Determinism` — green.
  - Differential RNG test: 100 fixed seeds × every primitive byte-equal upstream `Rng.cs`. **Hard gate — do not exit until passes.**
  - RNG state serialization roundtrip test green.
  - `make ci` green.
- **Risk pointers:** R1 (RNG drift — hardest gate of S1).
- **Spec:** `determinism-kernel.md`; Q1-ADR-003.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/Core/Random/Rng.cs` — port algorithm byte-faithful. If it uses `Godot.RandomNumberGenerator` internally, re-implement in plain C# matching the same byte stream.

#### S2 — M8 Engine Strip categorical stubs

- **Goal:** stub categories from engine-strip.md § Stub Categories. Enables S5 to compile inherited Godot-coupled model code.
- **Prereqs:** S0.
- **Files:** `src/Sts2Headless.EngineStrip/**`, `test/Sts2Headless.EngineStrip.Tests/**`.
- **Deliverable:** no-op stubs for Rendering, Audio, Animation, Input, Scene-tree, Lifecycle, Sentry/Steamworks/Vortice, Godot file-IO, localization, 0Harmony. `StubRegistry` for test instrumentation.
- **Validation gates:**
  - Per-stub-category inertia tests green.
  - S0 banned-API analyzer confirms `Sts2Headless.Domain.*` clean of `Godot.*` / `System.IO.*` / `DateTime.*` / `Stopwatch.*` / `Environment.TickCount`.
  - `make ci` green.
- **Risk pointers:** R4 (stub coverage gaps surface later — keep `StubRegistry` traceable).
- **Spec:** `engine-strip.md`; Q1-ADR-004.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/` — grep `using Godot;` to enumerate inherited Godot surfaces. Cross-reference `~/development/repos/godot/modules/mono/` for Godot C# API shapes when designing stub signatures.

#### S3 — M7 Content Catalog framework

- **Goal:** generic `ContentTable<TId, TModel>` framework, Q4 manifest loader, coverage-gate framework. Empty content tables (populated in S5/S12).
- **Prereqs:** S1.
- **Files:** `src/Sts2Headless.Domain/Content/**`, `test/fixtures/q4-manifest-phase1.json`.
- **Deliverable:** `CardCatalog`, `RelicCatalog`, `PowerCatalog`, `MonsterCatalog`, `PotionCatalog`, `TokenMap`, fixture Q4 manifest.
- **Validation gates:**
  - Unit tests per spec — green.
  - Coverage gate passes vacuously (empty content).
  - `make ci` green.
- **Risk pointers:** (none specific to this stage).
- **Spec:** `content-catalog.md`.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/` — search for catalog/registry patterns (likely in a Core/Content/ subtree or scattered through card/relic loaders).

#### S4 — M6d Action Queue & Hooks

- **Goal:** action queue, hook registry, deterministic effect ordering per Q1-ADR-006. Full ~150 `HookType` enum surface (only Phase-1 types fire in tests).
- **Prereqs:** S1.
- **Files:** `src/Sts2Headless.Domain/Actions/**`, `test/Sts2Headless.Domain.Tests/Actions/**`.
- **Deliverable:** `ActionQueue`, `IAction`, `HookRegistry`, `HookHandler`, `ExecutionContext`.
- **Validation gates:**
  - ~5 fixture multi-hook ordering scenarios green; expected post-state matches.
  - CI pin test for ordering — non-skippable.
  - `make ci` green.
- **Risk pointers:** R5 (effect ordering must match upstream — derive, do not invent).
- **Spec:** `action-queue.md`; Q1-ADR-006.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/Core/Hooks/`, `src/Core/GameActions/`, `src/Core/Commands/`. Derive ordering rules from upstream source.

#### S5 — M6c smoke content + bases

- **Goal:** abstract bases (`CardModel`, `RelicModel`, `PowerModel`, `MonsterModel`) + concrete content for reference encounter (Silent + Ring of the Snake vs CULTISTS_NORMAL): ~10–20 cards, ~5 relics, ~5 powers, 2 monsters.
- **Prereqs:** S2, S3, S4.
- **Files:** `src/Sts2Headless.Domain/Content/{Cards,Relics,Powers,Monsters}/**` (smoke subset only).
- **Deliverable:** abstract bases mirroring upstream signatures + concrete smoke content registered in S3 catalogs.
- **Validation gates:**
  - Per-card OnPlay tests green.
  - Per-relic OnHook tests green.
  - Per-power OnTrigger tests green.
  - Cultist intent rotation test green.
  - Coverage gate green for smoke set.
  - `make ci` green.
- **Risk pointers:** R4 (new Godot surfaces in inherited model code → expand S2 stubs reactively).
- **Spec:** `content-behaviors.md`; `content-catalog.md`.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/` — locate Cards / Relics / Powers / Monsters subtrees and port the smoke subset only. Use `StubRegistry` from S2 to gracefully handle any Godot surface a base class touches.

#### S6 — M6a Combat Domain

- **Goal:** `CombatState`, `Creature`, `CardPile`, `CardInstance`, `PowerInstance`, `MonsterIntent`; turn lifecycle; legal-action enumeration; `ICombatContext`.
- **Design constraint:** state types use **cheap-clone-friendly** patterns (immutable persistent collections or explicit COW wrappers) per S17 preempt.
- **Prereqs:** S5.
- **Files:** `src/Sts2Headless.Domain/Combat/**`, `test/Sts2Headless.Domain.Tests/Combat/**`.
- **Deliverable:** `Sts2Headless.Domain.Combat` namespace.
- **Validation gates:**
  - Reference combat driven to completion via in-process direct API.
  - Final state matches recorded golden trace from upstream Godot.
  - `make ci` green.
- **Risk pointers:** R7 (GC pauses on hot path — prefer structs, object pools), R2 (no `HashSet<T>`/`Dictionary<TK,TV>` in serialized state types).
- **Spec:** `combat-domain.md`.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/Core/CombatManager.cs` and the surrounding combat subsystem. File is large — agent should sub-task by extraction unit (one upstream region per sub-task), each with passing tests before commit.

#### S7 — M1 State Codec

- **Goal:** binary section format + bit-identical roundtrip + manifest stamping + SHA-256 trailer.
- **Prereqs:** S6.
- **Files:** `src/Sts2Headless.Adapters/StateCodec/**`, `test/Sts2Headless.Adapters.Tests/StateCodec/**`.
- **Deliverable:** `Sts2Headless.Adapters.StateCodec`. CI gate enforces bit-identical roundtrip on every commit.
- **Validation gates:**
  - `Serialize(Deserialize(Serialize(s))) == Serialize(s)` byte-for-byte for ~20 fixture states. **Hard gate.**
  - Manifest stamping fields populated correctly.
  - `make ci` green.
- **Risk pointers:** R2.
- **Spec:** `state-codec.md`; Q1-ADR-002.
- **Upstream pointers:** (codec format is greenfield — no upstream source). State types being serialized live in `src/Sts2Headless.Domain/Combat/`; trace from there.

#### S8 — M9 Process Host (minimal)

- **Goal:** CLI invocation `(seed, character, deck, relics, encounter_id, ascension)` runs reference combat in-process (no IPC yet).
- **Prereqs:** S7.
- **Files:** `src/Sts2Headless.Host/**`, `test/Sts2Headless.Host.Tests/**`.
- **Deliverable:** arg parsing, composition root, scripted-action provider for test, structured JSON-line logs, Prometheus `/metrics` endpoint with placeholder counters.
- **Validation gates:**
  - End-to-end CLI test plays reference combat to documented final state.
  - SIGTERM graceful shutdown test green.
  - `make ci` green.
- **Risk pointers:** R7.
- **Spec:** `process-host.md`.
- **Upstream pointers:** (greenfield — Godot is the host upstream; agent does not port from it).

#### S9 — M2 Hook Protocol Adapter (IPC)

- **Goal:** shared-memory ring buffer + semaphores; manifest handshake; latency p99 < 500µs.
- **Prereqs:** S7, S8.
- **Files:** `src/Sts2Headless.Adapters/HookProtocol/**`, `test/mock-worker/**`, `test/Sts2Headless.Adapters.Tests/HookProtocol/**`.
- **Deliverable:** adapter; mock Q8 reference impl in `test/mock-worker/`.
- **Validation gates:**
  - End-to-end IPC roundtrip via mock Q8 drives reference combat to completion.
  - Latency p99 < 500µs measured under realistic load (warn at 400µs). **Hard gate.**
  - `make ci` green.
- **Risk pointers:** R3 (highest single-stage risk). Profile day one; budget allocations; consider unsafe-pointer ring-buffer access. If gate unreachable, reopen Q1-ADR-005 (do not workaround).
- **Spec:** `hook-protocol-adapter.md`; Q1-ADR-005.
- **Upstream pointers:** (greenfield).

#### S10 — M3 Replay Recorder

- **Goal:** replay format, action recording, manifest stamping, reader API, off-decision-path async flush.
- **Prereqs:** S7, S8.
- **Files:** `src/Sts2Headless.Adapters/Replay/**`, `test/Sts2Headless.Adapters.Tests/Replay/**`.
- **Deliverable:** `Sts2Headless.Adapters.Replay`.
- **Validation gates:**
  - Record session; replay through fresh Q1; final state matches via `M1.CanonicalHash`.
  - `make ci` green.
- **Risk pointers:** (none specific).
- **Spec:** `replay-recorder.md`.
- **Upstream pointers:** (greenfield).

#### S11 — M4 Control Plane

- **Goal:** Unix-socket JSON RPC for `save_state`, `load_state`, `set_seed`, `step_until_decision`, `apply_action`, `terminate`.
- **Prereqs:** S7, S8.
- **Files:** `src/Sts2Headless.Adapters/ControlPlane/**`, `test/Sts2Headless.Adapters.Tests/ControlPlane/**`.
- **Deliverable:** `Sts2Headless.Adapters.ControlPlane`. Replay-control RPCs deferred to S15.
- **Validation gates:**
  - Mock-orchestrator test drives save → reseed → step → apply → save flow; final state matches reference.
  - `make ci` green.
- **Risk pointers:** (none specific).
- **Spec:** `control-plane.md`.
- **Upstream pointers:** (greenfield).

#### S12 — M6c full Silent content

- **Goal:** expand M6c to Phase-1 scope: full Silent card pool (~75), full relic pool (~80), full power pool (~60), all monsters Silent encounters (~50), all combat potions (~20).
- **Prereqs:** S5, S6. (May dispatch alongside S9/S10/S11 — files disjoint from those adapters.)
- **Files:** same dirs as S5 (extend `Cards/`, `Relics/`, `Powers/`, `Monsters/`, `Potions/`).
- **Deliverable:** all content registered in S3 catalogs; coverage gate green.
- **Validation gates:**
  - Per-content unit tests (fixture-discovery framework).
  - Fixture combat tests for ~20 representative encounters across Acts 1–3.
  - Coverage gate green.
  - `make ci` green.
- **Risk pointers:** R4.
- **Spec:** `content-behaviors.md`; `content-catalog.md`.
- **Sub-parallelism guidance to the agent:** chunk by category (cards / relics / powers / monsters / potions). Either run them as 5 sequential subagent-driven-development passes within this single agent, or — preferred when wall-clock matters — return early and ask the orchestrator to split into 5 sub-stage agents (S12-cards, S12-relics, S12-powers, S12-monsters, S12-potions) for a parallel sub-wave.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/` — full content subtrees. Port Silent class + neutral content + all Silent-relevant monsters.

#### S13 — Determinism Probe + Phase-1 Gate

- **Goal:** external CI tool at `test/determinism-probe/` runs Q1 + unmodified headless Godot from same seed; per-step state hash comparison; Phase-1 corpus passes.
- **Prereqs:** S9, S10, S11, S12.
- **Files:** `test/determinism-probe/**`, `test/fixtures/phase1-corpus/**`, `Makefile` (add `probe` target).
- **Deliverable:**
  - Probe binary spawning Q1 (via M2 mock-worker driver) and headless Godot.
  - Phase-1 seed corpus: ~200 seeds × all Silent encounters across Acts 1–3.
  - Local cron / `make probe` job.
- **Phase-1 Gate (you, the orchestrator, verify all before advancing to W9):**
  - [ ] Bit-identical roundtrip CI gate green.
  - [ ] M2 latency p99 < 500µs.
  - [ ] Probe Phase-1 corpus passes with zero divergence.
  - [ ] Content-coverage gate green (every Silent-relevant `*Model` registered).
  - [ ] Pinned multi-hook ordering tests green.
  - [ ] T3 Ledger entries reviewed; budget ≲ ~5 entries.
  - [ ] Q2 Oracle adapter operational against M1 binary blob (parked work).
- **Risk pointers:** R6 (use `CanonicalHash` per-step; probe reports first-diverging action).
- **Spec:** `00-system-overview.md` (probe is cross-cutting).
- **Upstream pointers:** `~/development/projects/godot/sts2/` — probe spawns headless Godot from this checkout. `~/development/repos/godot/` — Godot headless mode invocation reference if needed.

#### S14 — M6b Run Domain

- **Goal:** acts, map DAG, room sequencing, encounter selection, rewards, run-scope state.
- **Design constraint:** cheap-clone-friendly state types (mirrors S6).
- **Prereqs:** Phase-1 Gate held.
- **Files:** `src/Sts2Headless.Domain/Run/**`, `test/Sts2Headless.Domain.Tests/Run/**`.
- **Validation gates:**
  - First end-to-end run integration test (Silent, ascension 0, full Act 1 to act-1-boss).
  - Per-room state hashes match upstream.
  - `make ci` green.
- **Risk pointers:** R7.
- **Spec:** `run-domain.md`.
- **Upstream pointers:** `~/development/projects/godot/sts2/src/Core/` — RunManager / act / map / room subsystem.

#### S15 — Run-level message types

- **Goal:** schema-versioned additions to M2 hook protocol + M4 control RPC for card-pick, map, shop, event, rest, potion.
- **Prereqs:** S14.
- **Files:** `src/Sts2Headless.Adapters/HookProtocol/**` (v2 messages), `src/Sts2Headless.Adapters/ControlPlane/**` (v2 RPCs).
- **Validation gates:** schema migration tests; both v1 and v2 clients work.
- **Spec:** `hook-protocol-adapter.md`, `control-plane.md` (schema v2 sections).
- **Upstream pointers:** (greenfield).

#### S16 — Run-level replay format

- **Goal:** M3 replay schema bump for run-level decisions; reader handles both versions.
- **Prereqs:** S14.
- **Files:** `src/Sts2Headless.Adapters/Replay/**` (v2 schema), tests.
- **Validation gates:** mixed v1/v2 replay reader test.
- **Spec:** `replay-recorder.md` (schema v2).
- **Upstream pointers:** (greenfield).

#### S17 — Branchable state primitives

- **Goal:** cheap clone of `CombatState` and `RunState` for counterfactual rollout. Should be small if S6/S14 followed pre-design.
- **Prereqs:** S14, S15, S16.
- **Files:** `src/Sts2Headless.Domain/Combat/**`, `src/Sts2Headless.Domain/Run/**` (clone surface only).
- **Validation gates:** clone+mutate test — original unaffected; benchmark cost < threshold (TBD on entry).
- **Upstream pointers:** (design problem — no upstream).

#### S18 — `clone_state` RPC + multi-Q1 orchestration

- **Goal:** M4 method for external orchestrators to spawn K parallel Q1 children from saved state.
- **Prereqs:** S17.
- **Files:** `src/Sts2Headless.Adapters/ControlPlane/**`.
- **Validation gates:** spawn-K test; children diverge correctly under different actions.
- **Spec:** `control-plane.md` (clone_state section).
- **Upstream pointers:** (greenfield).

> **Note on W9–W12 detail depth:** Stage manifests S15–S18 are deliberately thinner than S1–S13. Before dispatching W10+, re-read upstream run-domain code under `~/development/projects/godot/sts2/src/Core/` and consider re-expanding these manifests with concrete validation commands. Escalate to the human if the existing detail is insufficient at dispatch time.

---

## 7. Cross-Cutting Risk Register

Mitigation column is addressed to you, the orchestrator.

| # | Risk | Surfaces at | Mitigation (orchestrator action) |
|---|------|-------------|----------------------------------|
| R1 | M5 RNG diverges from upstream → all behavior drifts | S1 | Differential test vs upstream `Rng.cs` is a hard gate at S1 exit. Byte-equal or do not mark S1 complete. |
| R2 | M1 cannot achieve bit-identical roundtrip due to nondeterministic state types | S6, S7 | S0 banned-API analyzer disallows `HashSet<T>`/`Dictionary<TK,TV>` in serialized state types unless paired with explicit ordering rule. Confirm S6 agent honors this in its design before dispatch. |
| R3 | M2 cannot hit <500µs p99 with shared-memory ring buffer | S9 | Instruct S9 agent to profile from day one. Budget allocations on the decision path. If gate unreachable after re-dispatch with stronger model, reopen Q1-ADR-005 (in-process embedding via Python.NET reservation) — escalate to human. |
| R4 | M8 stub coverage gaps surface as runtime exceptions when unfamiliar Godot surfaces hit by inherited code | S5, S6, S12, S14 | Namespace analyzer catches at compile time. T3 Ledger tracked across stages; orchestrator reviews ledger at every wave gate. Stubs added reactively but only in S2-scoped commits, not slipped into other stages. |
| R5 | Q1-ADR-006 effect ordering does not match upstream → probe fails at every multi-hook scenario | S4, S13 | S4 agent prompt instructs: derive rules from upstream source, not invented. Pin scenarios in S4 fixture tests. Probe localizes via per-step hash in S13. |
| R6 | Probe divergence is hard to localize | S13 | M5 `CanonicalHash` from S1; probe records per-step hashes; first-diverging action reported. Verify S1 deliverable includes `CanonicalHash`. |
| R7 | C# GC pauses on hot path | S6, S9 | S6 agent prompt: struct types for hot state; object pooling. S8 agent: track GC time in Prometheus. Monitor at S13 probe stage. |
| R8 | Stage stalls (most likely S9 latency tuning) | S9 | Timebox each gating stage in your re-dispatch budget. If re-dispatch counter ≥3 with no progress, escalate to human and consider ADR reopen. Never burn unbounded iterations silently. |

---

## 8. Cumulative Verification Gates

Gates accumulate across waves. You (orchestrator) verify each before marking a stage complete or advancing a wave.

| Gate | Enabled at | Cadence | Action on failure |
|------|-----------|---------|-------------------|
| Banned-API analyzer | S0 | every commit | block merge |
| Stub-coverage compile gate | S2 | every commit | block merge |
| Content-coverage gate (Q4 mapping consistent) | S3 | every commit | block merge |
| Pinned multi-hook ordering tests | S4 | every commit | block merge |
| Bit-identical roundtrip | S7 | every commit | block merge |
| M2 latency p99 budget | S9 | every commit (PR-fast subset) + scheduled full run | warn / block |
| Determinism probe — Phase-1 corpus | S13 | scheduled (`make probe`) | block Phase 2 start |
| Determinism probe — full-run corpus | S14 | scheduled | block Phase 3 start |

---

## 9. Open Questions

Surface to the human before any wave where the question becomes load-bearing.

1. **Phase-2 character.** Phase 1 = Silent. Phase 2 — extend Silent or add a second character? Default: extend Silent through full run; second character deferred to post-Phase-3. Confirm before W9 dispatch.
2. **Content-extraction tooling.** S5/S12 port upstream model classes. Default: manual port file-by-file with diff-tracking. Revisit if volume becomes painful in S12.
3. **Replay-format binary vs JSON.** Module spec says binary. Default: binary per spec; ship a debug-mode JSON dumper alongside in S10.
4. **`make ci` vs full GitHub-Actions setup.** Default: local `make ci` only. GitHub Actions deferred until repo goes public or team grows.
5. **S12 sub-parallelism (single agent chunks vs orchestrator-split).** Default: orchestrator-split into 5 category agents (S12-cards, S12-relics, S12-powers, S12-monsters, S12-potions) for parallel sub-wave. Override per §5 if a coherent single commit history is preferred.
