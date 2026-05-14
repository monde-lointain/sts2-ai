"""Retention policy thresholds and sustained-pressure predicate.

Phase-1 thresholds per Q3-ADR-007 (50 GiB high-water, 100 GiB overflow).
Sustained-pressure predicate per Q3-ADR-008 (dual time-windowed: hot_bytes
above high-water for >=60 s OR queue_depth above 0.8 capacity for >=30 s).

See pipeline/experience-store/docs/specs/modules/control-plane.md
(RetentionController section).
"""

from __future__ import annotations

import json
import pathlib
from typing import Iterable

POLICY_DIR = "retention"
POLICY_FILE = "policy.json"

# Q3-ADR-007 defaults.
DEFAULT_HOT_HIGH_WATER_BYTES = 50 * 1024**3
DEFAULT_HOT_OVERFLOW_BYTES = 100 * 1024**3
DEFAULT_INGEST_QUEUE_CAPACITY = 10000

# Q3-ADR-008 windows.
HOT_BYTES_WINDOW_SECONDS = 60.0
QUEUE_DEPTH_WINDOW_SECONDS = 30.0
QUEUE_DEPTH_THRESHOLD_FRACTION = 0.8


class RetentionPolicy:
    """Persisted policy + pure sustained-pressure predicate.

    Persists thresholds to <data_dir>/retention/policy.json on first init.
    Subsequent inits keep the on-disk values (operator override is the
    single source of truth once the file exists).
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
            self._persist()

    def _persist(self) -> None:
        payload = {
            "hot_high_water_bytes": self._high_water,
            "hot_overflow_bytes": self._overflow,
            "ingest_queue_capacity": self._capacity,
            "hot_bytes_window_seconds": HOT_BYTES_WINDOW_SECONDS,
            "queue_depth_window_seconds": QUEUE_DEPTH_WINDOW_SECONDS,
            "queue_depth_threshold_fraction": QUEUE_DEPTH_THRESHOLD_FRACTION,
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

    def sustained_pressure(
        self,
        hot_bytes: int,
        queue_depth: int,
        now_ts: float,
        history: Iterable[tuple[float, int, int]],
    ) -> bool:
        """Return True iff Q3-ADR-008 dual condition fires.

        Conditions (either is sufficient):
        - hot_bytes > high_water for >=60 s continuously
        - queue_depth > 0.8 * capacity for >=30 s continuously

        `history` is an iterable of (ts, hot_bytes, queue_depth) samples
        the caller maintains (Lifecycle ring buffer). The current
        observation (now_ts, hot_bytes, queue_depth) is treated as the
        most recent sample. Continuity is checked walking back from
        the present: as soon as the predicate fails on a sample, the
        run length resets to 0 for that branch.
        """
        queue_threshold = self._capacity * QUEUE_DEPTH_THRESHOLD_FRACTION

        # Stitch the current sample to the prior history, oldest-first.
        prior = sorted(history, key=lambda row: row[0])
        samples: list[tuple[float, int, int]] = list(prior)
        samples.append((float(now_ts), int(hot_bytes), int(queue_depth)))

        # Walk back from the present; stop counting as soon as the
        # predicate fails. The covered window is now_ts - first-failing-ts.
        hot_window_ok = False
        queue_window_ok = False
        latest_ts = samples[-1][0]

        run_start_hot = latest_ts
        run_start_queue = latest_ts
        for ts, hb, qd in reversed(samples):
            if hb > self._high_water:
                run_start_hot = ts
            else:
                break
        for ts, hb, qd in reversed(samples):
            if qd > queue_threshold:
                run_start_queue = ts
            else:
                break

        if (latest_ts - run_start_hot) >= HOT_BYTES_WINDOW_SECONDS and samples[-1][1] > self._high_water:
            hot_window_ok = True
        if (latest_ts - run_start_queue) >= QUEUE_DEPTH_WINDOW_SECONDS and samples[-1][2] > queue_threshold:
            queue_window_ok = True

        return hot_window_ok or queue_window_ok
