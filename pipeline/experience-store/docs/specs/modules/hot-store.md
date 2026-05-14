# Submodule: HotStore

> Single owner of the RocksDB hot-tier instance. Batched append, point
> reads, range scans. Column-family-partitioned keyspace. Sharded out-of-
> process in Phase 3+; same API surface.

## Responsibilities

- Own the RocksDB handle for the hot-tier database file. Exactly one
  HotStore instance per Q3 process (Phase 1); Phase 3+ replicates the
  instance across shards but each instance still owns its own DB.
- Drain the IngestAPI queue in a dedicated consumer thread; batch-write
  trajectories into RocksDB column family `traj`.
- Maintain the `by_id` column family (trajectory_id → ingest_ts_ns) for
  point reads.
- Phase-3 maintain `step_idx` column family for stratified-by-decision-type
  reads. Phase-1: column family declared but empty.
- Serve in-process reads to Sampler (point + range scan) and Lifecycle
  (range scan + delete) — no external HTTP for these.
- Emit periodic flush checkpoints as filesystem markers
  (`hot/checkpoint/<seq>.marker`) for Lifecycle's tiering cursor.
- Expose RocksDB stats (level sizes, write amplification, compaction
  status) to ControlPlane.ObservabilityAdapter.

Out of scope: PriorityIndex CF (separate submodule, same DB file —
Q3-ADR-010); cold-tier reads (ColdStore); trajectory bytes content
(serialized opaquely, schema is cross-quantum).

## Data Ownership

- **`hot/rocksdb/`** — RocksDB DB directory.
  - **Column family `traj`** — primary trajectory store.
    - Key: 24-byte fixed `(ingest_ts_ns: uint64 big-endian, trajectory_id:
      bytes16 random)`. Big-endian on ts gives lex-order = time-order for
      range scans.
    - Value: serialized `Trajectory` protobuf bytes.
  - **Column family `by_id`** — point-lookup index.
    - Key: `trajectory_id` (16 bytes).
    - Value: 8-byte big-endian `ingest_ts_ns`.
  - **Column family `step_idx`** *(Phase 3+ — declared, empty Phase 1)*.
    - Key: `(decision_type: uint8, ingest_ts_ns: uint64, trajectory_id:
      bytes16, step_idx: uint16)`.
    - Value: empty (presence-only). Built incrementally by HotStore's
      ingest consumer.
- **`hot/checkpoint/<seq>.marker`** — small JSON file written on flush
  boundaries. `{seq: int, latest_ingest_ts_ns: int, ts: iso8601}`.
  Lifecycle consumes; never deletes the latest.

Schema-owning? **No.** Storage layout is internal and rebuildable from
cold tier (Phase 2+) or replayable from upstream Q8/Q11.

Owned exclusively. PriorityIndex owns the `priority` column family in the
same DB file (Q3-ADR-010); HotStore code MUST NOT touch `priority`.

## Communication

**External (HTTP, sync):** none. HotStore has no HTTP surface.

**Internal (in-process function calls, sync):**

- `append(traj_bytes: bytes, trajectory_id: bytes16) -> int` —
  returns assigned `ingest_ts_ns` (monotonic; CAS-protected on the
  in-proc clock). Atomically writes both `traj` and `by_id` CFs.
  Backpressure: blocks briefly if RocksDB write-stall is active (RocksDB
  internal); never silently drops.
- `read(trajectory_id: bytes16) -> bytes | None` — `by_id` lookup
  followed by `traj` read; `None` if not found (never raises on miss).
- `scan(after_ts_ns: int, limit: int) -> Iterator[(ingest_ts_ns,
  trajectory_id, traj_bytes)]` — range scan starting strictly after
  `after_ts_ns`, capped at `limit`. Used by Sampler uniform mode and
  Lifecycle promotion.
- `delete_range(until_ts_ns: int) -> int` — used by Lifecycle to evict
  promoted-or-dropped trajectories; returns count of deleted keys.
  Atomic across `traj` + `by_id` (and Phase-3 `step_idx`).
