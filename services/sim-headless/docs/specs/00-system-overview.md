# 00 — System Overview: Game Simulator (Q1) Internals

Entry point for `docs/specs/`. This spec set covers Q1 (Game Simulator) **internal** architecture. Q1's external boundary — its place in the 12-quanta pipeline, its owned schemas, its sync/async edges to Q2/Q3/Q7/Q8/Q11/Q12 — is fixed by `~/development/projects/cpp/sts2-ai/docs/specs/modules/game-simulator.md` and is **not restated here**. Cross-references only. Anything in this tree that contradicts the pipeline-level spec must be reconciled there first.

## 1. Purpose & Boundaries

Q1 is the headless C# Slay the Spire 2 Core: the deterministic game-state machine that everything else in the RL pipeline talks to (per pipeline ADR-002). It runs the game from a seed; exposes a hook protocol at every player-decision boundary; provides bit-identical save/restore; supports branchable rollouts; emits replay files. No rendering, audio, animation, UI, networking, multiplayer.

In scope of *this* spec set: the modules **inside** Q1, their responsibilities, what data each owns, how they communicate, how they are tested. Out of scope: pipeline-level ADRs, cross-quantum schemas, network architecture, training schedule.

Implementation target: `~/development/projects/cs/sts2-headless/` (currently empty). Source code is extracted from the production Godot/C# game at `~/development/projects/godot/sts2/` (~99% C# under `src/Core/`; engine-coupled surfaces concentrated in `src/Core/Nodes/`).

## 2. Architecture Characteristics (Ranked)

The implicit drivers Q1 must satisfy. Specializations of the pipeline-wide characteristics in `cpp/sts2-ai/docs/specs/00-system-overview.md` §3.

1. **Determinism / Reproducibility & Fidelity.** Q1 is the origin of determinism for the pipeline. Bit-identical save/restore (CI-enforced); branchable state with no shared mutable references; mechanical equality with unmodified Godot build (differential test); zero latent nondeterminism (no `DateTime.Now`, no thread-pool work on the decision path, no hash-set iteration in equality-relevant code, no GC finalizer side-effects). Failure mode is silent corruption of training data — there is no degraded mode.

2. **Hot-Path Throughput & Branchable State.** Per-decision IPC budget <500µs (pipeline ADR-005); fleet-target 10⁸ combat-steps/day. C# GC is the headline throughput risk (pipeline ADR-002 Negatives) — addressed by struct types for hot state, object pooling, GC tuning. Single-threaded decision path; concurrency is by process replication. Branchable rollouts must not require process restart.

3. **Patch Adaptability via Mod-Shaped Boundary.** STS2 is in active content-patch development. Q1's "engine strip" (replacing Godot surfaces with deterministic stubs) is structured as out-of-tree mod via `Core/Modding` wherever possible, per pipeline ADR-002. Per-component schema versioning (state, hook protocol, replay format) so a content-patch state change does not invalidate the IPC contract. Failure mode is engineering churn per patch (1-week rebase ↔ 1-month full retrain).

### Below the line — constraints, not characteristics

- **Single-process** (pipeline ADR-002 + ADR-005). Q1 is one .NET process.
- **Restart fault-tolerance.** Workers are cattle; per-worker durability is not needed.
- **Internal-only.** No security, multi-tenancy, PII concerns.
- **No multiplayer.** All multiplayer code paths from upstream `src/Core/Multiplayer/` and `src/Core/GameActions/Multiplayer/` are stripped.

## 3. Internal Context Diagram (textual)

External actors (cross-quantum):

- **Q8 Rollout Workers** — sync hot-path consumer of Q1's hook protocol. Latency-critical edge (<500µs/decision) over shared-memory ring buffer.
- **Q11 Curriculum Generator / Q12 Evaluation Harness** — cold-path consumers via control RPC. Save/load/seed/step orchestration.
- **Q2 Oracle Verifier** — consumes Q1's serialized binary state (Q2 owns the engine→CompactState adapter per pipeline ADR-011).
- **Q3 Experience Store** — async consumer of replay files (filesystem sink).
- **Q4 Content Registry** — read-only token-ID source bundled with model artifact (pipeline ADR-010); Q1 references but does not call at runtime.
- **Q7 Observability TSDB** — pulls Prometheus metrics from Q1's host endpoint.
- **Q8 supervisor** — spawns Q1 paired with the worker; restarts both on crash (pipeline ADR-005).

