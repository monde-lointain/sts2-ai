# Submodule: IngestAPI

> Write-path HTTP front door. Validates schema version; queues to HotStore;
> back-pressure via HTTP 503 / 423. Owns no persistent state.

## Responsibilities

- Accept trajectory append requests via `POST /trajectories` and
  `POST /trajectories:batch`. Body is wire-format protobuf per
  `contracts/schemas/trajectory/trajectory.proto` v1; content type
  `application/x-protobuf`.
- Consult [SchemaRegistry](schema-registry.md) per request to validate
  `Trajectory.schema_version` against the current accepted set and the
  drain state.
- Enqueue accepted trajectories on an in-memory bounded queue read by
  [HotStore](hot-store.md). Queue cap default 4096 trajectories.
- Apply back-pressure when the queue is at capacity (HTTP 503,
  `retry_after_sec`).
- Apply schema-drain rejection (HTTP 423) for stale-version writes during a
  drain window per [Q3-ADR-006](../01-decisions-log.md#q3-adr-006--schema-migration-ux-via-503--retry_after_sec).
- Emit an audit log entry on every accepted ingest into
  [ControlPlane](control-plane.md) ProvenanceIndex; mandatory, non-droppable.
- Expose `GET /ingest/status` for writer-side health probes.

Out of scope: durability (HotStore), provenance index format (ControlPlane),
schema version policy (SchemaRegistry).

## Data Ownership

None persistent. Transient in-process:

- **In-memory bounded queue** — `queue.Queue(maxsize=ingest_queue_capacity)`,
  default cap 4096. Read by HotStore's single consumer thread.
- **Per-request transient state** — request body buffer, parsed
  `Trajectory` message; freed after enqueue.

Schema-owning? **No.** Defers to SchemaRegistry.

No shared tables or column families with any other submodule.

## Communication

**External (HTTP, sync):**

- `POST /trajectories`
  - Headers: `Content-Type: application/x-protobuf`,
    `Content-Length: <= max_body_bytes` (default 64 MiB).
  - Body: serialized `Trajectory` message.
  - Response: `202 Accepted` `{"trajectory_id": <str>, "ingest_ts_ns": <int>}`
    on success; `400` malformed; `413` body too large; `415` wrong
    content-type; `423` schema-drain stale-version; `503` queue full
    `{"retry_after_sec": <int>}`.
- `POST /trajectories:batch`
  - Body: length-delimited frames; each frame a `Trajectory`. Atomic per
    frame (one bad frame fails only that frame); response array.
- `GET /ingest/status`
  - Response: `{"queue_depth": int, "queue_capacity": int,
    "accepted_total": int, "rejected_total": int,
    "schema_drain_state": "open"|"draining"|"locked"}`.

**Internal (in-process function calls, sync):**

- `SchemaRegistry.validate(schema_version, drain_state) -> Decision` —
  per request.
- `ControlPlane.provenance.append(trajectory_id, model_version,
  sampling_mode, generator, ingest_ts_ns, schema_version) -> None` —
  on every accept; failure aborts the ingest with 500.

**Internal (queue, async):**

- `ingest_queue.put_nowait(trajectory_bytes, trajectory_id, ingest_ts_ns)`
  — back-pressure surfaces here as `queue.Full` → HTTP 503.

**Metrics emitted to ControlPlane.ObservabilityAdapter:**

- `sts2_q3_ingest_accepted_total{}` — counter.
- `sts2_q3_ingest_rejected_total{reason="schema_drain"|"queue_full"|
  "schema_unknown"|"malformed"|"too_large"}` — counter.
- `sts2_q3_ingest_queue_depth{}` — gauge.
- `sts2_q3_ingest_bytes_total{}` — counter.

## Coupling

- **Afferent (in):** Q8 rollout-workers, Q11 curriculum-generator. (Q3
  external boundary.)
- **Efferent (out, internal):** SchemaRegistry, ControlPlane.ProvenanceIndex,
  HotStore (via queue), ControlPlane.ObservabilityAdapter (metrics).
- **Efferent (out, external):** none.
- **Indirect:** writer-side supervisor / retry loop in Q8/Q11.

## Testing Strategy

### Unit (mock all I/O)

1. **Wrong content-type → 415.** POST with `application/json` → returns
   `415 Unsupported Media Type` and increments
   `sts2_q3_ingest_rejected_total{reason="malformed"}` (or `reason="content_type"`
   if separated). Absent test: a misconfigured writer could silently land
   JSON bytes into RocksDB.
2. **Body over max → 413.** POST with `Content-Length` > `max_body_bytes`
   → `413 Payload Too Large`, queue depth unchanged. Absent test: a
   runaway trajectory (e.g., 1M-step combat from a buggy worker) OOM-kills
   the service.
3. **Well-formed protobuf, valid schema → 202.** POST a Trajectory whose
   `schema_version` matches SchemaRegistry.current_write_target → `202`,
   `trajectory_id` returned, queue depth +1, provenance.append called once.
   Absent test: the happy path could regress silently.
4. **Queue full → 503 with retry_after_sec.** Queue at capacity, POST →
   `503 {"retry_after_sec": int>0}`. Provenance NOT appended.
   Absent test: a back-pressure regression silently drops trajectories on
   the floor.
5. **Schema drain, stale version → 423.** SchemaRegistry in `draining`,
   POST with stale `schema_version` → `423 Locked`. POST with target
   `schema_version` → `202` (still accepted). Absent test: drain windows
   don't actually drain.
6. **Provenance append failure → 500, no enqueue.** ControlPlane raises on
   append → IngestAPI returns `500`, queue depth unchanged. Mitigation
   for partial-state-write hazards. Absent test: provenance can drift
   from actual rows on the floor.

### Integration

1. **End-to-end ingest → HotStore → range scan.** POST 100 well-formed
   trajectories sequentially; after a flush, HotStore.scan returns the
   same 100 with monotonic `ingest_ts_ns` and bit-equal bytes. Verifies
   queue → HotStore → RocksDB pipeline.
2. **Drain → flip → ingest.** Operator triggers
   `POST /schema/drain {target: 1.1}`; pre-flip stale-version POSTs
   receive 423; target-version POSTs receive 202; after
   `POST /schema/flip`, all subsequent POSTs accept target version.
   Verifies multi-submodule drain coordination.
3. **Provenance row written for every accepted ingest.** 1000 POSTs →
   ProvenanceIndex log has exactly 1000 entries with matching
   `trajectory_id`s, model_versions, generators. Verifies the mandatory
   provenance contract.

### Smoke (mandatory)

- `pipeline/tests/smoke_services.py` boots `service.py` and asserts
  `/health == {"service":"experience-store","status":"ok","schema":<int>}`
  and `/metrics` contains `sts2_service_up{service="experience-store"} 1`.
  IngestAPI must not break either contract (no request-handling on
  `/health` / `/metrics` paths; ObservabilityAdapter owns those).
- `/health` returns 200 even while the ingest queue is non-empty.
