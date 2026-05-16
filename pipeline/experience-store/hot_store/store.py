"""HotStore: single-shard RocksDB-backed append-only trajectory store.

Implements `pipeline/experience-store/docs/specs/modules/hot-store.md` for
S0 Phase 1. Column families per Q3-ADR-002 + Q3-ADR-010:
- `traj`: trajectory bytes keyed by (ingest_ts_ns u64be, trajectory_id b16).
- `by_id`: trajectory_id b16 -> u64be ingest_ts_ns.
- `step_idx`: declared empty, Phase-3 activation.
- `priority`: declared empty, PriorityIndex-owned per Q3-ADR-010.

The HotStore submodule code MUST NOT read or write the `priority` CF —
the CF is created here only because Rocks requires CFs to exist at open
time. PriorityIndex (separate submodule, Phase-2+) owns its keyspace.

Binding choice — rocksdict 0.3.29 (MIT, last release 2025-12-01):
- Actively maintained (vs python-rocksdb which the upstream owner
  archived 2025-06-27); wraps the official RocksDB C++ library via
  Rust bindings.
- Supports raw-bytes mode (`Options(raw_mode=True)`), explicit column
  families, WriteBatch atomicity across CFs, range iteration, range
  delete, and RocksDB property queries — all the primitives Q3-ADR-002
  and the hot-store spec require.
- pure-wheel install on cp312-manylinux: no system RocksDB build step.

Spec/prompt API surface notes:
- Canonical API per `modules/hot-store.md`:
  `append(traj_bytes, trajectory_id) -> ingest_ts_ns`, `read(id)`,
  `scan(after_ts_ns, limit)`, `delete_range(until_ts_ns) -> count`,
  `range_size_bytes() -> int`. Implemented.
- W3-facing convenience helpers added for IngestAPI/Sampler/Lifecycle
  ergonomics: `append_new(traj_bytes) -> trajectory_id`,
  `sample_uniform(n, seed)`, `count()`, `size_bytes()` (alias of
  range_size_bytes), `drop_oldest_age_first(target_bytes) -> count`.
  These do not violate the spec; spec methods remain canonical.
- trajectory_id is `bytes16 random` per spec section "Data Ownership" —
  16 raw bytes. The prompt suggested "uuid4 hex"; the spec wins.
"""

from __future__ import annotations

import contextlib
import os
import random
import threading
import time
from collections.abc import Iterator
from pathlib import Path

from rocksdict import Options, Rdict, WriteBatch

from .keys import (
    TRAJ_ID_LEN,
    decode_traj_key,
    decode_ts,
    encode_traj_key,
    encode_ts,
    traj_range_hi,
    traj_range_lo,
)

_CF_TRAJ = "traj"
_CF_BY_ID = "by_id"
_CF_STEP_IDX = "step_idx"
_CF_PRIORITY = "priority"

# All four CFs always created at open time (Q3-ADR-002, Q3-ADR-010).
_ALL_CFS: tuple[str, ...] = (_CF_TRAJ, _CF_BY_ID, _CF_STEP_IDX, _CF_PRIORITY)


