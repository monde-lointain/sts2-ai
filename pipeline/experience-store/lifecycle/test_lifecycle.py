"""Unit tests for Lifecycle (S0.C.gamma).

Spec: pipeline/experience-store/docs/specs/modules/lifecycle.md.
Mocked HotStore + RetentionController; no RocksDB instances needed.
"""

from __future__ import annotations

import json
import threading
import time

import pytest
from control_plane.retention import Pressure

from lifecycle.audit import AUDIT_FILE
from lifecycle.lifecycle import Lifecycle, TickResult
from lifecycle.policy import POLICY_FILE

# ---------------------------------------------------------------------------
# Test doubles
# ---------------------------------------------------------------------------


class _FakeHotStore:
    """Minimal HotStore stub matching the spec API surface."""

    def __init__(self, hot_bytes: int = 0) -> None:
        self.hot_bytes = int(hot_bytes)
        self.rows: list[tuple[int, bytes, bytes]] = []  # (ts, tid, body)
        self.delete_calls: list[int] = []
        self.delete_return = 0

    def range_size_bytes(self) -> int:
        return self.hot_bytes

    def scan(self, after_ts_ns: int, limit: int):
        for ts, tid, body in self.rows:
            if ts > after_ts_ns:
                yield ts, tid, body

    def delete_range(self, until_ts_ns: int) -> int:
        self.delete_calls.append(int(until_ts_ns))
        # Simulate the bytes drop so subsequent ticks see "fixed" state.
        survivors = [r for r in self.rows if r[0] >= until_ts_ns]
        dropped = len(self.rows) - len(survivors)
        self.rows = survivors
        # Cap reported bytes; if explicit delete_return was set, use it.
        if self.delete_return:
            return int(self.delete_return)
        return dropped


class _FakeRetention:
    """Returns the configured Pressure regardless of inputs."""

    def __init__(self, pressure: Pressure) -> None:
        self.pressure = pressure
        self.calls: list[tuple[int, int, int]] = []

    def classify_pressure(self, hot_bytes: int, queue_depth: int, queue_capacity: int) -> Pressure:
        self.calls.append((int(hot_bytes), int(queue_depth), int(queue_capacity)))
        return self.pressure


# ---------------------------------------------------------------------------
# Construction / persistence
# ---------------------------------------------------------------------------


