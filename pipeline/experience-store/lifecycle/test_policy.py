"""Unit tests for LifecyclePolicy + validate() (S0.C.gamma)."""

from __future__ import annotations

import json

import pytest

from lifecycle.policy import (
    DEFAULT_COLD_TIER_ENABLED,
    DEFAULT_HOT_HIGH_WATER_BYTES,
    DEFAULT_HOT_OVERFLOW_BYTES,
    DEFAULT_MAX_AGE_SECONDS,
    DEFAULT_TICK_INTERVAL_SECONDS,
    POLICY_DIR,
    POLICY_FILE,
    LifecyclePolicy,
    default_policy,
    validate,
)


def test_default_policy_matches_spec(tmp_path):
    """Spec lines 32-41: defaults per Q3-ADR-007 + Phase-1A flags."""
    pol = LifecyclePolicy(tmp_path)
    assert pol.hot_high_water_bytes() == 50 * 1024**3
    assert pol.hot_overflow_bytes() == 100 * 1024**3
    assert pol.tick_interval_seconds() == 60
    assert pol.cold_tier_enabled() is False
    assert pol.max_age_seconds() is None
    # Cross-check constants.
    assert pol.hot_high_water_bytes() == DEFAULT_HOT_HIGH_WATER_BYTES
    assert pol.hot_overflow_bytes() == DEFAULT_HOT_OVERFLOW_BYTES
    assert pol.tick_interval_seconds() == DEFAULT_TICK_INTERVAL_SECONDS
    assert pol.cold_tier_enabled() is DEFAULT_COLD_TIER_ENABLED
    assert pol.max_age_seconds() is DEFAULT_MAX_AGE_SECONDS


def test_policy_file_written_on_first_init(tmp_path):
    path = tmp_path / POLICY_DIR / POLICY_FILE
    assert not path.exists()
    LifecyclePolicy(tmp_path)
    assert path.exists()
    persisted = json.loads(path.read_text(encoding="utf-8"))
    assert persisted["hot_high_water_bytes"] == DEFAULT_HOT_HIGH_WATER_BYTES
    assert persisted["cold_tier_enabled"] is False


def test_policy_reinit_does_not_overwrite_existing(tmp_path):
    """Operator override survives restart: existing values win."""
    first = LifecyclePolicy(tmp_path)
    new = {
        "hot_high_water_bytes": 10 * 1024**3,
        "hot_overflow_bytes": 20 * 1024**3,
        "tick_interval_seconds": 5,
        "cold_tier_enabled": False,
        "max_age_seconds": None,
    }
    first.update(new)
    # Second LifecyclePolicy on the same dir loads on-disk values.
    second = LifecyclePolicy(tmp_path)
    assert second.hot_high_water_bytes() == 10 * 1024**3
    assert second.hot_overflow_bytes() == 20 * 1024**3
    assert second.tick_interval_seconds() == 5


def test_update_persists_atomically(tmp_path):
    pol = LifecyclePolicy(tmp_path)
    pol.update(
        {
            "hot_high_water_bytes": 1024,
            "hot_overflow_bytes": 2048,
            "tick_interval_seconds": 1,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    persisted = json.loads(pol.path.read_text(encoding="utf-8"))
    assert persisted["hot_high_water_bytes"] == 1024
    assert persisted["hot_overflow_bytes"] == 2048
    assert pol.hot_high_water_bytes() == 1024


def test_validate_rejects_missing_keys():
    with pytest.raises(ValueError, match="missing keys"):
        validate({"hot_high_water_bytes": 1})


def test_validate_rejects_negative_thresholds():
    with pytest.raises(ValueError, match="hot_high_water_bytes"):
        validate(
            {
                "hot_high_water_bytes": 0,
                "hot_overflow_bytes": 100,
                "tick_interval_seconds": 1,
                "cold_tier_enabled": False,
                "max_age_seconds": None,
            }
        )


def test_validate_rejects_overflow_below_high_water():
    """Spec invariant: overflow must be >= high_water."""
    with pytest.raises(ValueError, match="must be >="):
        validate(
            {
                "hot_high_water_bytes": 100,
                "hot_overflow_bytes": 50,
                "tick_interval_seconds": 1,
                "cold_tier_enabled": False,
                "max_age_seconds": None,
            }
        )


def test_validate_rejects_non_bool_cold_flag():
    with pytest.raises(ValueError, match="cold_tier_enabled must be bool"):
        validate(
            {
                "hot_high_water_bytes": 100,
                "hot_overflow_bytes": 200,
                "tick_interval_seconds": 1,
                "cold_tier_enabled": "no",
                "max_age_seconds": None,
            }
        )


def test_validate_accepts_max_age_null():
    pol = validate(
        {
            "hot_high_water_bytes": 100,
            "hot_overflow_bytes": 200,
            "tick_interval_seconds": 1,
            "cold_tier_enabled": False,
            "max_age_seconds": None,
        }
    )
    assert pol["max_age_seconds"] is None


def test_validate_rejects_zero_max_age():
    with pytest.raises(ValueError, match="max_age_seconds"):
        validate(
            {
                "hot_high_water_bytes": 100,
                "hot_overflow_bytes": 200,
                "tick_interval_seconds": 1,
                "cold_tier_enabled": False,
                "max_age_seconds": 0,
            }
        )


def test_default_policy_returns_independent_dict():
    a = default_policy()
    b = default_policy()
    a["hot_high_water_bytes"] = 1
    assert b["hot_high_water_bytes"] == DEFAULT_HOT_HIGH_WATER_BYTES
