"""Unit tests for RetentionPolicy (S0.B.gamma)."""

from __future__ import annotations

import json

from control_plane.retention import (
    DEFAULT_HOT_HIGH_WATER_BYTES,
    DEFAULT_HOT_OVERFLOW_BYTES,
    DEFAULT_INGEST_QUEUE_CAPACITY,
    POLICY_DIR,
    POLICY_FILE,
    RetentionPolicy,
)


def test_default_thresholds_match_q3_adr_007(tmp_path):
    policy = RetentionPolicy({}, tmp_path)
    assert policy.high_water_bytes() == 50 * 1024**3
    assert policy.overflow_bytes() == 100 * 1024**3
    assert policy.ingest_queue_capacity() == 10000
    assert policy.high_water_bytes() == DEFAULT_HOT_HIGH_WATER_BYTES
    assert policy.overflow_bytes() == DEFAULT_HOT_OVERFLOW_BYTES
    assert policy.ingest_queue_capacity() == DEFAULT_INGEST_QUEUE_CAPACITY


def test_config_override_persisted(tmp_path):
    policy = RetentionPolicy(
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
    RetentionPolicy({}, tmp_path)
    assert policy_path.exists()
    persisted = json.loads(policy_path.read_text(encoding="utf-8"))
    assert persisted["hot_high_water_bytes"] == DEFAULT_HOT_HIGH_WATER_BYTES


def test_policy_json_not_overwritten_on_reinit(tmp_path):
    # First init writes custom values.
    RetentionPolicy(
        {
            "hot_high_water_bytes": 7 * 1024**3,
            "hot_overflow_bytes": 14 * 1024**3,
            "ingest_queue_capacity": 7000,
        },
        tmp_path,
    )
    # Re-init with different config keys; persisted values win.
    policy = RetentionPolicy(
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


def test_sustained_pressure_hot_bytes_60s_returns_true(tmp_path):
    policy = RetentionPolicy({"hot_high_water_bytes": 1000}, tmp_path)
    high = 2000  # above 1000 threshold
    # Synthesize a continuous run of 70 seconds, sample every 5 s.
    history = [(t, high, 0) for t in range(0, 65, 5)]
    now_ts = 65.0
    assert policy.sustained_pressure(high, 0, now_ts, history) is True


def test_sustained_pressure_short_spike_returns_false(tmp_path):
    policy = RetentionPolicy({"hot_high_water_bytes": 1000}, tmp_path)
    high = 2000
    # Only 30 seconds of overage — under the 60 s window.
    history = [(t, high, 0) for t in range(0, 30, 5)]
    now_ts = 30.0
    assert policy.sustained_pressure(high, 0, now_ts, history) is False


def test_sustained_pressure_below_threshold_returns_false(tmp_path):
    policy = RetentionPolicy({"hot_high_water_bytes": 1000}, tmp_path)
    history = [(t, 500, 0) for t in range(0, 120, 5)]
    now_ts = 120.0
    assert policy.sustained_pressure(500, 0, now_ts, history) is False


def test_sustained_pressure_queue_30s_returns_true(tmp_path):
    policy = RetentionPolicy(
        {"hot_high_water_bytes": 1_000_000, "ingest_queue_capacity": 100},
        tmp_path,
    )
    # Threshold is 0.8 * 100 = 80; depth=90 is above.
    history = [(t, 0, 90) for t in range(0, 35, 5)]
    now_ts = 35.0
    assert policy.sustained_pressure(0, 90, now_ts, history) is True


def test_sustained_pressure_queue_short_spike_returns_false(tmp_path):
    policy = RetentionPolicy(
        {"hot_high_water_bytes": 1_000_000, "ingest_queue_capacity": 100},
        tmp_path,
    )
    # Only 15 seconds of overage — under 30 s window.
    history = [(t, 0, 90) for t in range(0, 15, 5)]
    now_ts = 15.0
    assert policy.sustained_pressure(0, 90, now_ts, history) is False


def test_sustained_pressure_easing_resets_run(tmp_path):
    """Per Q3-ADR-008's hysteresis requirement (spec test #2 in retention)."""
    policy = RetentionPolicy({"hot_high_water_bytes": 1000}, tmp_path)
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
    policy = RetentionPolicy({"hot_high_water_bytes": 1000}, tmp_path)
    # No prior samples, just the current observation -> no window covered.
    assert policy.sustained_pressure(2000, 0, 10.0, []) is False
