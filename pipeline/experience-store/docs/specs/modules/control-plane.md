# Submodule: ControlPlane

> Merged cohesion module — ProvenanceIndex + RetentionController +
> SidebandRouter + ObservabilityAdapter. Each is small (~50 LOC) and
> shares an audit-table posture; merging avoids four near-empty
> submodules without losing the no-shared-tables rule.

Internally organized as four sub-units, each documented below.

## Responsibilities

- **ProvenanceIndex.** Append-only `(trajectory_id, model_version,
  sampling_mode, generator, ingest_ts_ns, schema_version)` log.
  Lookup by `trajectory_id`. Mandatory; non-droppable per ADR-006.
- **RetentionController.** Define and evaluate the sustained-pressure
  predicate from Q3-ADR-008. Tells Lifecycle when to escalate.
- **SidebandRouter.** Receive oracle-agreement payloads from Q2 (per
  ADR-017 / Q3-ADR-004) on `POST /sideband/oracle-agreement`. Phase-1
  stub stores to `sideband/oracle.ndjson`; Phase-2+ forwards into
  PriorityIndex.
- **ObservabilityAdapter.** Compute `/metrics` payload by polling each
  submodule's registered metric callables. Maintain `/health` overlay
  that injects SchemaRegistry's current major version into the schema
  field. Wire the stock `pipeline.common.service_host` `/health` +
  `/metrics` endpoints — must keep `pipeline/tests/smoke_services.py`
  green at every change.

Out of scope: priority-score computation (PriorityIndex); promotion
policy (Lifecycle); schema versions (SchemaRegistry); trajectory
content (cross-quantum).

## Data Ownership

### ProvenanceIndex

- **`provenance.ndjson`** — append-only log. One record per accepted
  trajectory:
  ```json
  {"trajectory_id":"...","model_version":"v3","sampling_mode":"uniform",
   "generator":"rollout_worker","ingest_ts_ns":1734...,"schema_major":1,
   "schema_minor":0}
  ```
  Rotated at 100 MiB; rotation files retained 30 days. **Schema-owning:
  yes** — owns the provenance row shape.
- **`provenance.bloom`** — in-memory Bloom filter for fast negative
  lookups (rebuilt at startup from the log; ~1 ms per 100k entries).

### RetentionController

- **`retention/policy.json`** — sustained-pressure thresholds (the dual
  predicate from Q3-ADR-008).
  ```json
  {"hot_bytes_window_seconds": 60,
   "queue_depth_window_seconds": 30,
   "queue_depth_threshold_fraction": 0.8}
  ```
- Transient in-memory series of recent `(ts, hot_bytes, queue_depth)`
  samples (ring buffer, ~64 entries) for the windowed check.

### SidebandRouter

- **`sideband/oracle.ndjson`** — append-only landing of raw
  oracle-agreement payloads (Phase 1 stub). Phase-2+ forwarded to
  PriorityIndex; landing log retained for audit.

### ObservabilityAdapter

- None persistent. In-process registry of `{metric_name → callable}`.
  Submodules call `register(name, callable, kind, help)` at boot.

Schema-owning? **Yes** (ProvenanceIndex). RetentionController,
SidebandRouter, ObservabilityAdapter own no wire schemas.

No shared tables or CFs with any other submodule. Internally, the four
sub-units own disjoint files.

## Communication

**External (HTTP, sync):**

