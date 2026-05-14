# Submodule: Lifecycle

> Tiering engine. Moves trajectories hot→cold on age or hot-tier-size
> thresholds; drops oldest when ColdStore is unavailable and pressure is
> sustained (per Q3-ADR-008). Background thread; one tick every N seconds.

## Responsibilities

- Run a background thread (`lifecycle_tick`, default every 60 s) that
  inspects HotStore size and the IngestAPI queue depth.
- **Phase 1 (cold-tier-disabled mode):** age-only drop. Beyond
  `hot_overflow_bytes` (Q3-ADR-007), Lifecycle invokes
  `HotStore.delete_range(until_ts_ns)` to evict the oldest range and
  increments the `retention_drop_total` counter (alert-grade event).
- **Phase 2+:** promote-then-drop. On `hot_bytes > hot_high_water_bytes`,
  promote oldest range to ColdStore via `ColdStore.write_shard`; only
  drop if ColdStore is unavailable and pressure is sustained per
  Q3-ADR-008.
- Maintain `lifecycle/cursor.json` — last-promoted `ingest_ts_ns`. Idempotent
  across restarts: a crash mid-promote resumes from `cursor` not from zero.
- Emit `lifecycle/audit.ndjson` records on every promotion, drop, and
  policy update.
- Honor `RetentionController.is_under_sustained_pressure()` — the dual
  time-windowed predicate from Q3-ADR-008 — for the escalation decision.

Out of scope: priority-aware drop (Phase-2+ revisit, not Phase 1);
ColdStore internal Parquet shape; sustained-pressure definition (lives in
ControlPlane.RetentionController).

## Data Ownership

- **`lifecycle/policy.json`** — current thresholds.
  ```json
  {
    "hot_high_water_bytes": 53687091200,   /* 50 GiB */
    "hot_overflow_bytes":  107374182400,   /* 100 GiB */
    "tick_interval_seconds": 60,
    "cold_tier_enabled": false,
    "max_age_seconds": null                /* Phase 2+ optional */
  }
  ```
- **`lifecycle/cursor.json`** — `{last_promoted_ts_ns: int,
  last_tick_ts_ns: int}`. Atomic write via temp + rename.
- **`lifecycle/audit.ndjson`** — append-only event log. Records:
  ```json
  {"ts_ns": ..., "action": "promote"|"drop"|"policy_update",
   "until_ts_ns": ..., "rows": N, "bytes": B, "reason": "..."}
  ```
  Rotated at 10 MiB.

Schema-owning? **No.** Policy schema is internal Q3 config; not on the
wire.

No shared tables or CFs with any other submodule.

## Communication

**External (HTTP, sync):**

- `GET /lifecycle/status` — `{policy, cursor, last_tick_action,
  hot_bytes, cold_bytes, retention_drops_total}`.
- `POST /lifecycle/policy` *(operator-only)* — update policy fields;
  persists to `policy.json` atomically.
- `POST /lifecycle/force_tick` *(operator-only)* — synchronously run one
  tick; useful for ops + tests.

**Internal (in-process function calls):**

- `tick() -> TickResult` — one iteration:
  1. Read `hot_bytes = HotStore.range_size_bytes()`.
  2. Consult `RetentionController.classify_pressure(hot_bytes,
     queue_depth)` → `Normal` / `HighWater` / `Overflow` / `Sustained`.
  3. Action per classification:
     - `Normal` → no-op.
     - `HighWater` + cold-enabled (P2+) → promote oldest range.
     - `Overflow` (cold disabled or sustained pressure with cold-failure) →
       drop oldest range.
     - `Sustained` → escalate alert (`retention_drop_imminent`) +
       proceed with drop.
  4. Update `cursor.json`, append `audit.ndjson` entry, emit metrics.
- `force_promote(until_ts_ns: int) -> PromoteResult` — operator-driven
  promotion bounded by current cursor.
- `set_policy(policy: dict) -> None` — atomic policy update; validates.

**Background thread:**

