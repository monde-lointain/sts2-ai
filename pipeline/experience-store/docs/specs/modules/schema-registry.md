# Submodule: SchemaRegistry

> Single source-of-truth for accepted `(major, minor)` wire-schema versions.
> Owns the drain↔flip migration FSM. Rejects stale-version writes
> structurally. Schema-owning.

## Responsibilities

- Maintain the accepted-versions set, current write-target, and drain-state
  (`open`, `draining`, `locked`) — the FSM that gates schema migrations.
- Validate `(major, minor)` on every IngestAPI POST and every Sampler read
  — same call signature; one source of truth.
- Drive operator-initiated migrations: `drain` (start refusing stale-version
  writes; allow target-version), `flip` (mark target as current, archive
  prior version), `revert` (Phase-2+ undo before flip completes).
- Append-only audit log of every state transition for post-mortem analysis.
- Surface schema state on `GET /schema` so writers and readers can
  introspect.
- Update the `schema` field in the service's `/health` response (replaces
  the placeholder `0` from `pipeline/common/service_host.py:26`).

Out of scope: trajectory schema content itself (cross-quantum at
`contracts/schemas/trajectory/trajectory.proto`); routing or storage
decisions (delegated to other submodules).

## Data Ownership

- **`schema/registry.json`** — current state.
  ```json
  {
    "accepted": [{"major": 1, "minor": 0}],
    "current_write_target": {"major": 1, "minor": 0},
    "drain_state": "open",
    "drain_target": null
  }
  ```
  Atomic writes via temp + rename.
- **`schema/migration_log.ndjson`** — append-only audit. One JSON record
  per state transition: `{ts_ns, action, from, to, operator}`. Rotated at
  10 MiB (10 most-recent files retained).
- **`schema/<v>.active` filesystem markers** — sentinel files emitted on
  flip; Lifecycle's promotion logic reads these to stamp cold-tier shards
  with the right schema version.

Schema-owning? **Yes.** Owns Q3's wire-version policy and the migration
state machine. Distinct from the trajectory schema itself, which is
cross-quantum.

No shared tables or column families with any other submodule.

## Communication

**External (HTTP, sync):**

- `GET /schema`
  - Response: `{accepted: [{major, minor}],
    current_write_target: {major, minor},
    drain_state: "open"|"draining"|"locked",
    drain_target: {major, minor} | null}`.
- `POST /schema/drain` *(operator-only; bearer token via
  `config/local.json` `operator_token` Phase 1)*
  - Body: `{target: {major, minor}}`.
  - Effect: `drain_state = "draining"`, `drain_target = target`. From now
    on, IngestAPI 423s stale-version writes; target-version writes accept.
  - Response: `200` with new state.
- `POST /schema/flip` *(operator-only)*
  - Body: empty.
  - Precondition: `drain_state == "draining"` and (Phase-2+) drain-cursor
    indicates all in-flight stale-version writes have completed.
  - Effect: archive prior version, promote target to `current_write_target`,
    set `drain_state = "locked"` momentarily during flip, then `"open"`.
  - Response: `200` with new state.
- `POST /schema/revert` *(operator-only; Phase 2+)*
  - Effect: returns `drain_state` to `"open"`, clears `drain_target`. No-op
    if already open.

**Internal (in-process function calls, sync):**

- `validate(trajectory_schema_version, op="read"|"write") -> Decision`
  where Decision is `Accept` or `Reject(reason, http_status)`.
  - Phase-1 rule set:
    - Unknown version (not in `accepted`) → `Reject("schema_unknown", 400)`.
    - `op="write"` and `drain_state=="draining"` and `version !=
      drain_target` → `Reject("schema_drain_stale", 423)`.
    - `op="read"` and `drain_state=="locked"` → `Reject("schema_flip",
      503, retry_after_sec=5)`.
    - Else → `Accept`.
- `current_health_schema() -> int` — returns
  `current_write_target.major`. Read by ControlPlane.ObservabilityAdapter
  to override the stock `/health` response's `"schema": 0`.

**Filesystem events:**

- On flip complete, write `schema/<major>.<minor>.active` sentinel; remove
  the prior `.active`. Lifecycle reads this to stamp cold-tier shards.

**Metrics:**

- `sts2_q3_schema_state{state="open"|"draining"|"locked"}` — gauge 0/1.
- `sts2_q3_schema_validate_total{op,result}` — counter.
- `sts2_q3_schema_migration_total{from,to}` — counter (transitions).

## Coupling

- **Afferent (in):** IngestAPI (per-request validate), Sampler
  (per-request validate), Lifecycle (read current target for cold-tier
  stamping), operators (HTTP).
- **Efferent (out):** filesystem (registry.json, audit log, sentinels);
  ControlPlane.ObservabilityAdapter (metrics, `/health` schema field).
- **Indirect:** Q10 trainer (consumes `retry_after_sec` from Sampler 503).

## Testing Strategy

### Unit (mock filesystem)

1. **Default `current_write_target` matches proto header.** On boot with no
   prior state, default to `(1, 0)` matching
   `contracts/schemas/trajectory/trajectory.proto:5` `sts2.schema.major=1,
   minor=0`. Absent test: a wrong default would silently reject every
   incoming trajectory.
2. **Unknown version → structured reject.** `validate({major:9, minor:9},
   op="write")` returns
   `Reject("schema_unknown", 400, {accepted: [{major:1,minor:0}]})`.
   Absent test: writers get opaque 400s with no actionable diagnostic.
3. **Drain FSM rejects flip while drain incomplete.** `drain → flip`
   without (Phase-2+) drain-cursor at zero raises operator error 409.
   Absent test: schema flips mid-write, corrupting in-flight bytes.
4. **Audit log is append-only.** `validate` does not write; only `drain` /
   `flip` / `revert` do. After 100 transitions, log file size equals
   100 × record size; no edits. Absent test: silent edits would erase
   post-mortem evidence.
5. **`/health` schema field updates after flip.** Flip from (1,0) to (1,1);
   subsequent `/health` returns `"schema": 1` (major). Absent test:
   smoke-test invariant could go stale after a real flip.

### Integration

1. **End-to-end drain blocks stale ingest.** Operator POST `/schema/drain
   {target:{major:1,minor:1}}`; IngestAPI POST with `(1,0)` → 423; POST
   with `(1,1)` → 202. Verifies cross-submodule drain enforcement.
2. **Migration log persists across restarts.** Trigger drain, flip,
   restart service; `GET /schema` reports `current_write_target =
   (1, 1)`; migration_log.ndjson has the two records. Verifies
   `registry.json` atomic-write contract.
3. **Sentinel file written on flip.** After flip to (1,1),
   `schema/1.1.active` exists; previous `schema/1.0.active` removed.
   Lifecycle test asserts it can read the current sentinel.

### Smoke (mandatory)

- `/health` returns 200 with `"schema": <current major>` (replaces the
  hardcoded `0` from service_host.py:26). The cross-submodule wiring
  hook lives in service.py at startup; smoke test asserts the override is
  applied before the first request.
- `/metrics` includes `sts2_q3_schema_state{state="open"} 1` at boot.