class HotStore:
    """Single-shard RocksDB trajectory store, in-process owner of one DB handle.

    Thread-safe for concurrent `append`. Reads (scan/read) hold no global
    lock — RocksDB itself is multi-reader / single-writer-friendly. The
    monotonic-ts clock is protected by an internal mutex.
    """

    def __init__(self, db_dir: str | os.PathLike) -> None:
        self._db_dir = Path(db_dir)
        self._db_dir.mkdir(parents=True, exist_ok=True)

        opts = Options(raw_mode=True)
        opts.create_if_missing(True)
        opts.create_missing_column_families(True)
        # Modest defaults; Q3-ADR-007 sizing is a Lifecycle concern, not
        # something HotStore tunes at open time.
        opts.increase_parallelism(max(2, (os.cpu_count() or 4) // 2))

        cf_opts = {name: Options(raw_mode=True) for name in _ALL_CFS}

        self._db = Rdict(str(self._db_dir), opts, column_families=cf_opts)
        self._cf_traj = self._db.get_column_family(_CF_TRAJ)
        self._cf_by_id = self._db.get_column_family(_CF_BY_ID)
        # step_idx + priority CFs exist but HotStore never writes/reads
        # them. We don't hold long-lived handles to discourage misuse.

        self._traj_handle = self._db.get_column_family_handle(_CF_TRAJ)
        self._by_id_handle = self._db.get_column_family_handle(_CF_BY_ID)

        # Monotonic ts: max(time.time_ns(), last_assigned + 1).
        self._ts_lock = threading.Lock()
        self._last_ts_ns = 0

    # ---------- spec-canonical API (modules/hot-store.md §Communication) ----------

    def append(self, traj_bytes: bytes, trajectory_id: bytes) -> int:
        """Atomically write `traj_bytes` and update `by_id`. Returns assigned ts."""
        if not isinstance(traj_bytes, (bytes, bytearray)):
            raise TypeError(f"traj_bytes must be bytes; got {type(traj_bytes).__name__}")
        if not isinstance(trajectory_id, (bytes, bytearray)) or len(trajectory_id) != TRAJ_ID_LEN:
            raise ValueError(
                f"trajectory_id must be {TRAJ_ID_LEN} bytes; got len "
                f"{len(trajectory_id) if hasattr(trajectory_id, '__len__') else 'N/A'}"
            )
        ingest_ts_ns = self._next_ts_ns()
        wb = WriteBatch(raw_mode=True)
        wb.put(
            encode_traj_key(ingest_ts_ns, bytes(trajectory_id)),
            bytes(traj_bytes),
            column_family=self._traj_handle,
        )
        wb.put(
            bytes(trajectory_id),
            encode_ts(ingest_ts_ns),
            column_family=self._by_id_handle,
        )
        self._db.write(wb)
        return ingest_ts_ns

    def read(self, trajectory_id: bytes) -> bytes | None:
        """Point read via `by_id` -> `traj`. None if not found."""
        if not isinstance(trajectory_id, (bytes, bytearray)) or len(trajectory_id) != TRAJ_ID_LEN:
            return None
        ts_blob = self._cf_by_id.get(bytes(trajectory_id))
        if ts_blob is None:
            return None
        try:
            ts_ns = decode_ts(ts_blob)
        except ValueError:
            return None
        return self._cf_traj.get(encode_traj_key(ts_ns, bytes(trajectory_id)))

    def scan(self, after_ts_ns: int, limit: int) -> Iterator[tuple[int, bytes, bytes]]:
        """Range-scan `traj` strictly after `after_ts_ns`, capped at `limit`.

        Yields (ingest_ts_ns, trajectory_id, traj_bytes) tuples.
        """
        if limit <= 0:
            return
        # Strictly-after: seek to the smallest key with ts > after_ts_ns.
        # Using ts=after_ts_ns+1 lower bound; if after_ts_ns is uint64 max,
        # the seek simply finds no keys.
        if after_ts_ns >= (1 << 64) - 1:
            return
        seek_key = traj_range_lo(after_ts_ns + 1)
        it = self._cf_traj.iter()
        it.seek(seek_key)
        yielded = 0
        while it.valid() and yielded < limit:
            k = it.key()
            v = it.value()
            ts_ns, tid = decode_traj_key(k)
            yield ts_ns, tid, v
            yielded += 1
            it.next()

    def delete_range(self, until_ts_ns: int) -> int:
        """Delete all `traj`+`by_id` rows with ingest_ts_ns < until_ts_ns. Returns count."""
        if until_ts_ns <= 0:
            return 0
        # Collect victim (ts, id) pairs first so we can clean by_id.
        victims: list[tuple[int, bytes]] = []
        it = self._cf_traj.iter()
        it.seek_to_first()
        hi = traj_range_hi(until_ts_ns)
        while it.valid():
            k = it.key()
            if k >= hi:
                break
            ts_ns, tid = decode_traj_key(k)
            victims.append((ts_ns, tid))
            it.next()
        if not victims:
            return 0
        # Atomic delete across both CFs.
        wb = WriteBatch(raw_mode=True)
        wb.delete_range(traj_range_lo(0), hi, column_family=self._traj_handle)
        for _, tid in victims:
            wb.delete(tid, column_family=self._by_id_handle)
        self._db.write(wb)
        return len(victims)

    def range_size_bytes(self) -> int:
        """Approximate on-disk + memtable bytes for the `traj` CF."""
        # Flush memtable to SST so size properties stabilize; cheap when
        # there's nothing to flush.
        self._cf_traj.flush(wait=True)
        sst = self._cf_traj.property_int_value("rocksdb.total-sst-files-size") or 0
        memtable = self._cf_traj.property_int_value("rocksdb.cur-size-all-mem-tables") or 0
        return int(sst) + int(memtable)

    def compact_range(self, start_ts_ns: int | None = None, end_ts_ns: int | None = None) -> None:
        """Trigger explicit compaction of the `traj` CF (subrange optional)."""
        if start_ts_ns is None and end_ts_ns is None:
            self._cf_traj.compact_range(None, None)
        else:
            lo = traj_range_lo(start_ts_ns or 0)
            hi = traj_range_hi(end_ts_ns) if end_ts_ns is not None else None
            self._cf_traj.compact_range(lo, hi)

    def stats(self) -> dict[str, int]:
        """RocksDB stats for ObservabilityAdapter."""
        self._cf_traj.flush(wait=True)
        props = (
            "rocksdb.total-sst-files-size",
            "rocksdb.live-sst-files-size",
            "rocksdb.estimate-num-keys",
            "rocksdb.cur-size-all-mem-tables",
            "rocksdb.size-all-mem-tables",
            "rocksdb.compaction-pending",
            "rocksdb.num-running-compactions",
            "rocksdb.actual-delayed-write-rate",
            "rocksdb.is-write-stopped",
        )
        out: dict[str, int] = {}
        for p in props:
            try:
                val = self._cf_traj.property_int_value(p)
            except Exception:
                val = 0
            out[p] = int(val or 0)
        return out

    # ---------- W3-facing convenience methods (S0.B.α prompt) ----------

    def append_new(self, traj_bytes: bytes) -> bytes:
        """Append with an auto-generated 16-byte trajectory_id. Returns the id.

        ID is 16 cryptographically-random bytes; bytes per spec, not hex.
        IngestAPI may hex-encode at HTTP boundary if desired.
        """
        trajectory_id = os.urandom(TRAJ_ID_LEN)
        self.append(traj_bytes, trajectory_id)
        return trajectory_id

    def size_bytes(self) -> int:
        """Alias of `range_size_bytes` for prompt-named callers."""
        return self.range_size_bytes()

    def count(self) -> int:
        """Exact count of rows in `traj` (iterates; suitable for Phase-1 volumes).

        RocksDB's `estimate-num-keys` is approximate and includes tombstones
        until compaction. For S0 verification correctness we walk the CF.
        """
        n = 0
        it = self._cf_traj.iter()
        it.seek_to_first()
        while it.valid():
            n += 1
            it.next()
        return n

    def sample_uniform(self, n: int, seed: int | None = None) -> list[bytes]:
        """Uniform-without-replacement random sample of `n` trajectory values.

        Determinism: for a fixed `seed` AND fixed store state (same set of
        keys), repeated calls return the same list. State mutation (append /
        drop) changes the eligible-key set and may change the result.

        Implementation: collect all keys (Phase-1 volumes only — order 10⁶),
        sort for canonical order, then `random.Random(seed).sample(...)`.
        """
        if n < 0:
            raise ValueError(f"n must be >= 0; got {n}")
        if n == 0:
            return []
        keys: list[bytes] = []
        it = self._cf_traj.iter()
        it.seek_to_first()
        while it.valid():
            keys.append(it.key())
            it.next()
        if not keys:
            return []
        keys.sort()  # iter() should already be sorted but be explicit
        rng = random.Random(seed)
        k_pick = min(n, len(keys))
        chosen = rng.sample(keys, k_pick)
        return [self._cf_traj[k] for k in chosen]  # pyright: ignore[reportReturnType]  # keys from iterator above

    def drop_oldest_age_first(self, target_bytes: int) -> int:
        """Drop oldest-ts rows until at least `target_bytes` freed. Returns count dropped.

        Walks `traj` in ascending ts order accumulating value sizes; when
        cumulative size >= `target_bytes`, snapshots the cutoff ts and
        deletes [0, cutoff_ts+1) atomically across `traj` and `by_id`.
        """
        if target_bytes <= 0:
            return 0
        cumulative = 0
        victims: list[tuple[int, bytes]] = []
        it = self._cf_traj.iter()
        it.seek_to_first()
        while it.valid():
            k = it.key()
            v = it.value()
            ts_ns, tid = decode_traj_key(k)
            victims.append((ts_ns, tid))
            cumulative += len(v)
            if cumulative >= target_bytes:
                it.next()
                break
            it.next()
        if not victims:
            return 0
        # cutoff = ts of last victim + 1 (exclusive upper bound).
        cutoff = victims[-1][0] + 1
        wb = WriteBatch(raw_mode=True)
        wb.delete_range(
            traj_range_lo(0),
            traj_range_lo(cutoff),
            column_family=self._traj_handle,
        )
        for _, tid in victims:
            wb.delete(tid, column_family=self._by_id_handle)
        self._db.write(wb)
        return len(victims)

    # ---------- lifecycle ----------

    def flush(self) -> None:
        """Force memtable flush on all CFs (use on shutdown / before snapshot)."""
        self._cf_traj.flush(wait=True)
        self._cf_by_id.flush(wait=True)
        self._db.flush(wait=True)

    def close(self) -> None:
        """Close the underlying DB handle.

        Drops the CF handles first; rocksdict CF handles hold references
        to the parent DB and otherwise keep the file lock alive even after
        the user calls close().
        """
        with contextlib.suppress(Exception):
            self.flush()
        # Drop CF Rdict references; the parent DB's close releases the lock.
        with contextlib.suppress(Exception):
            self._cf_traj.close()
        with contextlib.suppress(Exception):
            self._cf_by_id.close()
        self._cf_traj = None  # type: ignore[assignment]
        self._cf_by_id = None  # type: ignore[assignment]
        self._traj_handle = None  # type: ignore[assignment]
        self._by_id_handle = None  # type: ignore[assignment]
        self._db.close()

    def __enter__(self) -> HotStore:
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.close()

    # ---------- internals ----------

    def _next_ts_ns(self) -> int:
        """Strictly-monotonic ns timestamp across concurrent callers."""
        with self._ts_lock:
            now = time.time_ns()
            if now <= self._last_ts_ns:
                now = self._last_ts_ns + 1
            self._last_ts_ns = now
            return now
