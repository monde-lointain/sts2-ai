"""Cursor LRU cache for resumable sample requests (Phase-1A).

Per `modules/sampler.md` lines 20-22 + 35-37: transient
`{cursor_id: str -> CursorState(mode, filters, position_ts_ns,
served_count, last_touch_ts_ns)}`. Bounded (default 1024); idle timeout
default 300 s. Eviction is lazy (on access) — a background scan thread
is Phase-2+ work and is not strictly required by the spec.

The class is thread-safe: every public method takes a single internal
lock around the OrderedDict mutation. Phase-1A request rate is low
enough that this single-lock model has no contention concern; if
profiling Phase-2+ shows otherwise, swap for a striped lock.
"""

from __future__ import annotations

import threading
import time
import uuid
from collections import OrderedDict
from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any


@dataclass
class CursorState:
    """Resumable sample-cursor state.

    Fields:
    - mode: sampling mode token (Phase-1A: always "uniform").
    - filters: decoded request filters, preserved verbatim across resumes
      so subsequent calls don't have to repeat them.
    - position_ts_ns: last fully-drained `ingest_ts_ns` from HotStore.scan;
      resume uses this as `after_ts_ns` (strictly-after; matches
      HotStore.scan semantics). When a trajectory is only partially
      consumed (batch boundary lands mid-trajectory), position_ts_ns
      stays at the prior-fully-drained ts and `step_offset` advances
      so the resume picks up the unsent tail.
    - step_offset: number of leading steps to skip in the next trajectory
      at `position_ts_ns + 1`'s scan boundary. Zero means "no partial
      consumption pending."
    - served_count: total `TrajectoryStep` rows yielded so far.
    - last_touch_ts_ns: ns-precision timestamp of most recent access
      (used for idle eviction).
    """

    mode: str
    filters: dict[str, Any] = field(default_factory=dict)
    position_ts_ns: int = 0
    step_offset: int = 0
    served_count: int = 0
    last_touch_ts_ns: int = 0

    def snapshot(self) -> dict[str, Any]:
        """Diagnostic shape for `GET /sample/cursor/<id>`."""
        return {
            "mode": self.mode,
            "filters": dict(self.filters),
            "position_ts_ns": int(self.position_ts_ns),
            "step_offset": int(self.step_offset),
            "served_count": int(self.served_count),
            "last_touch_ts_ns": int(self.last_touch_ts_ns),
        }


def _default_now_ns() -> int:
    return time.time_ns()


class CursorCache:
    """Bounded-LRU + idle-timeout cursor store.

    Capacity defaults to 1024 (Phase-1 cap per spec). Idle timeout
    defaults to 300 seconds. Both are configurable for tests and for
    Phase-2+ tuning.

    The optional `now_ns` injection (defaults to `time.time_ns`) lets
    tests fast-forward time without sleeping or monkey-patching the
    stdlib. RAII-equivalent for the lock is via the `with` context
    manager (no manual release path).
    """

    def __init__(
        self,
        capacity: int = 1024,
        idle_timeout_seconds: int = 300,
        now_ns: Callable[[], int] = _default_now_ns,
    ) -> None:
        if capacity < 1:
            raise ValueError(f"capacity must be >= 1; got {capacity}")
        if idle_timeout_seconds < 1:
            raise ValueError(f"idle_timeout_seconds must be >= 1; got {idle_timeout_seconds}")
        self._capacity = capacity
        self._idle_ns = int(idle_timeout_seconds) * 1_000_000_000
        self._now_ns = now_ns
        self._lock = threading.Lock()
        self._items: OrderedDict[str, CursorState] = OrderedDict()

    # ---------- public API ----------

    def create(self, state: CursorState) -> str:
        """Insert a new cursor; return its 32-char hex id."""
        with self._lock:
            self._evict_idle_locked()
            cursor_id = uuid.uuid4().hex
            # collision is statistically impossible but defend anyway
            while cursor_id in self._items:
                cursor_id = uuid.uuid4().hex
            state.last_touch_ts_ns = self._now_ns()
            self._items[cursor_id] = state
            self._evict_overflow_locked()
            return cursor_id

    def get(self, cursor_id: str) -> CursorState | None:
        """Return state if present and not expired; touch LRU order."""
        with self._lock:
            self._evict_idle_locked()
            state = self._items.get(cursor_id)
            if state is None:
                return None
            # mark as most-recently-used + refresh idle clock
            self._items.move_to_end(cursor_id)
            state.last_touch_ts_ns = self._now_ns()
            return state

    def update(self, cursor_id: str, state: CursorState) -> None:
        """Replace state for an existing cursor (no-op if missing).

        Used by the Sampler after serving a page: position_ts_ns and
        served_count advance.
        """
        with self._lock:
            if cursor_id not in self._items:
                return
            state.last_touch_ts_ns = self._now_ns()
            self._items[cursor_id] = state
            self._items.move_to_end(cursor_id)

    def delete(self, cursor_id: str) -> bool:
        """Remove `cursor_id`; return True iff it was present."""
        with self._lock:
            return self._items.pop(cursor_id, None) is not None

    def __len__(self) -> int:
        with self._lock:
            return len(self._items)

    def count(self) -> int:
        """Public alias of `len(self)` for metrics callers."""
        return len(self)

    # ---------- internals ----------

    def _evict_overflow_locked(self) -> None:
        """Drop LRU entries until size <= capacity. Caller holds lock."""
        while len(self._items) > self._capacity:
            self._items.popitem(last=False)

    def _evict_idle_locked(self) -> None:
        """Drop entries whose last_touch_ts_ns is older than idle_ns.

        Lazy: runs on every public-API entry. Cheap because OrderedDict
        iteration is O(items_examined) — once we hit a non-expired entry
        we'd ideally break, but `last_touch_ts_ns` is not monotonic in
        insertion order under `move_to_end` (a recent `get` reorders).
        Phase-1A scans linearly; Phase-2+ may add a secondary
        idle-ordered index.
        """
        if not self._items:
            return
        now = self._now_ns()
        cutoff = now - self._idle_ns
        expired: list[str] = []
        for cid, state in self._items.items():
            if state.last_touch_ts_ns < cutoff:
                expired.append(cid)
        for cid in expired:
            self._items.pop(cid, None)