def test_fresh_init_creates_policy_cursor_audit(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    base = tmp_path / "lifecycle"
    assert (base / POLICY_FILE).exists()
    assert (base / "cursor.json").exists()
    assert (base / AUDIT_FILE).exists()


def test_reinit_does_not_overwrite_existing_state(tmp_path):
    """Spec: existing on-disk values must survive Lifecycle reconstruction."""
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc1 = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc1.set_policy(
        {
            "hot_high_water_bytes": 1234,
            "hot_overflow_bytes": 5678,
            "tick_interval_seconds": 7,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    lc2 = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    assert lc2.policy.hot_high_water_bytes() == 1234
    assert lc2.policy.hot_overflow_bytes() == 5678
    assert lc2.policy.tick_interval_seconds() == 7


# ---------------------------------------------------------------------------
# tick() branches per pressure
# ---------------------------------------------------------------------------


def test_tick_normal_is_noop(tmp_path):
    """RetentionController -> NORMAL: cursor unchanged, audit unchanged.

    Spec testing strategy #1: "no-op tick when Normal."
    """
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 10,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    result = lc.tick()
    assert isinstance(result, TickResult)
    assert result.pressure == "normal"
    assert result.action == "noop"
    assert result.reason is None
    assert result.rows_dropped == 0
    assert hot.delete_calls == []
    # Audit empty (no actions audited besides ticks).
    assert lc.audit.read_all() == []


def test_tick_high_water_is_noop_phase_1a(tmp_path):
    """Phase-1A: cold disabled, HighWater is a no-op (no promote, no drop)."""
    hot = _FakeHotStore(hot_bytes=60 * 1024**3)
    ret = _FakeRetention(Pressure.HIGH_WATER)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 10,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    result = lc.tick()
    assert result.pressure == "high_water"
    assert result.action == "noop"
    assert hot.delete_calls == []


def test_tick_overflow_drops_oldest_range(tmp_path):
    """Spec testing strategy #3: Overflow + cold disabled -> drop.

    Audit log records reason=overflow; dropped counter increments.
    """
    # 120 GiB hot bytes triggers overflow.
    hot = _FakeHotStore(hot_bytes=120 * 1024**3)
    # Seed 3 rows so scan->delete_range has work.
    hot.rows = [
        (100, b"a" * 16, b"\x00" * (512 * 1024**2)),  # 512 MiB
        (200, b"b" * 16, b"\x00" * (512 * 1024**2)),
        (300, b"c" * 16, b"\x00" * (512 * 1024**2)),
    ]
    ret = _FakeRetention(Pressure.OVERFLOW)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 10,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    result = lc.tick()
    assert result.pressure == "overflow"
    assert result.action == "drop"
    assert result.reason == "overflow"
    assert hot.delete_calls, "delete_range must be called"
    assert result.rows_dropped > 0
    audit = lc.audit.read_all()
    assert any(r.get("action") == "drop" and r.get("reason") == "overflow" for r in audit)


def test_tick_sustained_emits_alert_and_drops(tmp_path):
    """Spec testing strategy #4: Sustained -> escalate alert + drop.

    Audit log records reason=sustained_pressure; pressure_state metric == 1
    for state=sustained.
    """
    hot = _FakeHotStore(hot_bytes=60 * 1024**3)
    hot.rows = [
        (10, b"x" * 16, b"\x00" * (2 * 1024**3)),  # 2 GiB
        (20, b"y" * 16, b"\x00" * (2 * 1024**3)),
    ]
    ret = _FakeRetention(Pressure.SUSTAINED)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 900,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    result = lc.tick()
    assert result.pressure == "sustained"
    assert result.action == "drop"
    assert result.reason == "sustained_pressure"
    assert hot.delete_calls
    assert result.rows_dropped > 0
    audit = lc.audit.read_all()
    assert any(r.get("action") == "drop" and r.get("reason") == "sustained_pressure" for r in audit)
    # Metric: sustained == 1, others 0.
    lines = [b.decode("utf-8") for b in lc.metrics_lines("experience-store")]
    sustained_line = next(l for l in lines if 'state="sustained"' in l and "pressure_state" in l)
    assert sustained_line.endswith(" 1")
    normal_line = next(l for l in lines if 'state="normal"' in l and "pressure_state" in l)
    assert normal_line.endswith(" 0")


def test_tick_overflow_with_empty_hot_is_safe_noop(tmp_path):
    """Mocked overflow but no rows to drop -> zero-row drop, no crash."""
    hot = _FakeHotStore(hot_bytes=120 * 1024**3)  # No rows seeded.
    ret = _FakeRetention(Pressure.OVERFLOW)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    result = lc.tick()
    assert result.pressure == "overflow"
    assert result.rows_dropped == 0
    assert result.until_ts_ns == 0


# ---------------------------------------------------------------------------
# force_promote / set_policy
# ---------------------------------------------------------------------------


def test_force_promote_raises_phase_1a(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    with pytest.raises(NotImplementedError, match="Phase-2"):
        lc.force_promote(until_ts_ns=12345)


def test_set_policy_persists_and_affects_subsequent_tick(tmp_path):
    """set_policy({new_thresholds}) -> policy.json updated + tick uses new values."""
    hot = _FakeHotStore(hot_bytes=30 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    new = {
        "hot_high_water_bytes": 1 * 1024**3,
        "hot_overflow_bytes": 2 * 1024**3,
        "tick_interval_seconds": 30,
        "cold_tier_enabled": False,
        "max_age_seconds": None,
    }
    lc.set_policy(new)
    persisted = json.loads((tmp_path / "lifecycle" / POLICY_FILE).read_text(encoding="utf-8"))
    assert persisted["hot_high_water_bytes"] == 1 * 1024**3
    assert persisted["hot_overflow_bytes"] == 2 * 1024**3
    # Tick uses the new policy (visible via metrics_lines path, since tick
    # consults policy.hot_overflow_bytes inside _drop_target_bytes when
    # pressure drops).
    assert lc.policy.hot_overflow_bytes() == 2 * 1024**3


def test_set_policy_rejects_invalid(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    with pytest.raises(ValueError):
        lc.set_policy({"hot_high_water_bytes": -1})


# ---------------------------------------------------------------------------
# Background thread
# ---------------------------------------------------------------------------


def test_start_runs_at_least_one_tick(tmp_path):
    """Background loop must tick at least once within the configured interval."""
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    # Shorten tick interval so the test doesn't sit on the default 60 s.
    lc.set_policy(
        {
            "hot_high_water_bytes": 50 * 1024**3,
            "hot_overflow_bytes": 100 * 1024**3,
            "tick_interval_seconds": 0.05,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    stop = threading.Event()
    lc.start(stop)
    try:
        # Wait until classify_pressure has been called at least once.
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline and not ret.calls:
            time.sleep(0.02)
        assert ret.calls, "background thread must call classify_pressure"
    finally:
        stop.set()
        lc.join(timeout=2.0)
    assert not lc.is_alive(), "thread must exit after stop"


def test_start_stops_cleanly_on_signal(tmp_path):
    """Stops within (tick_interval + 5 s) after stop_event.set()."""
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.set_policy(
        {
            "hot_high_water_bytes": 50 * 1024**3,
            "hot_overflow_bytes": 100 * 1024**3,
            "tick_interval_seconds": 0.2,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    stop = threading.Event()
    lc.start(stop)
    time.sleep(0.05)  # Let the first tick fire.
    stop.set()
    deadline = time.monotonic() + 5.5  # tick (0.2) + safety (5 s)
    while time.monotonic() < deadline and lc.is_alive():
        time.sleep(0.05)
    assert not lc.is_alive()


def test_double_start_is_rejected(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.set_policy(
        {
            "hot_high_water_bytes": 50 * 1024**3,
            "hot_overflow_bytes": 100 * 1024**3,
            "tick_interval_seconds": 0.05,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    stop = threading.Event()
    lc.start(stop)
    try:
        with pytest.raises(RuntimeError, match="already running"):
            lc.start(threading.Event())
    finally:
        stop.set()
        lc.join(timeout=2.0)


# ---------------------------------------------------------------------------
# Metrics
# ---------------------------------------------------------------------------


def test_metrics_lines_emit_expected_shape(tmp_path):
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.tick()
    lines = [b.decode("utf-8") for b in lc.metrics_lines("experience-store")]

    # Counters for all three reasons present.
    assert any(
        "sts2_q3_lifecycle_dropped_rows_total" in l and 'reason="overflow"' in l for l in lines
    )
    assert any(
        "sts2_q3_lifecycle_dropped_rows_total" in l and 'reason="sustained_pressure"' in l
        for l in lines
    )
    assert any(
        "sts2_q3_lifecycle_dropped_rows_total" in l and 'reason="cold_unavailable"' in l
        for l in lines
    )
    # promoted counter present.
    assert any("sts2_q3_lifecycle_promoted_rows_total" in l for l in lines)
    # cursor gauge.
    assert any("sts2_q3_lifecycle_cursor_ts_ns" in l for l in lines)
    # last_tick_ts_ns gauge.
    assert any("sts2_q3_lifecycle_last_tick_ts_ns" in l for l in lines)
    # tick_seconds_total counter.
    assert any("sts2_q3_lifecycle_tick_seconds_total" in l for l in lines)


def test_metrics_exactly_one_pressure_state_is_one(tmp_path):
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.tick()
    lines = [b.decode("utf-8") for b in lc.metrics_lines("experience-store")]
    state_lines = [l for l in lines if "sts2_q3_lifecycle_pressure_state" in l]
    ones = [l for l in state_lines if l.endswith(" 1")]
    zeros = [l for l in state_lines if l.endswith(" 0")]
    assert len(ones) == 1
    assert len(zeros) == 3
    # The one == 1 should be the "normal" line.
    assert 'state="normal"' in ones[0]


# ---------------------------------------------------------------------------
# Cursor + HTTP handlers
# ---------------------------------------------------------------------------


def test_tick_advances_last_tick_ts_in_cursor(tmp_path):
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    before = lc.cursor["last_tick_ts_ns"]
    lc.tick()
    after = lc.cursor["last_tick_ts_ns"]
    assert after > before


def test_tick_overflow_advances_last_promoted_ts(tmp_path):
    hot = _FakeHotStore(hot_bytes=120 * 1024**3)
    hot.rows = [
        (100, b"a" * 16, b"\x00" * (512 * 1024**2)),
        (200, b"b" * 16, b"\x00" * (512 * 1024**2)),
        (300, b"c" * 16, b"\x00" * (512 * 1024**2)),
    ]
    ret = _FakeRetention(Pressure.OVERFLOW)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    assert lc.cursor["last_promoted_ts_ns"] == 0
    lc.tick()
    assert lc.cursor["last_promoted_ts_ns"] > 0
    # Persisted to disk.
    persisted = json.loads((tmp_path / "lifecycle" / "cursor.json").read_text(encoding="utf-8"))
    assert persisted["last_promoted_ts_ns"] == lc.cursor["last_promoted_ts_ns"]


def test_handle_get_lifecycle_status_returns_expected_keys(tmp_path):
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.tick()
    status, headers, body = lc.handle_get_lifecycle_status()
    assert status == 200
    assert headers["Content-Type"] == "application/json"
    payload = json.loads(body.decode("utf-8"))
    for key in (
        "policy",
        "cursor",
        "last_tick_action",
        "hot_bytes",
        "cold_bytes",
        "retention_drops_total",
    ):
        assert key in payload
    assert payload["cold_bytes"] == 0
    assert payload["retention_drops_total"] == 0


def test_handle_post_lifecycle_policy_round_trip(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    body = json.dumps(
        {
            "hot_high_water_bytes": 1024,
            "hot_overflow_bytes": 2048,
            "tick_interval_seconds": 5,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    ).encode("utf-8")
    status, _, resp = lc.handle_post_lifecycle_policy(body)
    assert status == 200
    payload = json.loads(resp.decode("utf-8"))
    assert payload["hot_high_water_bytes"] == 1024
    assert lc.policy.hot_high_water_bytes() == 1024


def test_handle_post_lifecycle_policy_rejects_invalid(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    status, _, resp = lc.handle_post_lifecycle_policy(b"not json")
    assert status == 400
    err = json.loads(resp.decode("utf-8"))
    assert "error" in err


def test_handle_post_lifecycle_force_tick_returns_result(tmp_path):
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    status, _, resp = lc.handle_post_lifecycle_force_tick()
    assert status == 200
    payload = json.loads(resp.decode("utf-8"))
    assert payload["pressure"] == "normal"
    assert payload["action"] == "noop"


# ---------------------------------------------------------------------------
# Queue-depth provider plumbing
# ---------------------------------------------------------------------------


def test_queue_depth_provider_is_consulted_each_tick(tmp_path):
    """Provider receives no args and is called once per tick."""
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    counter = {"calls": 0}

    def provider() -> int:
        counter["calls"] += 1
        return 42

    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=provider,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    lc.tick()
    lc.tick()
    assert counter["calls"] == 2
    # Provider value reaches classify_pressure call args.
    assert ret.calls[0][1] == 42
    assert ret.calls[1][1] == 42


def test_constructor_rejects_non_callable_provider(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    with pytest.raises(TypeError, match="callable"):
        Lifecycle(
            hot_store=hot,
            retention_controller=ret,
            queue_depth_provider=0,  # type: ignore[arg-type]
            queue_capacity=1000,
            data_dir=tmp_path,
        )


def test_constructor_rejects_zero_capacity(tmp_path):
    hot = _FakeHotStore()
    ret = _FakeRetention(Pressure.NORMAL)
    with pytest.raises(ValueError, match="queue_capacity"):
        Lifecycle(
            hot_store=hot,
            retention_controller=ret,
            queue_depth_provider=lambda: 0,
            queue_capacity=0,
            data_dir=tmp_path,
        )


import inspect


def test_handle_get_lifecycle_status_reads_cursor_under_lock():
    src = inspect.getsource(Lifecycle.handle_get_lifecycle_status)
    assert "with self._lock:" in src, "missing lock acquisition"
    lock_idx = src.find("with self._lock:")
    cursor_idx = src.find("self._cursor")
    assert lock_idx >= 0 and cursor_idx > lock_idx, (
        "_cursor must be read AFTER `with self._lock:` opens"
    )


def test_tick_writes_cursor_under_lock():
    src = inspect.getsource(Lifecycle.tick)
    cursor_read_idx = src.find("new_cursor = dict(self._cursor)")
    assert cursor_read_idx > 0
    block_starts = [i for i in range(cursor_read_idx) if src.startswith("with self._lock:", i)]
    assert block_starts, "no `with self._lock:` precedes the cursor read in tick()"


def test_cursor_property_takes_lock():
    src = inspect.getsource(Lifecycle.cursor.fget)  # type: ignore[union-attr]
    assert "with self._lock:" in src


def test_last_tick_state_is_typed_tickresult(tmp_path):
    """After tick(), Lifecycle._last_tick_state is a TickResult (None pre-first-tick)."""
    hot = _FakeHotStore(hot_bytes=10 * 1024**3)
    ret = _FakeRetention(Pressure.NORMAL)
    lc = Lifecycle(
        hot_store=hot,
        retention_controller=ret,
        queue_depth_provider=lambda: 0,
        queue_capacity=1000,
        data_dir=tmp_path,
    )
    assert lc._last_tick_state is None
    lc.force_tick()
    assert isinstance(lc._last_tick_state, TickResult)
    assert lc._last_tick_state.pressure == "normal"
    assert lc._last_tick_state.action == "noop"