- `range_size_bytes() -> int` — approximate hot-tier total bytes for
  Lifecycle's high-water check.
- `compact_range(start_ts_ns: int, end_ts_ns: int) -> None` — explicit
  compaction trigger; called by Lifecycle after large deletes.
- `stats() -> dict` — RocksDB stats for ObservabilityAdapter.

**Filesystem events:**

- Emit `hot/checkpoint/<seq>.marker` after every WAL flush (RocksDB's
  internal flush callback). Lifecycle's background tick reads the latest.

**Metrics:**

- `sts2_q3_hot_bytes{}` — gauge (approximate range_size_bytes).
- `sts2_q3_hot_keys{cf}` — gauge.
- `sts2_q3_hot_write_total{cf}` — counter.
- `sts2_q3_hot_read_total{cf, result="hit"|"miss"}` — counter.
- `sts2_q3_hot_compaction_pending{}` — gauge.
- `sts2_q3_hot_write_stall_seconds_total{}` — counter.

## Coupling

- **Afferent (in):** IngestAPI (writes via queue), Sampler (range scan +
  point read), Lifecycle (range scan, delete_range, compact_range).
- **Efferent (out):** filesystem (RocksDB DB dir, checkpoint markers);
  ControlPlane.ObservabilityAdapter (stats).
- **Constraint:** PriorityIndex shares the DB file (Q3-ADR-010) but owns
  the `priority` CF; HotStore code does not access it.

## Testing Strategy

### Unit (use RocksDB in-memory or tmp dir; no other mocks needed)

1. **Monotonic ingest_ts_ns under concurrent in-proc callers.** N=8 threads
   each call `append` 100 times; collected ts values are strictly
   monotonic across all threads. Absent test: time-order range scans
   return mis-ordered results.
2. **Range scan honors `limit` exactly.** Insert 1000 trajectories;
   `scan(after_ts=0, limit=100)` returns exactly 100, with the
   chronologically first 100. Absent test: Sampler reads under-or-over-
   reach the cap.
3. **Point read of unknown id → `None`, not raise.** `read(b"\x00"*16)`
   returns `None`. Absent test: a missing-row exception bubbles up to
   HTTP 500 unnecessarily.
4. **Round-trip bytes-equal.** Random 10 KB blobs in → same bytes out via
   `read` and via `scan`. Absent test: a serialization corruption goes
   undetected for the lifetime of a trajectory.
5. **`delete_range` atomic across CFs.** Delete `[0, ts_N]`; `traj` and
   `by_id` agree (no orphaned `by_id` entries). Absent test: dangling
   index entries accumulate.
6. **Stats include compaction-pending and write-stall counters.** Force
   write-stall via tiny write_buffer; `stats()` reflects it. Absent
   test: ops can't see compaction-IO storms.

### Integration

1. **Crash-recovery: no torn writes.** During a high-rate ingest (100k
   `append`s/sec for 5s), SIGKILL the process; on restart, verify the
   last successfully-committed batch is fully visible and no partial
   trajectory exists in `traj` without its `by_id` mate. Verifies the
   WAL + atomic-batch invariant.
2. **RocksDB WAL stays under configured cap.** With a 1 GiB WAL cap and a
   write rate that would otherwise overflow, `stats()` shows WAL size
   bounded; oldest WAL files recycle after flush. Verifies tuning.
3. **range_size_bytes matches `du -s`.** After 10 GiB of writes,
   `range_size_bytes()` reports within 5% of `du -s hot/rocksdb`.
   Verifies the metric Lifecycle relies on.

### Smoke (mandatory)

- Service starts cleanly when `data/experience-store/hot/rocksdb/` is empty
  (first boot). RocksDB column families `traj`, `by_id`, `step_idx`, and
  `priority` (Q3-ADR-010, PriorityIndex-owned) are created on first open.
- `/health` returns 200 even during a long compaction (compaction runs in
  RocksDB-internal background threads).
- `/metrics` includes `sts2_q3_hot_bytes` after first append.
