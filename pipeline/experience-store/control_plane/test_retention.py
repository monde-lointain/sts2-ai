"""Unit tests for RetentionController (S0.B.gamma + S0.B.gamma' spec alignment)."""

from __future__ import annotations

import json
from unittest import mock

from control_plane.retention import (
    DEFAULT_HOT_BYTES_WINDOW_SECONDS,
    DEFAULT_HOT_HIGH_WATER_BYTES,
    DEFAULT_HOT_OVERFLOW_BYTES,
    DEFAULT_INGEST_QUEUE_CAPACITY,
    DEFAULT_QUEUE_DEPTH_THRESHOLD_FRACTION,
    DEFAULT_QUEUE_DEPTH_WINDOW_SECONDS,
    POLICY_DIR,
    POLICY_FILE,
    Pressure,
    RetentionController,
    RetentionPolicy,
)


# ---------------------------------------------------------------------------
# Threshold defaults / persistence
# ---------------------------------------------------------------------------


def test_default_thresholds_match_q3_adr_007(tmp_path):
    policy = RetentionController({}, tmp_path)
    assert policy.high_water_bytes() == 50 * 1024**3
    assert policy.overflow_bytes() == 100 * 1024**3
    assert policy.ingest_queue_capacity() == 10000
    assert policy.high_water_bytes() == DEFAULT_HOT_HIGH_WATER_BYTES
    assert policy.overflow_bytes() == DEFAULT_HOT_OVERFLOW_BYTES
    assert policy.ingest_queue_capacity() == DEFAULT_INGEST_QUEUE_CAPACITY


def test_default_windows_match_q3_adr_008(tmp_path):
    policy = RetentionController({}, tmp_path)
    assert policy.hot_bytes_window_seconds() == DEFAULT_HOT_BYTES_WINDOW_SECONDS
    assert policy.queue_depth_window_seconds() == DEFAULT_QUEUE_DEPTH_WINDOW_SECONDS
    assert (
        policy.queue_depth_threshold_fraction()
        == DEFAULT_QUEUE_DEPTH_THRESHOLD_FRACTION
    )


def test_config_override_persisted(tmp_path):
    policy = RetentionController(
        {
            "hot_high_water_bytes": 10 * 1024**3,
            "hot_overflow_bytes": 20 * 1024**3,
            "ingest_queue_capacity": 5000,
        },
        tmp_path,
    )
    assert policy.high_water_bytes() == 10 * 1024**3
    assert policy.overflow_bytes() == 20 * 1024**3
    assert policy.ingest_queue_capacity() == 5000

    persisted = json.loads(
        (tmp_path / POLICY_DIR / POLICY_FILE).read_text(encoding="utf-8")
    )
    assert persisted["hot_high_water_bytes"] == 10 * 1024**3
    assert persisted["hot_overflow_bytes"] == 20 * 1024**3
    assert persisted["ingest_queue_capacity"] == 5000


def test_policy_json_written_on_first_init(tmp_path):
    policy_path = tmp_path / POLICY_DIR / POLICY_FILE
    assert not policy_path.exists()
    RetentionController({}, tmp_path)
    assert policy_path.exists()
    persisted = json.loads(policy_path.read_text(encoding="utf-8"))
    assert persisted["hot_high_water_bytes"] == DEFAULT_HOT_HIGH_WATER_BYTES


def test_policy_json_includes_three_new_window_fields(tmp_path):
    """Spec lines 50-56: policy.json carries the ADR-008 window config."""
    RetentionController({}, tmp_path)
    persisted = json.loads(
        (tmp_path / POLICY_DIR / POLICY_FILE).read_text(encoding="utf-8")
    )
    assert persisted["hot_bytes_window_seconds"] == 60
    assert persisted["queue_depth_window_seconds"] == 30
    assert persisted["queue_depth_threshold_fraction"] == 0.8


