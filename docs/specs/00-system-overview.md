# 00 — System Overview

Entry point for `docs/specs/`. Subsequent files (`01-decisions-log.md`, `modules/*.md`) refine this; anything that contradicts this document must be reconciled here first.

## 1. Purpose & Boundaries

Build a generalized RL agent for Slay the Spire 2. Today: an expectimax solver for one A0 combat (`Silent + Ring of the Snake` vs `CULTISTS_NORMAL`), plus deterministic `CompactState`, transposition table, and 252-test regression battery. Target (12-month committed): full A0 run agent, lead character, ≥85% win rate. Target (24-month uncommitted): superhuman across all characters at high ascension.

In scope of this spec: the **infrastructure** that produces, evaluates, and serves the agent — its quanta, their data, and their seams. Out of scope: specific network architecture, hyperparameters, training schedule. Those live in research notes and ride on top of the seams defined here. See `docs/scaling-strategy.md` for the research roadmap.

## 2. Context Diagram (textual)

External actors:

- **Megacrit STS2 source** — read-only. Drives Q1's headless port and patch-adaptation cadence.
- **Research team** — submits training jobs, reads dashboards, signs off model promotions.
- **CI system** — runs determinism + regression battery; blocks merges on divergence.

Internal quanta and primary edges (Q1–Q12; see §4):

```
  STS2 source ──► Q1 Game Simulator ◄──── Q12 Eval Harness ───► Q6 Eval Reports
                       ▲                          │
                       │ binary state, hooks      ▼
                       │                  (humans + CI gates)
              ┌────────┴───────────────────────────────────┐
              │ Q8 Rollout Workers ◄── Q9 Inference Server │ ◄── Q5 Model Registry
              │      │                       ▲             │           ▲
              │      ▼ trajectories          │ batched     │           │
              │ Q3 Experience Store ─────────┴─────────────┼─► Q10 Trainer
              └────────────────────────────────────────────┘           ▲
                       ▲                                               │
                       │                                               │
              Q11 Curriculum Generator                                 │
                                                                       │
              Q2 Oracle ─────── prioritized labeling signal ───────────┘

  All quanta emit to ───► Q7 Observability TSDB
  All quanta read   ◄──── Q4 Content Registry (token IDs / card-text DSL)
```

Sync edges (latency-critical): Q8↔Q9 via shared memory, target <50µs; Q8↔Q1 via shared memory, target <500µs per decision.
Async edges: Q8 → Q3, Q3 → Q10, Q10 → Q5. Backpressure via retention policy at Q3.

## 3. Architecture Characteristics (ranked)

1. **Throughput / Scalability** — 10⁸ combat-steps/day on a 1024-core fleet by Phase 3 gate (`scaling-strategy.md` §4.1). Drives: stateless workers, batched GPU inference, queue-mediated trainer, no single-process bottleneck.
2. **Determinism / Reproducibility** — bit-identical save/restore; pinned-seed regression battery on every CI commit; every model artifact stamped with `(code SHA, dataset SHA, seed, hyperparameters, token-registry SHA)` (`scaling-strategy.md` §5.5). Drives: versioned binary schemas, single seeded `RandomService`, replay-as-truth.
3. **Evolvability / Patch Adaptability** — re-fit budget bounded; balance patches must not require pipeline-wide rebuild (`scaling-strategy.md` §0 risk #1, §2.7). Drives: stable-ID Content Registry, card-text subnetwork, out-of-tree mod for Q1, per-component schema versioning.

Below the line — constraints, not characteristics:

- Inference latency: <100ms at 64-sim search budget (Phase 1); <500ms at full budget (Phase 5).
- Internal-only system. No security, multi-tenancy, or PII concerns.
- Fault tolerance is "restart" — workers are cattle. No per-worker durability needed.

## 4. Quanta Map

7 schema-owning quanta, 5 stateless service quanta:

| Q | Name | Schema-owning | Phase 1 | Module file |
|---|---|---|---|---|
| Q1 | Game Simulator | yes | yes | `modules/game-simulator.md` |
| Q2 | Oracle Verifier | yes | yes | `modules/oracle.md` |
| Q3 | Experience Store | yes | yes | `modules/experience-store.md` |
| Q4 | Content Registry | yes | yes (minimal) | `modules/content-registry.md` |
| Q5 | Model Registry | yes | yes | `modules/model-registry.md` |
| Q6 | Evaluation Reports | yes | partial — matures Phase 3 | `modules/evaluation-reports.md` |
| Q7 | Observability TSDB | yes | yes | `modules/observability.md` |
| Q8 | Rollout Workers | no | yes | `modules/rollout-workers.md` |
| Q9 | Inference Server | no | yes | `modules/inference-server.md` |
| Q10 | Trainer | no | yes | `modules/trainer.md` |
| Q11 | Curriculum Generator | no | no — Phase 2+ | `modules/curriculum-generator.md` |
| Q12 | Evaluation Harness | no | yes | `modules/evaluation-harness.md` |

Trained policies (combat policy, run-level heads) are *artifacts inside Q5*. They are versioned and served, not deployed independently.

## 5. Where to Look Next

- `01-decisions-log.md` — ADRs. Read before challenging any quantum boundary; each ADR's `Consequences` section leads with the negative trade-offs we are accepting.
- `modules/<name>.md` — one per quantum: responsibilities, data ownership, communication, coupling.

Open questions resolved by judgement (Phase 2 directive): see ADR-002, ADR-005, ADR-007, ADR-010, ADR-011. Open questions explicitly deferred: see ADRs marked `Status: Deferred`.
