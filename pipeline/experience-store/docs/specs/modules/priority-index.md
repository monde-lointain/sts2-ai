# Submodule: PriorityIndex *(Phase 2+; Phase-1 stub)*

> Per-trajectory or per-step priority scores for prioritized replay.
> Floor-bounded TD-error + oracle-agreement signals. Top-K serve under
> filter predicates. RocksDB column family colocated in HotStore's DB
> file (Q3-ADR-010). Schema-owning.

## Responsibilities

- *(Phase 2+)* Receive priority updates from Q10 trainer (TD errors) and
  from ControlPlane.SidebandRouter (oracle-agreement signals from Q2).
- *(Phase 2+)* Maintain a `(score_bucket, ingest_ts_ns, trajectory_id) →
  PriorityRecord` keyspace in RocksDB CF `priority` (in HotStore's DB
  file, but PriorityIndex-owned).
- *(Phase 2+)* Serve `top_k(k, filters)` to Sampler prioritized mode.
- *(Phase 2+)* Enforce per-source priority floor and ceiling to avoid
  starvation (no source dominates; no source vanishes).
- *(Phase 1)* Ship as a stub: `enabled = false`; all update and read
  methods are no-ops; emit a single metric showing disabled state.

Out of scope: priority-score computation (Q10 + Q2 produce); replay
sampling policy (Sampler composes; PriorityIndex serves raw top-K).

## Data Ownership

*(Phase 2+)*

- **RocksDB column family `priority`** — same DB file as HotStore
  (Q3-ADR-010), separate keyspace.
  - Key: 25-byte fixed `(score_bucket: uint8 (descending = highest first),
    ingest_ts_ns: uint64, trajectory_id: bytes16)`.
  - Value: serialized `PriorityRecord` protobuf:
    ```protobuf
    message PriorityRecord {
      float    score = 1;             /* raw, unbucketed */
      string   source = 2;            /* "td_error" | "oracle_agreement" | ... */
      uint32   step_idx = 3;          /* 0 = trajectory-level */
      uint64   last_update_ts_ns = 4;
    }
    ```
- **`priority/sideband.ndjson`** — append-only landing zone for
  raw sideband payloads from Q2 before they're written into RocksDB.
  Owned by PriorityIndex; SidebandRouter (ControlPlane) is the only
  writer.

- **`contracts/schemas/priority-signal/priority_signal.proto`** *(Phase 2+
  contract file)* — wire schema for `POST /priority/update` payloads.
  Package `sts2.q3.priority.v1`. Versioned alongside trajectory.proto
  via SchemaRegistry.

Schema-owning? **Yes.** Owns the priority-signal wire shape (small
schema, separate from trajectory.proto).

## Communication

**External (HTTP, sync; Phase 2+):**

- `POST /priority/update`
  - Body: `priority_signal.proto` payload — `{trajectory_id, step_idx?,
    score, source}`.
  - Effect: insert/update record in `priority` CF; idempotent on
    `(trajectory_id, step_idx, source)` (last-write-wins per source).
  - Response: `202 Accepted`.
- `GET /priority/<trajectory_id>` — diagnostic read.

**Internal (in-process function calls):**

*(Phase 1 stub)*

- `enabled() -> bool` — returns `False`.
- `update(...)` — no-op; logs at DEBUG.
- `top_k(k, filters)` — returns empty list.

*(Phase 2+)*

- `update(trajectory_id, step_idx, score, source)` — atomic write to
  `priority` CF; enforces per-source floor/ceiling.
- `top_k(k: int, filters: PriorityFilters) -> list[(trajectory_id,
  step_idx, score)]` — descending-score iteration over `priority` CF;
  filter by source / model_version (cross-references HotStore for
  model_version, P2+).
- `compaction_tick()` — periodic re-bucketing to keep top-K reads cheap.

**Metrics:**

- `sts2_q3_priority_enabled{}` — gauge 0/1.
- `sts2_q3_priority_updates_total{source}` — counter.
- `sts2_q3_priority_top_k_seconds` — histogram (P2+).
- `sts2_q3_priority_bucket_size{bucket}` — gauge (P2+).
- `sts2_q3_priority_floor_clamps_total{source}` — counter.

## Coupling

- **Afferent (in, Phase 2+):** Q10 trainer (TD updates),
  ControlPlane.SidebandRouter (oracle-agreement signals), Sampler
  (top_k).
- **Efferent (out):** HotStore DB file (column family `priority`),
  ControlPlane.ObservabilityAdapter (metrics).
- **Constraint:** HotStore submodule code MUST NOT read/write
  `priority` CF (Q3-ADR-010 ownership rule).

## Testing Strategy

### Unit, Phase 1 stub

1. **`enabled() == False`.** Returns False. Absent test: a half-enabled
   priority path silently accepts and forgets updates.
2. **`update` is a no-op.** Returns cleanly; RocksDB `priority` CF empty.
   Absent test: pre-Phase-2 writes leak into the CF and skew Phase-2
   first read.
3. **`top_k` returns `[]`.** Sampler's prioritized mode falls back to
   uniform Phase 1 only by config; PriorityIndex stub returns empty.
   Absent test: prioritized mode silently breaks.

### Unit, Phase 2+

1. **Idempotent update by `(trajectory_id, step_idx, source)`.** Three
   updates from `source="td_error"` for the same row → final record
   has the LAST score; bucket bookkeeping correct. Absent test:
   replays of the same update accumulate phantom rows.
2. **Floor / ceiling enforced.** Update with `score < floor` clamps to
   floor; `score > ceiling[source]` clamps to ceiling. Absent test:
   one signal can starve others.
3. **`top_k` is sorted by score descending.** 10k random updates;
   `top_k(100)` returns rows in non-increasing score order. Absent
   test: prioritized sampling silently uniformizes.
4. **Oracle-agreement source has independent ceiling.** Floods of
   td_error don't push oracle-agreement out of top_k. Absent test:
   training prioritization loses oracle-agreement signal under
   typical TD-error volume.
5. **Bucket compaction preserves top-K invariant.** After
   `compaction_tick`, `top_k(100)` returns the same rows as before
   (modulo equal-score ordering). Absent test: re-bucketing silently
   loses top rows.

### Integration

1. **Sampler prioritized mode actually biases.** Update 1k random
   trajectories with score U[0,1]; configure Sampler `mode=prioritized,
   batch_size=64`; over 10k runs, top-quartile rows appear with mean
   weight ≥ 3× bottom-quartile (statistical test, 99% CI). Absent
   test: prioritization regresses to uniform.
2. **Sideband ingestion lands in PriorityIndex.** ControlPlane.SidebandRouter
   receives 100 oracle-agreement payloads from Q2; within 1 s,
   `priority` CF has 100 corresponding records with `source="oracle_
   agreement"`. Verifies the cross-submodule sideband pipeline.

### Smoke

- *(Phase 1)*: `/metrics` includes `sts2_q3_priority_enabled 0`.
- *(Phase 2+)*: `/metrics` includes `sts2_q3_priority_enabled 1` and
  `sts2_q3_priority_updates_total` counter is registered.