def test_window_config_override_persisted(tmp_path):
    policy = RetentionController(
        {
            "hot_bytes_window_seconds": 120.0,
            "queue_depth_window_seconds": 45.0,
            "queue_depth_threshold_fraction": 0.7,
        },
        tmp_path,
    )
    assert policy.hot_bytes_window_seconds() == 120.0
    assert policy.queue_depth_window_seconds() == 45.0
    assert policy.queue_depth_threshold_fraction() == 0.7

    persisted = json.loads(
        (tmp_path / POLICY_DIR / POLICY_FILE).read_text(encoding="utf-8")
    )
    assert persisted["hot_bytes_window_seconds"] == 120.0
    assert persisted["queue_depth_window_seconds"] == 45.0
    assert persisted["queue_depth_threshold_fraction"] == 0.7


def test_policy_json_not_overwritten_on_reinit(tmp_path):
    # First init writes custom values.
    RetentionController(
        {
            "hot_high_water_bytes": 7 * 1024**3,
            "hot_overflow_bytes": 14 * 1024**3,
            "ingest_queue_capacity": 7000,
        },
        tmp_path,
    )
    # Re-init with different config keys; persisted values win.
    policy = RetentionController(
        {
            "hot_high_water_bytes": 999,
            "hot_overflow_bytes": 999,
            "ingest_queue_capacity": 999,
        },
        tmp_path,
    )
    assert policy.high_water_bytes() == 7 * 1024**3
    assert policy.overflow_bytes() == 14 * 1024**3
    assert policy.ingest_queue_capacity() == 7000


# ---------------------------------------------------------------------------
# Backward compat: RetentionPolicy alias
# ---------------------------------------------------------------------------


def test_retention_policy_alias_is_retention_controller(tmp_path):
    """RetentionPolicy is retained as a one-wave backward-compat alias."""
    assert RetentionPolicy is RetentionController
    policy = RetentionPolicy({}, tmp_path)
    assert isinstance(policy, RetentionController)


# ---------------------------------------------------------------------------
# classify_pressure: the four spec states
# ---------------------------------------------------------------------------


def test_classify_pressure_normal_under_threshold(tmp_path):
    policy = RetentionController(
        {"hot_high_water_bytes": 1000, "hot_overflow_bytes": 2000},
        tmp_path,
    )
    # Below high_water and queue depth well under threshold.
    assert policy.classify_pressure(500, 0, 100) == Pressure.NORMAL


def test_classify_pressure_high_water_when_hot_bytes_above(tmp_path):
    policy = RetentionController(
        {"hot_high_water_bytes": 1000, "hot_overflow_bytes": 5000},
        tmp_path,
    )
    # hot_bytes above high_water, below overflow, single sample (no sustained run).
    assert policy.classify_pressure(2000, 0, 100) == Pressure.HIGH_WATER


def test_classify_pressure_high_water_when_queue_above_threshold(tmp_path):
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1_000_000,
            "hot_overflow_bytes": 5_000_000,
            "ingest_queue_capacity": 100,
        },
        tmp_path,
    )
    # queue_depth >= 0.8 * 100 = 80; hot_bytes below high_water.
    assert policy.classify_pressure(0, 90, 100) == Pressure.HIGH_WATER


def test_classify_pressure_overflow(tmp_path):
    policy = RetentionController(
        {"hot_high_water_bytes": 1000, "hot_overflow_bytes": 2000},
        tmp_path,
    )
    assert policy.classify_pressure(2500, 0, 100) == Pressure.OVERFLOW


