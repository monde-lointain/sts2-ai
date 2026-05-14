"""Lifecycle: tick loop + classify_pressure-driven age-drop (Phase-1A).

Implements ``pipeline/experience-store/docs/specs/modules/lifecycle.md``.
Phase-1A scope is age-only drop on ``Overflow`` / ``Sustained`` pressure;
``HighWater`` is a no-op (Phase-2+ would promote here). Cold-tier
promotion is disabled (``policy.cold_tier_enabled = False``); the
``force_promote`` operator hook raises ``NotImplementedError`` until
Phase-2+ wires the cold store.

The submodule owns three on-disk files under ``<data_dir>/lifecycle/``:
``policy.json``, ``cursor.json``, ``audit.ndjson`` (+ rotated peers).
"""

from __future__ import annotations

import dataclasses
import json
import pathlib
import threading
import time
from typing import Any, Callable

from .audit import AuditLog
from .policy import LifecyclePolicy
from .thread_manager import LifecycleThreadManager

CURSOR_FILE = "cursor.json"

# Drop-sizing constants (spec line 71-74; "drop oldest 10% of overflow
# margin or 1 GiB whichever larger" per prompt).
_DROP_FRACTION_OF_OVERFLOW_MARGIN = 0.10
_MIN_DROP_BYTES = 1 * 1024**3  # 1 GiB


@dataclasses.dataclass(frozen=True)
class TickResult:
    """Outcome of a single tick. Returned by ``tick()`` and operator endpoints."""

    pressure: str  # "normal" | "high_water" | "overflow" | "sustained"
    action: str  # "noop" | "drop"
    reason: str | None  # "overflow" | "sustained_pressure" | None
    hot_bytes: int
    queue_depth: int
    rows_dropped: int
    until_ts_ns: int
    tick_ts_ns: int

    def to_dict(self) -> dict[str, Any]:
        return dataclasses.asdict(self)


