"""Retention controller: thresholds + pressure classification.

Phase-1 thresholds per Q3-ADR-007 (50 GiB high-water, 100 GiB overflow).
Pressure classification per spec lines 98-100 produces one of four states
(Normal / HighWater / Overflow / Sustained); Sustained uses Q3-ADR-008's
dual time-windowed predicate (hot_bytes above high-water for >=60 s OR
queue_depth above 0.8 capacity for >=30 s).

Sustained-window samples are kept in an internal ring buffer (~64 entries)
of `(ts_seconds, hot_bytes, queue_depth)` tuples timestamped via
`time.monotonic()`. See modules/control-plane.md (RetentionController
section, lines 48-58).

The class is named `RetentionController` (spec name). `RetentionPolicy`
is retained as a backward-compat alias for one wave so existing W2
callers keep building.
"""

from __future__ import annotations

import collections
import enum
import json
import pathlib
import threading
import time
from typing import Iterable

POLICY_DIR = "retention"
POLICY_FILE = "policy.json"

# Q3-ADR-007 defaults.
DEFAULT_HOT_HIGH_WATER_BYTES = 50 * 1024**3
DEFAULT_HOT_OVERFLOW_BYTES = 100 * 1024**3
DEFAULT_INGEST_QUEUE_CAPACITY = 10000

# Q3-ADR-008 windows (also persisted to policy.json so operators can tune).
DEFAULT_HOT_BYTES_WINDOW_SECONDS = 60.0
DEFAULT_QUEUE_DEPTH_WINDOW_SECONDS = 30.0
DEFAULT_QUEUE_DEPTH_THRESHOLD_FRACTION = 0.8

# Ring buffer size per spec line 58.
_RING_BUFFER_SIZE = 64


class Pressure(enum.Enum):
    """Pressure classification per spec line 98-100.

    The four states are mutually exclusive; precedence on tie is
    OVERFLOW > SUSTAINED > HIGH_WATER > NORMAL.
    """

    NORMAL = "normal"
    HIGH_WATER = "high_water"
    OVERFLOW = "overflow"
    SUSTAINED = "sustained"