def test_classify_pressure_overflow_takes_precedence_over_sustained(tmp_path):
    """Overflow > Sustained per spec precedence (immediate-drop trigger)."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1000,
            "hot_overflow_bytes": 2000,
            "hot_bytes_window_seconds": 60.0,
        },
        tmp_path,
    )
    # Build a sustained run, then a single sample above overflow.
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        # 65 s of hot_bytes=1500 (above high_water=1000) -> sustained.
        for i in range(14):
            t[0] = i * 5.0
            policy.classify_pressure(1500, 0, 100)
        # Now jump to overflow; classify should return OVERFLOW not SUSTAINED.
        t[0] = 70.0
        assert policy.classify_pressure(2500, 0, 100) == Pressure.OVERFLOW


def test_classify_pressure_sustained_after_60s_hot_bytes(tmp_path):
    """Hot-bytes sustained branch fires after >= hot_bytes_window_seconds."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1000,
            "hot_overflow_bytes": 10_000,
            "hot_bytes_window_seconds": 60.0,
        },
        tmp_path,
    )
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        # Inject 60+ seconds of overage at 5-second cadence; the final
        # call covers a 65 s window of continuous hot_bytes > high_water.
        result = Pressure.NORMAL
        for i in range(14):  # 0, 5, ... 65
            t[0] = i * 5.0
            result = policy.classify_pressure(1500, 0, 100)
        assert result == Pressure.SUSTAINED


def test_classify_pressure_sustained_after_30s_queue_depth(tmp_path):
    """Queue-depth sustained branch fires after >= queue_depth_window_seconds."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1_000_000,
            "hot_overflow_bytes": 10_000_000,
            "ingest_queue_capacity": 100,
            "queue_depth_window_seconds": 30.0,
        },
        tmp_path,
    )
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        result = Pressure.NORMAL
        for i in range(8):  # 0, 5, ... 35
            t[0] = i * 5.0
            result = policy.classify_pressure(0, 90, 100)
        assert result == Pressure.SUSTAINED


def test_classify_pressure_short_spike_stays_high_water_not_sustained(tmp_path):
    """30 s of hot-bytes overage stays HIGH_WATER (under 60 s window)."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1000,
            "hot_overflow_bytes": 10_000,
            "hot_bytes_window_seconds": 60.0,
        },
        tmp_path,
    )
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        result = Pressure.NORMAL
        for i in range(7):  # 0, 5, ... 30
            t[0] = i * 5.0
            result = policy.classify_pressure(1500, 0, 100)
        assert result == Pressure.HIGH_WATER


def test_classify_pressure_easing_returns_normal(tmp_path):
    """After Sustained then easing, classify returns Normal on next call (hysteresis)."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1000,
            "hot_overflow_bytes": 10_000,
            "hot_bytes_window_seconds": 60.0,
        },
        tmp_path,
    )
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        # Build a sustained run.
        for i in range(14):
            t[0] = i * 5.0
            policy.classify_pressure(1500, 0, 100)
        # Drop the most-recent sample below threshold; next call observes
        # the easing.
        t[0] = 70.0
        result = policy.classify_pressure(500, 0, 100)
        assert result == Pressure.NORMAL


# ---------------------------------------------------------------------------
# Backward-compat: sustained_pressure(...) shim
# ---------------------------------------------------------------------------


def test_sustained_pressure_hot_bytes_60s_returns_true(tmp_path):
    policy = RetentionController({"hot_high_water_bytes": 1000}, tmp_path)
    high = 2000  # above 1000 threshold
    # Synthesize a continuous run of 70 seconds, sample every 5 s.
    history = [(t, high, 0) for t in range(0, 65, 5)]
    now_ts = 65.0
    assert policy.sustained_pressure(high, 0, now_ts, history) is True


def test_sustained_pressure_short_spike_returns_false(tmp_path):
    policy = RetentionController({"hot_high_water_bytes": 1000}, tmp_path)
    high = 2000
    # Only 30 seconds of overage — under the 60 s window.
    history = [(t, high, 0) for t in range(0, 30, 5)]
    now_ts = 30.0
    assert policy.sustained_pressure(high, 0, now_ts, history) is False


def test_sustained_pressure_below_threshold_returns_false(tmp_path):
    policy = RetentionController({"hot_high_water_bytes": 1000}, tmp_path)
    history = [(t, 500, 0) for t in range(0, 120, 5)]
    now_ts = 120.0
    assert policy.sustained_pressure(500, 0, now_ts, history) is False


def test_sustained_pressure_queue_30s_returns_true(tmp_path):
    policy = RetentionController(
        {"hot_high_water_bytes": 1_000_000, "ingest_queue_capacity": 100},
        tmp_path,
    )
    # Threshold is 0.8 * 100 = 80; depth=90 is above.
    history = [(t, 0, 90) for t in range(0, 35, 5)]
    now_ts = 35.0
    assert policy.sustained_pressure(0, 90, now_ts, history) is True


def test_sustained_pressure_queue_short_spike_returns_false(tmp_path):
    policy = RetentionController(
        {"hot_high_water_bytes": 1_000_000, "ingest_queue_capacity": 100},
        tmp_path,
    )
    # Only 15 seconds of overage — under 30 s window.
    history = [(t, 0, 90) for t in range(0, 15, 5)]
    now_ts = 15.0
    assert policy.sustained_pressure(0, 90, now_ts, history) is False


def test_sustained_pressure_easing_resets_run(tmp_path):
    """Per Q3-ADR-008's hysteresis requirement (spec test #2 in retention)."""
    policy = RetentionController({"hot_high_water_bytes": 1000}, tmp_path)
    high = 2000
    low = 500
    # 70 s above, then 5 s below; the run gets reset at the most-recent
    # below-threshold sample.
    history = [(t, high, 0) for t in range(0, 70, 5)]
    history += [(70.0, low, 0)]
    now_ts = 75.0
    # Current sample is low; predicate should be False.
    assert policy.sustained_pressure(low, 0, now_ts, history) is False