Internal modules (M1–M9; see §4):

```
                                     ┌──────────────────────┐
                                     │   M9 Process Host    │  composition root
                                     └──────┬─────────────┬─┘
                                            │             │
                                ┌───────────┼─────────────┼──────────────┐
                                │ schema-owning adapters  │              │
        Q8 ◄── shm IPC ───►  M2 Hook Protocol             │              │
        Q11/Q12 ◄── unix sock ──► M4 Control Plane        │              │
        Q3 ◄── filesystem ──── M3 Replay Recorder         │              │
        (any caller) ──────► M1 State Codec ─── stamps ──►│  manifest    │
                                       ▲                  │              │
                                       │ serialize        │              │
                                ┌──────┴──────────────────┴──────────────┐
                                │              Domain Core               │
                                │                                        │
                                │   M6b Run Domain ─────► M6a Combat     │
                                │        │                    │          │
                                │        ▼                    ▼          │
                                │   M6c Content Behaviors                │
                                │        │                               │
                                │        ▼                               │
                                │   M6d Action Queue & Hooks ─────► M5   │
                                └────────────────────────────────────────┘
                                          │              │
                                          ▼              ▼
                                  M5 Determinism   M7 Content
                                  Kernel           Catalog
                                          ▲
                                          │ structurally replaces Godot surfaces
                                  M8 Engine Strip / Mod Layer
```

Sync edges (latency-critical): M2 ↔ Q8 via shared memory, target <500µs/decision (pipeline ADR-005).
Sync edges (cold path): M4 ↔ Q11/Q12 over Unix socket; not latency-critical.
Async edges: M3 → filesystem (replay sink). M9 → Prometheus pull endpoint.

## 4. Internal Module Map

12 modules. 6 own external-facing schemas (data contracts other modules or other quanta version against); 6 are domain or utility modules with no external schema.

| # | Module | File | Schema-Owning? |
|---|---|---|---|
| M1 | State Codec | `modules/state-codec.md` | yes — versioned binary state schema + Game Version Manifest |
| M2 | Hook Protocol Adapter | `modules/hook-protocol-adapter.md` | yes — hook protocol message schema |
| M3 | Replay Recorder | `modules/replay-recorder.md` | yes — replay file format |
| M4 | Control Plane | `modules/control-plane.md` | yes — control RPC schema |
| M5 | Determinism Kernel | `modules/determinism-kernel.md` | yes — RNG state schema (roundtripped through M1) |
| M6a | Combat Domain | `modules/combat-domain.md` | no |
| M6b | Run Domain | `modules/run-domain.md` | no |
| M6c | Content Behaviors | `modules/content-behaviors.md` | no |
| M6d | Action Queue & Hooks | `modules/action-queue.md` | no |
| M7 | Content Catalog | `modules/content-catalog.md` | no (references Q4 tokens) |
| M8 | Engine Strip / Mod Layer | `modules/engine-strip.md` | no (boundary contract) |
| M9 | Process Host | `modules/process-host.md` | no |

## 5. Phasing

Per the pipeline Q1 spec, Q1 has three implementation phases:

- **Phase 1 — Combat-only.** `(seed, character, deck, relics, encounter_id, ascension) → final state`. M6a, M6c, M6d, M5, M7, M1, M2, M8, M9 minimally functional. M6b stubbed; M3 may emit minimal replay; M4 minimal RPC.
- **Phase 2 — Full run.** `(seed, character, ascension) → full run`. M6b lit up; hook at every decision type (card pick, map, shop, event, rest, potion); replay format extended.
- **Phase 3+ — Counterfactual rollout.** Replay-from-saved-state under alternative actions; full faithful event simulation.

Module specs document responsibilities at full Phase-3 scope; `[Phase 1 scope]` / `[Phase 2]` / `[Phase 3+]` markers identify which phase each capability lights up in.

## 6. Where to Look Next

- `01-decisions-log.md` — ADRs governing Q1 internals. Read before challenging any module boundary.
- `modules/<name>.md` — one per module. Responsibilities, Data Ownership, Communication, Coupling, Testing Strategy.
- `~/development/projects/cpp/sts2-ai/docs/specs/` — pipeline-level specs (system overview, pipeline ADRs, all 12 module files). Q1's external boundary lives there.
- `~/development/projects/cpp/sts2-ai/docs/scaling-strategy.md` — research roadmap; the *why* behind the determinism + throughput + patch-adaptability budget.