class RetentionController:
    """Persisted thresholds + windowed pressure classification.

    Persists thresholds to <data_dir>/retention/policy.json on first init.
    Subsequent inits keep the on-disk values (operator override is the
    single source of truth once the file exists).

    Maintains an internal ring buffer (deque, maxlen=64) of recent
    `(ts_monotonic_seconds, hot_bytes, queue_depth)` samples for the
    sustained-window check. `classify_pressure(...)` appends the current
    sample and then evaluates all four predicates.
    """

    def __init__(self, config: dict, data_dir: pathlib.Path) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._policy_path = self._data_dir / POLICY_DIR / POLICY_FILE
        self._policy_path.parent.mkdir(parents=True, exist_ok=True)

        if self._policy_path.exists():
            with self._policy_path.open("r", encoding="utf-8") as handle:
                persisted = json.load(handle)
            self._high_water = int(persisted["hot_high_water_bytes"])
            self._overflow = int(persisted["hot_overflow_bytes"])
            self._capacity = int(persisted["ingest_queue_capacity"])
            self._hot_window_s = float(
                persisted.get(
                    "hot_bytes_window_seconds", DEFAULT_HOT_BYTES_WINDOW_SECONDS
                )
            )
            self._queue_window_s = float(
                persisted.get(
                    "queue_depth_window_seconds",
                    DEFAULT_QUEUE_DEPTH_WINDOW_SECONDS,
                )
            )
            self._queue_threshold_frac = float(
                persisted.get(
                    "queue_depth_threshold_fraction",
                    DEFAULT_QUEUE_DEPTH_THRESHOLD_FRACTION,
                )
            )
        else:
            self._high_water = int(
                config.get("hot_high_water_bytes", DEFAULT_HOT_HIGH_WATER_BYTES)
            )
            self._overflow = int(
                config.get("hot_overflow_bytes", DEFAULT_HOT_OVERFLOW_BYTES)
            )
            self._capacity = int(
                config.get("ingest_queue_capacity", DEFAULT_INGEST_QUEUE_CAPACITY)
            )
            self._hot_window_s = float(
                config.get(
                    "hot_bytes_window_seconds", DEFAULT_HOT_BYTES_WINDOW_SECONDS
                )
            )
            self._queue_window_s = float(
                config.get(
                    "queue_depth_window_seconds",
                    DEFAULT_QUEUE_DEPTH_WINDOW_SECONDS,
                )
            )
            self._queue_threshold_frac = float(
                config.get(
                    "queue_depth_threshold_fraction",
                    DEFAULT_QUEUE_DEPTH_THRESHOLD_FRACTION,
                )
            )
            self._persist()

        # Ring buffer of recent samples for the sustained-window predicate
        # (spec lines 53-58). Entries are (ts_monotonic_seconds, hot_bytes,
        # queue_depth) tuples; maxlen evicts oldest automatically.
        self._samples: collections.deque[tuple[float, int, int]] = collections.deque(
            maxlen=_RING_BUFFER_SIZE
        )
        self._samples_lock = threading.Lock()

    def _persist(self) -> None:
        payload = {
            "hot_high_water_bytes": self._high_water,
            "hot_overflow_bytes": self._overflow,
            "ingest_queue_capacity": self._capacity,
            "hot_bytes_window_seconds": self._hot_window_s,
            "queue_depth_window_seconds": self._queue_window_s,
            "queue_depth_threshold_fraction": self._queue_threshold_frac,
        }
        tmp = self._policy_path.with_suffix(".json.tmp")
        with tmp.open("w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2, sort_keys=True)
            handle.write("\n")
        tmp.replace(self._policy_path)

    @property
    def policy_path(self) -> pathlib.Path:
        return self._policy_path

    def high_water_bytes(self) -> int:
        return self._high_water

    def overflow_bytes(self) -> int:
        return self._overflow

    def ingest_queue_capacity(self) -> int:
        return self._capacity

    def hot_bytes_window_seconds(self) -> float:
        return self._hot_window_s

    def queue_depth_window_seconds(self) -> float:
        return self._queue_window_s

    def queue_depth_threshold_fraction(self) -> float:
        return self._queue_threshold_frac

    def classify_pressure(
        self, hot_bytes: int, queue_depth: int, queue_capacity: int
    ) -> Pressure:
        now = time.monotonic()
        with self._samples_lock:
            self._samples.append((now, int(hot_bytes), int(queue_depth)))
        if hot_bytes > self._overflow:
            return Pressure.OVERFLOW
        if self._sustained_fires(int(queue_capacity)):
            return Pressure.SUSTAINED
        queue_threshold = int(queue_capacity) * self._queue_threshold_frac
        if hot_bytes > self._high_water:
            return Pressure.HIGH_WATER
        if queue_depth >= queue_threshold:
            return Pressure.HIGH_WATER
        return Pressure.NORMAL

    def _sustained_fires(self, queue_capacity: int) -> bool:
        with self._samples_lock:
            if not self._samples:
                return False
            samples = list(self._samples)
        queue_threshold = float(queue_capacity) * self._queue_threshold_frac
        latest_ts, latest_hb, latest_qd = samples[-1]
        hot_fires = False
        if latest_hb > self._high_water:
            run_start_hot = latest_ts
            for ts, hb, _qd in reversed(samples):
                if hb > self._high_water:
                    run_start_hot = ts
                else:
                    break
            if (latest_ts - run_start_hot) >= self._hot_window_s:
                hot_fires = True
        queue_fires = False
        if latest_qd > queue_threshold:
            run_start_queue = latest_ts
            for ts, _hb, qd in reversed(samples):
                if qd > queue_threshold:
                    run_start_queue = ts
                else:
                    break
            if (latest_ts - run_start_queue) >= self._queue_window_s:
                queue_fires = True
        return hot_fires or queue_fires

    def sustained_pressure(
        self,
        hot_bytes: int,
        queue_depth: int,
        now_ts: float,
        history: Iterable[tuple[float, int, int]],
    ) -> bool:
        """Backward-compat shim: returns True iff classify_pressure -> SUSTAINED.

        Existing callers (and pre-spec-alignment tests) pass an external
        `history` iterable plus an explicit `now_ts`. This shim swaps the
        instance ring buffer for the caller-supplied series, evaluates
        the dual-window predicate, and restores the original buffer so
        subsequent `classify_pressure(...)` calls remain consistent.

        New callers should use `classify_pressure(...)` directly and let
        the controller manage its own ring buffer via `time.monotonic()`.
        """
        # Build a transient sample list (oldest-first) ending in the
        # current observation; load it into the ring buffer for the
        # sustained-only check, then restore.
        prior = sorted(history, key=lambda row: row[0])
        synthetic: list[tuple[float, int, int]] = [
            (float(ts), int(hb), int(qd)) for ts, hb, qd in prior
        ]
        synthetic.append((float(now_ts), int(hot_bytes), int(queue_depth)))

        with self._samples_lock:
            saved = list(self._samples)
            self._samples.clear()
            for sample in synthetic[-_RING_BUFFER_SIZE:]:
                self._samples.append(sample)
        try:
            return self._sustained_fires(self._capacity)
        finally:
            with self._samples_lock:
                self._samples.clear()
                for sample in saved[-_RING_BUFFER_SIZE:]:
                    self._samples.append(sample)


# Backward-compat alias for one wave. W3 dispatches will switch to
# RetentionController; this name is removed after W3 close.
RetentionPolicy = RetentionController
