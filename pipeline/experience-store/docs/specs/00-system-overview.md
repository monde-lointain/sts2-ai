# 00 — Q3 Experience Store: System Overview

Entry point for Q3's internal specs. Subsequent files (`01-decisions-log.md`,
`modules/*.md`) refine this; anything that contradicts this document must be
reconciled here first.

Cross-quantum context lives at `docs/specs/00-system-overview.md` and
`docs/specs/modules/experience-store.md`. Cross-quantum ADRs that constrain
Q3: ADR-001, ADR-006, ADR-014..018 at `docs/specs/01-decisions-log.md`.

## 1. Purpose & Boundaries

Q3 is the async backbone between Q8 rollout-workers (writers) and Q10 trainer
(reader), absorbing the ~10³:1 producer/consumer rate mismatch
(`docs/scaling-strategy.md` §4.5, §5.3). Q3 persists per-step trajectory rows
per `contracts/schemas/trajectory/trajectory.proto` v1, serves sampled
minibatches under uniform / stratified / prioritized modes, routes the
oracle-agreement sideband from Q2 (per ADR-017 carve-out), and is the
dataset-of-record for retroactive analysis.

In scope: ingest, durable storage (hot + cold tiers), sampling, schema
version-gate, retention, provenance, oracle-agreement sideband, observability
adapter.

Out of scope: trajectory schema design (cross-quantum, owned at
`contracts/schemas/trajectory/`), engine state encoding (Q1), priority-score
computation (Q10 + Q2 produce; Q3 stores and serves).

## 2. Context Diagram (textual)

External producers and consumers:

```
   Q8 rollout-workers ─┐                            ┌── Q10 trainer
   Q11 curriculum    ──┼─[POST /trajectories]──►    ◄─[POST /sample]──
   Q2 oracle (sideband)┘                            │
                                                    │   Q12 eval-harness
                                                    │   ◄─[GET /sample/recent]──
                                                    │
                       Q7 observability             │   ◄─[GET /metrics]──
                                                    ▲
   ┌────────────────────────────────────────────────────────────┐
   │              Q3 Experience Store (one process, port 18103)  │
   │                                                              │
   │   ┌──────────────┐                                           │
   │   │  IngestAPI   │──[fn]──► SchemaRegistry (version gate)    │
   │   │ POST /traj   │              │                            │
   │   └──────┬───────┘              │                            │
   │          │ [queue]               ▼                           │
   │          ▼                                                   │
   │   ┌──────────────┐         ┌────────────────┐                │
   │   │  HotStore    │◄──[fn]─►│ PriorityIndex  │ (P2+)          │
   │   │  RocksDB CFs │         │ same DB / CF   │                │
   │   └──────┬───────┘         └────────────────┘                │
   │          │ [fs marker]              ▲                        │
   │          ▼                          │ [fn]                   │
   │   ┌──────────────┐                  │                        │
   │   │  Lifecycle   │──[fn]──► ColdStore (Parquet) (P2+)        │
   │   └──────┬───────┘                                           │
   │          │ [fn]                                              │
   │          ▼                                                   │
   │   ┌──────────────┐         ┌────────────────┐                │
   │   │   Sampler    │◄─[fn]───│  ControlPlane  │                │
   │   │ POST /sample │         │  Prov / Reten  │                │
   │   └──────────────┘         │  Sideband / Obs│                │
   │                            └───────┬────────┘                │
   │                                    │                         │
   │   GET /health, /metrics ◄──────────┘                         │
   └────────────────────────────────────────────────────────────┘
```

Sync edges (latency-relevant):
- Q10 → Sampler: HTTP POST, length-delimited protobuf stream. No hard SLA;
  trainer is GPU-coupled.
- Q7 → ObservabilityAdapter: HTTP GET `/metrics`, Prometheus text scrape.

Async edges:
- Q8/Q11 → IngestAPI: HTTP POST, fire-and-forget beyond local buffer flush.
  Back-pressure surfaces as HTTP 503 with `retry_after_sec`.
- Q2 → SidebandRouter: HTTP POST oracle-agreement rows.

Internal edges are in-process function calls today; every boundary is shaped
as if RPC-able so Phase-3 hot-tier shard-out is a refactor, not a rewrite.

