# Submodule: ColdStore *(Phase 2+; Phase-1 stub)*

> Parquet shards on S3-equivalent. Episode-level partitioning by
> `(date, model_version)`. Read path for stratified backfill. Phase-1
> ships as a disabled stub that fails closed.

## Responsibilities

- *(Phase 2+)* Write episode-level Parquet shards under
  `cold/parquet/date=YYYY-MM-DD/model_version=<id>/<shard>.parquet`.
  Invoked by Lifecycle on hot→cold promotion.
- *(Phase 2+)* Serve read scans for Sampler stratified mode and
  evaluation-harness cold reads.
- *(Phase 2+)* Maintain `cold/index.ndjson` — per-shard metadata
  (`shard_uri`, `row_count`, `min/max ingest_ts_ns`, `schema_version`).
- *(Phase 1)* Ship as a stub: `enabled = false` in config; all write
  methods raise `ColdDisabledError` cleanly; all read methods return
  empty iterators. `/health` reports `cold_tier_enabled: false`.

Out of scope: hot-tier reads (HotStore); promotion policy (Lifecycle);
S3 credential management (operator-provided env vars Phase 2+).

## Data Ownership

*(Phase 2+)*

- **`cold/parquet/date=YYYY-MM-DD/model_version=<id>/<shard-uuid>.parquet`** —
  episode-level Parquet shards. Schema mirrors
  `contracts/schemas/trajectory/trajectory.proto` v1 row-wise:
  one Parquet row per `TrajectoryStep`, plus a `Trajectory`-level metadata
  group (trajectory_id, episode_id, seed, model_version, sampling_mode,
  generator, schema_version_major, schema_version_minor).
- **`cold/index.ndjson`** — append-only index. One record per shard:
  ```json
  {"shard_uri":"...","row_count":N,"min_ts_ns":...,"max_ts_ns":...,
   "schema_major":1,"schema_minor":0,"date":"2026-05-14",
   "model_version":"...","written_ts_ns":...}
  ```
- **`cold/.cold-config.json`** — local-mode root (Phase 2 filesystem-backed);
  contains storage backend URI. Phase 3+ replaces with cloud config.

Schema-owning? **No.** Mirrors trajectory.proto; serialization rules track
SchemaRegistry's `current_write_target`.

No shared tables or column families with any other submodule.

## Communication

**External (HTTP, sync):** none Phase 1. Phase 2+ has no external HTTP
either; ColdStore is internal-only. The external Sampler API
(`POST /sample`) transparently spans tiers.

**Internal (in-process function calls):**

*(Phase 1 stub)*

- `enabled() -> bool` — returns `False`.
- `write_shard(...)` — raises `ColdDisabledError`.
- `scan(...)` — returns empty iterator.
- `stat() -> {"shards": 0, "bytes": 0, "latest_date": null}`.

*(Phase 2+)*

- `write_shard(trajectories: list[Trajectory], model_version: str,
  date: str) -> shard_uri: str` — serialize batch to Parquet, write to
  S3-equivalent, append to `cold/index.ndjson`. Idempotent on the same
  `(shard_uri, batch_id)` pair; safe to retry on partial failure.
- `scan(predicate: Predicate, limit: int) -> Iterator[Row]` — filtered
  Parquet read; predicate supports filters on `date`, `model_version`,
  `decision_type`, `ingest_ts_ns` range. Predicate pushdown to the
  Parquet reader.
- `stat() -> {shards: int, bytes: int, latest_date: date,
  schema_versions: list}`.

**Filesystem events:** none emitted; Lifecycle polls `stat()`.

**Metrics:**

- `sts2_q3_cold_enabled{}` — gauge 0/1.
- `sts2_q3_cold_shards{}` — gauge.
- `sts2_q3_cold_bytes{}` — gauge.
- `sts2_q3_cold_write_shard_total{result="ok"|"err"}` — counter.
- `sts2_q3_cold_scan_rows_total{}` — counter.
- `sts2_q3_cold_read_latency_seconds` — histogram (Phase 2+ only).

## Coupling

- **Afferent (in):** Lifecycle (writes), Sampler (reads, Phase 2+).
- **Efferent (out):** filesystem (Phase 2 local) or S3 client (Phase 3+);
  ControlPlane.ObservabilityAdapter (metrics).
- **Indirect:** SchemaRegistry (read `current_write_target` to stamp
  shards).

## Testing Strategy

### Unit, Phase 1 stub

1. **`enabled() == False` at boot.** Configuration `cold_tier_enabled:
   false` (default). Absent test: a half-enabled cold path silently
   accepts writes that never persist.
2. **`write_shard` raises `ColdDisabledError`.** Cleanly raised, no
   partial filesystem effect. Absent test: Lifecycle's promote-error
   handling never exercised.
3. **`scan` returns empty iterator, never raises.** Sampler can call
   transparently and just get zero cold rows. Absent test: Sampler
   crashes when transitioning Phase 1 → Phase 2.
4. **`/health` reflects `cold_tier_enabled: false`.** Exposed in the
   service's `/health` extension. Absent test: ops can't tell if cold
   tier is wired.

### Unit, Phase 2+

1. **Parquet partition path well-formed.** `write_shard` of trajectories
   dated 2026-05-14 with model_version `v3` writes to
   `cold/parquet/date=2026-05-14/model_version=v3/<uuid>.parquet`.
   Absent test: malformed partitions break partition-pruning in scans.
2. **Idempotent re-write of same shard.** Second `write_shard` with
   identical batch and same target uri does not duplicate index entries.
   Absent test: retries duplicate data.
3. **Schema-version stamp on shard.** Written shard's
   `schema_major`/`schema_minor` match SchemaRegistry's
   `current_write_target`. Absent test: cold-tier reads under mixed-
   schema retention go subtly wrong.
4. **Predicate pushdown.** `scan(date="2026-05-14")` reads only shards
   under that date partition; `scan(model_version="v3")` reads only that
   version. Absent test: full-scan performance regressions are
   invisible.

### Integration

1. **Round-trip via Lifecycle promotion.** Write 1000 trajectories to
   HotStore; Lifecycle promotes; ColdStore.scan returns 1000 rows byte-
   equal to originals. Verifies hot→cold→read pipeline.
2. **Index file durability.** Crash service mid-write_shard; on restart,
   index has either the entry (write committed) or no entry (write
   rolled back); never a half-written line. Verifies index atomicity.
3. **(Phase 3+)** S3-equivalent retry on transient failures: simulate
   503s from storage; `write_shard` succeeds after exponential backoff.
   Verifies upload resilience.

### Smoke (mandatory, Phase 1)

- `/health` returns 200 with `cold_tier_enabled: false` extension on
  initial boot. The stock smoke test (`pipeline/tests/smoke_services.py`)
  is unaware of this extension and ignores it; richer Phase-2 smoke adds
  cold-enabled checks.
- `/metrics` includes `sts2_q3_cold_enabled 0`.