def test_sustained_pressure_empty_history_returns_false(tmp_path):
    policy = RetentionController({"hot_high_water_bytes": 1000}, tmp_path)
    # No prior samples, just the current observation -> no window covered.
    assert policy.sustained_pressure(2000, 0, 10.0, []) is False


def test_sustained_pressure_shim_preserves_ring_buffer(tmp_path):
    """Calling sustained_pressure(...) must not corrupt classify_pressure state."""
    policy = RetentionController(
        {
            "hot_high_water_bytes": 1000,
            "hot_overflow_bytes": 10_000,
            "hot_bytes_window_seconds": 60.0,
        },
        tmp_path,
    )
    t = [0.0]

    def fake_monotonic():
        return t[0]

    with mock.patch("control_plane.retention.time.monotonic", fake_monotonic):
        # Build classify_pressure state up to HIGH_WATER (1 sample).
        t[0] = 0.0
        assert policy.classify_pressure(1500, 0, 100) == Pressure.HIGH_WATER
        # Call the shim with a synthetic 60s history -> True.
        history = [(t, 2000, 0) for t in range(0, 65, 5)]
        assert policy.sustained_pressure(2000, 0, 65.0, history) is True
        # The ring buffer must still reflect the original classify_pressure
        # state, not the synthetic history; another classify call at the
        # same monotonic time must still be HIGH_WATER (not SUSTAINED).
        t[0] = 0.0
        assert policy.classify_pressure(1500, 0, 100) == Pressure.HIGH_WATER


import threading

def test_classify_pressure_threadsafe_under_concurrent_load(tmp_path):
    """8 threads x 2k iters of classify+windowed-read; deque must remain
    structurally valid (1..64 3-tuples) and no exceptions."""
    rc = RetentionController({}, tmp_path)
    barrier = threading.Barrier(8)
    errors: list[Exception] = []
    def hammer():
        barrier.wait()
        try:
            for _ in range(2000):
                rc.classify_pressure(rc.high_water_bytes() + 1, 0, 1000)
                rc._sustained_fires(1000)
        except Exception as exc:  # noqa: BLE001
            errors.append(exc)
    ts = [threading.Thread(target=hammer) for _ in range(8)]
    for t in ts: t.start()
    for t in ts: t.join()
    assert errors == []
    assert 1 <= len(rc._samples) <= 64
    for entry in rc._samples:
        assert isinstance(entry, tuple) and len(entry) == 3