## 3. Architecture Characteristics (ranked)

Specialized from the pipeline-level ranking at
`docs/specs/00-system-overview.md` §3.

1. **Throughput / Scalability.** Sustain ≥10⁶ trajectory-step writes/day on a
   single host in Phase 1; scale to 10⁸/day on a sharded hot tier by Phase 3.
   Trainer sample p99 < 100 ms at batch_size=512 uniform. Drives sharded
   keyspace, batched I/O, queue-mediated back-pressure.
2. **Determinism / Reproducibility.** Every accepted trajectory round-trips
   bit-equal. Provenance `(model_version, sampling_mode, generator)` is
   mandatory and non-droppable. Sampling under fixed `(mode, seed, cursor)`
   returns the same rows. Drives schema-versioned wire format, single-writer
   ingest ordering, append-only provenance log.
3. **Evolvability / Patch Adaptability.** Schema drain-and-flip is routine
   (≤5 min planned downtime, never during phase-gate eval, per
   `docs/specs/modules/experience-store.md:45`). Phase-3 hot-tier shard-out
   is a deployment change, not a rewrite. Drives explicit SchemaRegistry
   submodule, typed submodule boundaries, column-family-per-keyspace layout.

Below the line — constraints, not characteristics:
- Latency on sample RPC is GPU-coupled; no hard SLA.
- Internal-only system; no auth / multi-tenancy / PII.
- Hot tier is ephemeral; cold tier is the durability tier (Phase 2+).
- Workers do not slow down if Q3 fills; retention drops oldest.

## 4. Submodule Map

Q3 ships as **one deployable service** (modular monolith — see Q3-ADR-001).
Internal decomposition into 8 submodules, each owning disjoint on-disk state.
Three submodules own schemas; no two share a table or RocksDB column family.

| # | Submodule | Phase 1 | Schema-owning | Persistent data |
|---|---|---|---|---|
| 1 | [IngestAPI](modules/ingest-api.md) | yes | no | none (transient queue) |
| 2 | [SchemaRegistry](modules/schema-registry.md) | yes | **yes** (wire-version policy) | `schema/registry.json`, `schema/migration_log.ndjson` |
| 3 | [HotStore](modules/hot-store.md) | yes | no (rebuildable layout) | `hot/rocksdb/` CFs `traj`, `by_id`, `step_idx` |
| 4 | [ColdStore](modules/cold-store.md) | stub (P2+) | no | `cold/parquet/...`, `cold/index.ndjson` |
| 5 | [Lifecycle](modules/lifecycle.md) | yes (age-drop only) | no | `lifecycle/{policy,cursor,audit}` |
| 6 | [Sampler](modules/sampler.md) | yes (uniform) | no | none (LRU cache) |
| 7 | [PriorityIndex](modules/priority-index.md) | stub (P2+) | **yes** (priority-signal shape) | RocksDB CF `priority`, `priority/sideband.ndjson` |
| 8 | [ControlPlane](modules/control-plane.md) | yes | **yes** (Provenance row shape) | `provenance.ndjson`, `retention/policy.json`, `sideband/oracle.ndjson` |

Trained policies, oracle code, and inference networks are **not** Q3
artifacts. Q3 stores their outputs (trajectories, oracle-agreement signals)
only.

## 5. Where to Look Next

- `01-decisions-log.md` — Q3-internal ADRs (Q3-ADR-001..010). Read before
  challenging any submodule boundary; each ADR's Consequences block leads
  with the negative trade-offs being accepted.
- `modules/<name>.md` — one per submodule: responsibilities, data ownership,
  communication, coupling, testing strategy.
- Cross-quantum ADRs that Q3 inherits: `docs/specs/01-decisions-log.md`
  ADR-001 (service-based pipeline), ADR-006 (Q3-as-async-backbone),
  ADR-014..018 (sample/summary, macro_context, observability regime,
  counterfactual carve-out, macro-owned reward).
- Cross-quantum ADRs that originated here:
  `docs/specs/01-decisions-log.md` ADR-020 (sideband through Q3),
  ADR-021 (Phase-1 degenerate-sample convention).