class Lifecycle:
    """Age-drop tiering engine; Phase-1A cold-disabled.

    Construction is idempotent: existing ``policy.json`` / ``cursor.json`` /
    ``audit.ndjson`` files under ``<data_dir>/lifecycle/`` are loaded, not
    overwritten.

    ``queue_depth_provider`` is a zero-arg callable that returns the
    current ingest queue depth (W4 wires this to IngestAPI). The provider
    is consulted once per tick so the Lifecycle has no static coupling
    to IngestAPI.
    """

    def __init__(
        self,
        hot_store: Any,
        retention_controller: Any,
        queue_depth_provider: Callable[[], int],
        queue_capacity: int,
        data_dir: pathlib.Path,
    ) -> None:
        self._hot_store = hot_store
        self._retention = retention_controller
        if not callable(queue_depth_provider):
            raise TypeError("queue_depth_provider must be callable")
        self._queue_depth_provider = queue_depth_provider
        if int(queue_capacity) <= 0:
            raise ValueError(f"queue_capacity must be > 0; got {queue_capacity}")
        self._queue_capacity = int(queue_capacity)

        self._data_dir = pathlib.Path(data_dir)
        self._lifecycle_dir = self._data_dir / "lifecycle"
        self._lifecycle_dir.mkdir(parents=True, exist_ok=True)

        self._policy = LifecyclePolicy(self._data_dir)
        self._audit = AuditLog(self._data_dir)
        self._cursor_path = self._lifecycle_dir / CURSOR_FILE

        # Lock must precede `_load_or_init_cursor()` because that path may
        # call `_write_cursor`, which now acquires `self._lock`.
        self._lock = threading.Lock()
        self._cursor = self._load_or_init_cursor()

        # Metric backing state.
        self._dropped_total: dict[str, int] = {
            "overflow": 0,
            "sustained_pressure": 0,
            "cold_unavailable": 0,
        }
        self._promoted_total = 0  # Phase-2+; stays 0 here.
        self._pressure_state = "normal"
        self._last_tick_state: TickResult | None = None
        self._tick_seconds_total = 0.0

        # Background thread mechanics live in a dedicated manager so the
        # tick logic above stays unit-testable without a real thread.
        self._thread_manager = LifecycleThreadManager(
            tick_fn=self.tick,
            interval_fn=self._policy.tick_interval_seconds,
            on_error=self._audit_tick_error,
        )

    # ------------------------------------------------------------------
    # Public tick API
    # ------------------------------------------------------------------

    def tick(self) -> TickResult:
        """One iteration of the lifecycle loop. Spec lines 69-80.

        1. Read ``hot_bytes`` from HotStore.
        2. Read queue depth from provider.
        3. ``classify_pressure(hot_bytes, queue_depth, queue_capacity)``.
        4. Branch on pressure; ``OVERFLOW``/``SUSTAINED`` call
           ``_drop_oldest_range``.
        5. Persist cursor, append audit record, return ``TickResult``.
        """
        tick_started_monotonic = time.monotonic()
        tick_ts_ns = time.time_ns()

        hot_bytes = int(self._hot_store.range_size_bytes())
        queue_depth = int(self._queue_depth_provider())
        pressure = self._retention.classify_pressure(
            hot_bytes, queue_depth, self._queue_capacity
        )
        pressure_value = self._pressure_value(pressure)

        action = "noop"
        reason: str | None = None
        rows_dropped = 0
        until_ts_ns = 0

        if pressure_value == "normal":
            pass
        elif pressure_value == "high_water":
            # Phase-1A: cold disabled, HighWater is a no-op (Phase-2+ promotes).
            pass
        elif pressure_value == "overflow":
            reason = "overflow"
            rows_dropped, until_ts_ns = self._drop_oldest_range(hot_bytes, reason)
            action = "drop"
        elif pressure_value == "sustained":
            reason = "sustained_pressure"
            rows_dropped, until_ts_ns = self._drop_oldest_range(hot_bytes, reason)
            action = "drop"
        else:
            raise ValueError(f"unknown pressure value: {pressure_value!r}")

        result = TickResult(
            pressure=pressure_value,
            action=action,
            reason=reason,
            hot_bytes=hot_bytes,
            queue_depth=queue_depth,
            rows_dropped=rows_dropped,
            until_ts_ns=until_ts_ns,
            tick_ts_ns=tick_ts_ns,
        )

        with self._lock:
            self._pressure_state = pressure_value
            if reason is not None:
                self._dropped_total[reason] = (
                    self._dropped_total.get(reason, 0) + rows_dropped
                )
            self._last_tick_state = result
            self._tick_seconds_total += time.monotonic() - tick_started_monotonic
            new_cursor = dict(self._cursor)
            new_cursor["last_tick_ts_ns"] = tick_ts_ns
            if action == "drop" and until_ts_ns > 0:
                new_cursor["last_promoted_ts_ns"] = until_ts_ns
        self._write_cursor(new_cursor)

        # Append audit record (one entry per tick that took action).
        if action != "noop":
            self._audit.append(
                {
                    "ts_ns": tick_ts_ns,
                    "action": action,
                    "until_ts_ns": int(until_ts_ns),
                    "rows": int(rows_dropped),
                    "bytes": int(self._drop_target_bytes(hot_bytes)),
                    "reason": reason,
                }
            )

        return result

    def force_tick(self) -> TickResult:
        """Synchronous one-tick; called by the operator HTTP endpoint + tests."""
        return self.tick()

    def force_promote(self, until_ts_ns: int) -> None:
        """Phase-2+ operator-driven promotion; Phase-1A is cold-disabled.

        Spec lines 81-83 reserve this for Phase-2+ when ColdStore exists.
        Phase-1A raises so an accidental operator POST surfaces a 501 rather
        than silently no-op'ing or worse, evicting hot data.
        """
        raise NotImplementedError(
            "force_promote is Phase-2+ — cold tier disabled in Phase-1A "
            "(set policy.cold_tier_enabled=true to re-evaluate after the "
            "ColdStore submodule ships)."
        )

    def set_policy(self, policy: dict[str, Any]) -> dict[str, Any]:
        """Validate + atomically persist a replacement policy.

        Appends a ``policy_update`` audit record so operator drift is
        traceable. Returns the normalized policy dict.
        """
        old_dict = self._policy.as_dict()
        new_dict = self._policy.update(policy)
        self._audit.append(
            {
                "ts_ns": time.time_ns(),
                "action": "policy_update",
                "until_ts_ns": 0,
                "rows": 0,
                "bytes": 0,
                "reason": "operator_update",
                "before": old_dict,
                "after": new_dict,
            }
        )
        return new_dict

    # ------------------------------------------------------------------
    # Background thread
    # ------------------------------------------------------------------

    def start(self, stop_event: threading.Event) -> None:
        """Start the background tick loop. Delegates to LifecycleThreadManager."""
        self._thread_manager.start(stop_event)

    def join(self, timeout: float | None = None) -> None:
        """Wait for the background thread to exit (operator + smoke tests)."""
        self._thread_manager.join(timeout)

    def is_alive(self) -> bool:
        return self._thread_manager.is_alive()

    def _audit_tick_error(self, exc: Exception) -> None:
        """Audit-append a tick error; never re-raises.

        Wired as ``on_error`` for ``LifecycleThreadManager`` so a transient
        HotStore hiccup is recorded but doesn't kill the daemon.
        """
        try:
            self._audit.append(
                {
                    "ts_ns": time.time_ns(),
                    "action": "tick_error",
                    "until_ts_ns": 0,
                    "rows": 0,
                    "bytes": 0,
                    "reason": f"{type(exc).__name__}: {exc}",
                }
            )
        except Exception:
            # If even the audit log is broken, swallow rather
            # than tear down the daemon.
            pass

    # ------------------------------------------------------------------
    # HTTP-facing handlers (called by service.py W4)
    # ------------------------------------------------------------------

    def handle_get_lifecycle_status(self) -> tuple[int, dict[str, str], bytes]:
        """Spec line 60: status JSON."""
        with self._lock:
            last = self._last_tick_state
            if last is not None:
                # Preserve historical last_tick_action JSON shape (subset of fields).
                last_dict: dict[str, Any] = {
                    "pressure": last.pressure,
                    "action": last.action,
                    "reason": last.reason,
                    "rows_dropped": last.rows_dropped,
                    "tick_ts_ns": last.tick_ts_ns,
                }
            else:
                last_dict = {
                    "pressure": "normal",
                    "action": "noop",
                    "reason": None,
                    "rows_dropped": 0,
                    "tick_ts_ns": 0,
                }
            dropped = sum(self._dropped_total.values())
            cursor_snapshot = dict(self._cursor)
        body = {
            "policy": self._policy.as_dict(),
            "cursor": cursor_snapshot,
            "last_tick_action": last_dict,
            "hot_bytes": int(self._hot_store.range_size_bytes()),
            "cold_bytes": 0,  # Phase-1A: cold disabled.
            "retention_drops_total": dropped,
        }
        encoded = json.dumps(body, sort_keys=True).encode("utf-8")
        return 200, {"Content-Type": "application/json"}, encoded

    def handle_post_lifecycle_policy(
        self, body: bytes
    ) -> tuple[int, dict[str, str], bytes]:
        """Spec line 62-63: operator-only policy POST."""
        try:
            payload = json.loads(body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            err = json.dumps({"error": f"invalid json: {exc}"}).encode("utf-8")
            return 400, {"Content-Type": "application/json"}, err
        try:
            new_policy = self.set_policy(payload)
        except ValueError as exc:
            err = json.dumps({"error": str(exc)}).encode("utf-8")
            return 400, {"Content-Type": "application/json"}, err
        encoded = json.dumps(new_policy, sort_keys=True).encode("utf-8")
        return 200, {"Content-Type": "application/json"}, encoded

    def handle_post_lifecycle_force_tick(
        self,
    ) -> tuple[int, dict[str, str], bytes]:
        """Spec line 64-65: operator-triggered one-tick."""
        try:
            result = self.force_tick()
        except Exception as exc:  # noqa: BLE001
            err = json.dumps({"error": f"{type(exc).__name__}: {exc}"}).encode("utf-8")
            return 500, {"Content-Type": "application/json"}, err
        encoded = json.dumps(result.to_dict(), sort_keys=True).encode("utf-8")
        return 200, {"Content-Type": "application/json"}, encoded

    # ------------------------------------------------------------------
    # Metrics (spec lines 91-99)
    # ------------------------------------------------------------------

    def metrics_lines(self, service_name: str) -> list[bytes]:
        """Prometheus text v0.0.4 lines (no trailing newline per line).

        One line per (metric, label-combination). Caller (service.py) joins
        with newline and writes as ``text/plain; version=0.0.4``.
        """
        with self._lock:
            dropped = dict(self._dropped_total)
            promoted = self._promoted_total
            pressure_state = self._pressure_state
            last_tick_ts_ns = (
                self._last_tick_state.tick_ts_ns
                if self._last_tick_state is not None
                else 0
            )
            tick_seconds_total = self._tick_seconds_total
        cursor_ts_ns = int(self._cursor.get("last_promoted_ts_ns", 0))
        svc = service_name

        lines: list[bytes] = []
        for reason in ("overflow", "sustained_pressure", "cold_unavailable"):
            lines.append(
                (
                    f'sts2_q3_lifecycle_dropped_rows_total'
                    f'{{reason="{reason}",service="{svc}"}} {dropped[reason]}'
                ).encode("utf-8")
            )
        lines.append(
            (
                f'sts2_q3_lifecycle_promoted_rows_total'
                f'{{service="{svc}"}} {promoted}'
            ).encode("utf-8")
        )
        lines.append(
            (
                f'sts2_q3_lifecycle_cursor_ts_ns{{service="{svc}"}} {cursor_ts_ns}'
            ).encode("utf-8")
        )
        for state in ("normal", "high_water", "overflow", "sustained"):
            value = 1 if state == pressure_state else 0
            lines.append(
                (
                    f'sts2_q3_lifecycle_pressure_state'
                    f'{{state="{state}",service="{svc}"}} {value}'
                ).encode("utf-8")
            )
        lines.append(
            (
                f'sts2_q3_lifecycle_last_tick_ts_ns'
                f'{{service="{svc}"}} {last_tick_ts_ns}'
            ).encode("utf-8")
        )
        lines.append(
            (
                f'sts2_q3_lifecycle_tick_seconds_total'
                f'{{service="{svc}"}} {tick_seconds_total:.6f}'
            ).encode("utf-8")
        )
        return lines

    # ------------------------------------------------------------------
    # Drop semantics (spec line 71-74)
    # ------------------------------------------------------------------

    def _drop_target_bytes(self, hot_bytes: int) -> int:
        """Bytes-to-free target for one drop cycle.

        Phase-1A heuristic per prompt: max(1 GiB, 10% of overflow margin).
        Even on Sustained where margin is 0 or negative, we still drop
        at least 1 GiB so the alert pairs with measurable eviction.
        """
        overflow = self._policy.hot_overflow_bytes()
        margin = max(0, hot_bytes - overflow)
        fractional = int(margin * _DROP_FRACTION_OF_OVERFLOW_MARGIN)
        return max(_MIN_DROP_BYTES, fractional)

    def _drop_oldest_range(
        self, hot_bytes: int, reason: str
    ) -> tuple[int, int]:
        """Evict the oldest range. Returns (rows_dropped, until_ts_ns).

        Strategy:
          1. Compute ``target_bytes`` (see ``_drop_target_bytes``).
          2. Scan from start, accumulating ``len(traj_bytes)``.
          3. When cumulative >= target, capture ``until_ts_ns`` = last
             scanned ts + 1 (exclusive upper bound for ``delete_range``).
          4. ``hot_store.delete_range(until_ts_ns)`` evicts; returns count.
        """
        target_bytes = self._drop_target_bytes(hot_bytes)
        accumulated = 0
        cutoff_ts_ns = 0
        # Scan from -1 so HotStore returns the smallest-ts entry on first
        # `next`. scan() yields ascending ingest_ts_ns.
        for ts_ns, _tid, traj_bytes in self._hot_store.scan(
            after_ts_ns=-1, limit=10**9
        ):
            cutoff_ts_ns = int(ts_ns)
            accumulated += len(traj_bytes) if traj_bytes is not None else 0
            if accumulated >= target_bytes:
                break

        if cutoff_ts_ns == 0:
            # Empty hot tier; nothing to drop. Still record the no-op as a
            # zero-row drop so audit reflects pressure-state observed.
            return 0, 0

        until_ts_ns = cutoff_ts_ns + 1
        rows = int(self._hot_store.delete_range(until_ts_ns))
        return rows, until_ts_ns

    # ------------------------------------------------------------------
    # Cursor persistence
    # ------------------------------------------------------------------

    def _load_or_init_cursor(self) -> dict[str, int]:
        if self._cursor_path.exists():
            with self._cursor_path.open("r", encoding="utf-8") as handle:
                cursor = json.load(handle)
            return {
                "last_promoted_ts_ns": int(cursor.get("last_promoted_ts_ns", 0)),
                "last_tick_ts_ns": int(cursor.get("last_tick_ts_ns", 0)),
            }
        fresh = {"last_promoted_ts_ns": 0, "last_tick_ts_ns": 0}
        self._write_cursor(fresh)
        return fresh

    def _write_cursor(self, cursor: dict[str, int]) -> None:
        tmp = self._cursor_path.with_suffix(".json.tmp")
        with tmp.open("w", encoding="utf-8") as handle:
            json.dump(cursor, handle, sort_keys=True)
            handle.write("\n")
        tmp.replace(self._cursor_path)
        with self._lock:
            self._cursor = dict(cursor)

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _pressure_value(pressure: Any) -> str:
        """Normalize a ``Pressure`` enum (or string) to its lowercase value."""
        if hasattr(pressure, "value"):
            return str(pressure.value)
        return str(pressure)

    # Test helpers / introspection.

    @property
    def policy(self) -> LifecyclePolicy:
        return self._policy

    @property
    def audit(self) -> AuditLog:
        return self._audit

    @property
    def cursor(self) -> dict[str, int]:
        with self._lock:
            return dict(self._cursor)