- `_run_loop()` — calls `tick()` every `tick_interval_seconds`; exits on
  shutdown signal. Started by service.py at startup; joined on shutdown.

**Metrics:**

- `sts2_q3_lifecycle_promoted_rows_total{}` — counter (Phase 2+).
- `sts2_q3_lifecycle_dropped_rows_total{reason="overflow"|
  "sustained_pressure"|"cold_unavailable"}` — counter.
- `sts2_q3_lifecycle_cursor_ts_ns{}` — gauge.
- `sts2_q3_lifecycle_tick_seconds` — histogram.
- `sts2_q3_lifecycle_pressure_state{state="normal"|"high"|"overflow"|
  "sustained"}` — gauge 0/1.
- `sts2_q3_lifecycle_last_tick_ts_ns{}` — gauge.

## Coupling

- **Afferent (in):** operators (HTTP).
- **Efferent (out):** HotStore (range_size_bytes, scan, delete_range,
  compact_range), ColdStore (write_shard, stat — P2+),
  ControlPlane.RetentionController (classify_pressure),
  ControlPlane.ObservabilityAdapter (metrics), filesystem (policy,
  cursor, audit).
- **Indirect:** SchemaRegistry (read `current_write_target` to stamp
  cold-tier shards Phase 2+).

## Testing Strategy

### Unit (mock HotStore / ColdStore / RetentionController)

1. **No-op tick when `Normal`.** RetentionController returns `Normal`;
   `tick()` makes no HotStore mutations; cursor unchanged; audit log
   unchanged. Absent test: a misbehaving tick churns the audit log
   under steady state.
2. **`HighWater` + cold-enabled → promote.** Mock RetentionController →
   `HighWater`; cold_tier_enabled=True; `tick()` calls
   `ColdStore.write_shard` then `HotStore.delete_range`; cursor advances.
   Absent test: Phase-2 promote regresses silently.
3. **`Overflow` + cold-disabled → drop.** RetentionController → `Overflow`;
   cold disabled; `tick()` calls `HotStore.delete_range` without
   `ColdStore.write_shard`; `retention_drop_total` increments.
   Absent test: Phase 1 retention-drop regresses; alerting goes dark.
4. **`Sustained` escalates alert + drops.** RetentionController →
   `Sustained`; `tick()` emits alert metric AND drops. Audit log records
   `reason="sustained_pressure"`. Absent test: the load-bearing
   "retention drops oldest under sustained pressure" semantics of
   ADR-006 are unverified.
5. **`force_promote(until_ts)` bounded by cursor.** `until_ts < cursor`
   → no-op (idempotent). `until_ts > cursor` → promote up to
   `until_ts`, advance cursor. Absent test: operator-driven promotion
   can rewind history.
6. **Cold-failure during promote → no cursor advance.** ColdStore.write_shard
   raises; cursor unchanged; HotStore.delete_range NOT called; audit
   records `action="promote"`, `reason="cold_error"`. Absent test:
   partial promotes lose data.

### Integration

1. **End-to-end retention drop under synthetic overflow.** Configure
   `hot_overflow_bytes = 100 MiB`; ingest 200 MiB of trajectories;
   trigger force_tick; verify oldest range gone, newest preserved,
   audit log + retention-drop counter both incremented. Verifies the
   Phase-1 critical path.
2. **Crash-recovery: cursor persists.** Run 5 ticks; SIGKILL; on
   restart, cursor matches last persisted value; tick 6 resumes from
   there. Verifies idempotence on restart.
3. **Background thread is started and stopped cleanly.** Boot service;
   verify `_run_loop` thread alive; shutdown (SIGTERM); verify clean
   join within `tick_interval_seconds + 10 s`. Verifies clean
   shutdown.

### Smoke (mandatory)

- Background thread starts at service startup hook in `service.py` and
  does not block `/health` or `/metrics`. Smoke test (
  `pipeline/tests/smoke_services.py`) boots and shuts down within 5 s;
  Lifecycle thread must respect shutdown.
- `/metrics` includes `sts2_q3_lifecycle_last_tick_ts_ns` after the
  first tick.