- `POST /sideband/oracle-agreement` *(from Q2)*
  - Body: oracle-agreement payload (Phase-1: arbitrary JSON; Phase-2+
    schema'd via `contracts/schemas/oracle-agreement/`).
  - Response: `202 Accepted`.
- `GET /provenance/<trajectory_id>` — lookup by id; `404` if unknown.
- `GET /retention/state` — current pressure classification + recent
  series snapshot.
- `GET /health` and `GET /metrics` — **owned here** (overrides the
  default service_host handler at startup hook in service.py).

**Internal (in-process function calls, sync):**

- `provenance.append(trajectory_id, model_version, sampling_mode,
  generator, ingest_ts_ns, schema_version) -> None` — called by
  IngestAPI on every accept. Atomic NDJSON append.
- `provenance.lookup(trajectory_id) -> Provenance | None` — used by
  Sampler (P2+) for enrichment.
- `retention.classify_pressure(hot_bytes, queue_depth, queue_capacity)
  -> Pressure` (enum: Normal / HighWater / Overflow / Sustained).
  Called by Lifecycle.tick().
- `retention.update_sample(ts_ns, hot_bytes, queue_depth)` — pushes
  into ring buffer.
- `sideband.handle_oracle_agreement(payload) -> None` — Phase-1: NDJSON
  append; Phase-2+: also `PriorityIndex.update(...)`.
- `observability.register(name, callable, kind, help) -> None` — boot-time
  metric registration.
- `observability.health_payload() -> dict` — returns `{"service":
  "experience-store", "status": "ok", "schema": <SchemaRegistry.
  current_health_schema()>}`.

**Metrics:**

- `sts2_q3_provenance_rows_total{}` — counter.
- `sts2_q3_provenance_lookup_total{result="hit"|"miss"}` — counter.
- `sts2_q3_retention_pressure_state{state}` — gauge 0/1 per state.
- `sts2_q3_sideband_payloads_total{source="oracle_agreement",
  result="stored"|"forwarded"}` — counter.
- All other `sts2_q3_*` metrics from sibling submodules registered via
  `observability.register`.

## Coupling

- **Afferent (in):** all other Q3 submodules (metrics registration,
  health/retention/provenance queries); Q2 (sideband HTTP).
- **Efferent (out):** filesystem (provenance log, retention policy,
  sideband log); PriorityIndex (P2+ forward); SchemaRegistry (read
  current schema for health overlay).
- **Critical wiring:** `service.py` startup hook replaces the stock
  `Handler.do_GET` for `/health` and `/metrics` paths with
  `ObservabilityAdapter`'s handlers. Smoke test
  (`pipeline/tests/smoke_services.py:61`) is the gating contract.

## Testing Strategy

### Unit — ProvenanceIndex (mock filesystem)

1. **Append-only NDJSON.** Each `append` writes exactly one JSON line +
   `\n`; existing lines never edited. Absent test: silent edits would
   erase audit evidence.
2. **Lookup by id O(log n).** Bloom filter pre-check; on positive,
   linear scan within rotation file (Phase 1 acceptable — 100k entries
   per file is bounded). Absent test: per-row lookup degrades to
   full-log scan at scale.
3. **`model_version=""` rejected.** Provenance row with empty
   `model_version` raises `ProvenanceError`; IngestAPI passes through
   as `500`. Absent test: a misconfigured writer pollutes provenance.

### Unit — RetentionController

1. **Sustained-pressure dual condition.** Inject `(hot_bytes >
   high_water)` sustained for 65 s → `classify_pressure` returns
   `Sustained`. Inject same `hot_bytes` for only 30 s → returns
   `HighWater`. Absent test: Q3-ADR-008 semantics drift silently.
2. **Easing under threshold drops alert.** After `Sustained`, drop
   `hot_bytes` below `high_water` for 1 s → returns `Normal` on next
   call; no data drop initiated. Absent test: hysteresis bug holds
   alerts forever.

### Unit — SidebandRouter

1. **Phase-1 stub stores to NDJSON; no PriorityIndex call.** POST
   oracle-agreement payload → `sideband/oracle.ndjson` has 1 line;
   PriorityIndex.update never called. Absent test: Phase-1 ship
   silently calls into a stub-only PriorityIndex.
2. **Malformed payload → 400.** Body missing required field (Phase-2+
   schema'd) → `400`; nothing written. Absent test: bad data
   poisons the sideband.

### Unit — ObservabilityAdapter

1. **`/metrics` plain text matches smoke contract.** `GET /metrics`
   returns Prometheus text starting with `sts2_service_up{service=
   "experience-store"} 1` (compat with `service_host.py:32` line
   format). Absent test: smoke gate breaks at PR time.
2. **New submodule registers a counter without code change elsewhere.**
   Call `register("sts2_q3_test_total", fn, "counter", "...")`;
   `/metrics` emits it on next scrape. Absent test: every new submodule
   requires a hand-edit of a central metrics function.
3. **`/health` returns 200 silently.** `Handler.log_message` returns
   None per `service_host.py:38`; no stdout from `/health` requests.
   Absent test: noise floods stdout in production.
4. **`/health` schema field reflects SchemaRegistry, not hardcoded 0.**
   After SchemaRegistry flip to (1,1), `/health` returns `"schema": 1`.
   Absent test: smoke contract regresses post-flip.

### Integration

1. **End-to-end smoke (the gate).** `pipeline/tests/smoke_services.py`
   passes after every Q3 PR. ControlPlane is the responsible submodule;
   its absence breaks the gate.
2. **Provenance row written per ingest.** Run 1000 ingests; `wc -l
   provenance.ndjson` == 1000; sampler enrichment returns matching
   provenance for every id. Cross-submodule contract.
3. **Sideband end-to-end (Phase 2+).** Q2 POSTs oracle-agreement;
   `sideband/oracle.ndjson` has the raw payload; `priority` CF in
   RocksDB has the matching `PriorityRecord`; Sampler prioritized mode
   returns the row. Verifies the cross-quantum sideband loop closes.

### Smoke (THIS IS THE GATE)

- `pipeline/tests/smoke_services.py` is the must-pass contract.
- `/health` returns 200 with `{"service":"experience-store","status":
  "ok","schema":1}` (note `schema` is the SchemaRegistry-driven value,
  not the stock placeholder `0`).
- `/metrics` includes `sts2_service_up{service="experience-store"} 1`
  and `sts2_service_uptime_seconds{service="experience-store"} <float>`
  in addition to all `sts2_q3_*` metrics from sibling submodules.
- Silent logging: `log_message` is a no-op (override preserved).
