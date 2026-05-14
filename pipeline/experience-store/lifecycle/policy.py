"""Lifecycle policy.json IO + validation (Q3-ADR-007 / Q3-ADR-008 defaults).

Persists ``<data_dir>/lifecycle/policy.json`` atomically via temp + rename.
Defaults match the spec block at modules/lifecycle.md lines 32-41:

- ``hot_high_water_bytes`` = 50 GiB
- ``hot_overflow_bytes``   = 100 GiB
- ``tick_interval_seconds`` = 60
- ``cold_tier_enabled``     = false (Phase-1A)
- ``max_age_seconds``       = null (Phase-2+ optional)
"""

from __future__ import annotations

import json
import pathlib
from typing import Any

POLICY_DIR = "lifecycle"
POLICY_FILE = "policy.json"

# Q3-ADR-007 hot-tier thresholds.
DEFAULT_HOT_HIGH_WATER_BYTES = 50 * 1024**3
DEFAULT_HOT_OVERFLOW_BYTES = 100 * 1024**3
# Spec line 37.
DEFAULT_TICK_INTERVAL_SECONDS = 60
# Phase-1A cold-tier-disabled posture.
DEFAULT_COLD_TIER_ENABLED = False
DEFAULT_MAX_AGE_SECONDS: int | None = None


_REQUIRED_KEYS: tuple[str, ...] = (
    "hot_high_water_bytes",
    "hot_overflow_bytes",
    "tick_interval_seconds",
    "cold_tier_enabled",
    "max_age_seconds",
)


def default_policy() -> dict[str, Any]:
    """Fresh dict matching the spec defaults (Q3-ADR-007 + Phase-1A flags)."""
    return {
        "hot_high_water_bytes": DEFAULT_HOT_HIGH_WATER_BYTES,
        "hot_overflow_bytes": DEFAULT_HOT_OVERFLOW_BYTES,
        "tick_interval_seconds": DEFAULT_TICK_INTERVAL_SECONDS,
        "cold_tier_enabled": DEFAULT_COLD_TIER_ENABLED,
        "max_age_seconds": DEFAULT_MAX_AGE_SECONDS,
    }


def validate(policy: dict[str, Any]) -> dict[str, Any]:
    """Type-check + range-check a candidate policy. Returns the normalized dict.

    Raises ``ValueError`` on missing keys, non-numeric thresholds, or
    high_water/overflow inversion. Validation is conservative so operator
    POSTs to ``/lifecycle/policy`` cannot silently corrupt the file.
    """
    if not isinstance(policy, dict):
        raise ValueError(f"policy must be a dict; got {type(policy).__name__}")

    missing = [k for k in _REQUIRED_KEYS if k not in policy]
    if missing:
        raise ValueError(f"policy missing keys: {', '.join(missing)}")

    high_water = policy["hot_high_water_bytes"]
    overflow = policy["hot_overflow_bytes"]
    tick_interval = policy["tick_interval_seconds"]
    cold_enabled = policy["cold_tier_enabled"]
    max_age = policy["max_age_seconds"]

    if not isinstance(high_water, int) or high_water <= 0:
        raise ValueError(
            f"hot_high_water_bytes must be positive int; got {high_water!r}"
        )
    if not isinstance(overflow, int) or overflow <= 0:
        raise ValueError(
            f"hot_overflow_bytes must be positive int; got {overflow!r}"
        )
    if overflow < high_water:
        raise ValueError(
            f"hot_overflow_bytes ({overflow}) must be >= "
            f"hot_high_water_bytes ({high_water})"
        )
    if not isinstance(tick_interval, (int, float)) or tick_interval <= 0:
        raise ValueError(
            f"tick_interval_seconds must be positive number; got {tick_interval!r}"
        )
    if not isinstance(cold_enabled, bool):
        raise ValueError(
            f"cold_tier_enabled must be bool; got {type(cold_enabled).__name__}"
        )
    if max_age is not None and (not isinstance(max_age, int) or max_age <= 0):
        raise ValueError(
            f"max_age_seconds must be positive int or null; got {max_age!r}"
        )

    return {
        "hot_high_water_bytes": int(high_water),
        "hot_overflow_bytes": int(overflow),
        "tick_interval_seconds": (
            int(tick_interval) if isinstance(tick_interval, int) else float(tick_interval)
        ),
        "cold_tier_enabled": bool(cold_enabled),
        "max_age_seconds": (None if max_age is None else int(max_age)),
    }


class LifecyclePolicy:
    """Loads / persists ``<data_dir>/lifecycle/policy.json`` with atomic writes.

    On first init the file is created with spec defaults. Subsequent inits
    load the on-disk values (operator override wins after first boot).
    ``update(...)`` validates, replaces in-memory state, and rewrites the
    file atomically (temp + os.replace).
    """

    def __init__(self, data_dir: pathlib.Path) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._policy_dir = self._data_dir / POLICY_DIR
        self._policy_dir.mkdir(parents=True, exist_ok=True)
        self._path = self._policy_dir / POLICY_FILE

        if self._path.exists():
            with self._path.open("r", encoding="utf-8") as handle:
                self._policy = validate(json.load(handle))
        else:
            self._policy = default_policy()
            self._persist()

    @property
    def path(self) -> pathlib.Path:
        return self._path

    def as_dict(self) -> dict[str, Any]:
        """Return a deep-enough copy of the current policy."""
        return dict(self._policy)

    def hot_high_water_bytes(self) -> int:
        return int(self._policy["hot_high_water_bytes"])

    def hot_overflow_bytes(self) -> int:
        return int(self._policy["hot_overflow_bytes"])

    def tick_interval_seconds(self) -> float:
        return float(self._policy["tick_interval_seconds"])

    def cold_tier_enabled(self) -> bool:
        return bool(self._policy["cold_tier_enabled"])

    def max_age_seconds(self) -> int | None:
        val = self._policy["max_age_seconds"]
        return None if val is None else int(val)

    def update(self, policy: dict[str, Any]) -> dict[str, Any]:
        """Validate + atomically persist a replacement policy. Returns new dict."""
        self._policy = validate(policy)
        self._persist()
        return self.as_dict()

    def _persist(self) -> None:
        tmp = self._path.with_suffix(".json.tmp")
        with tmp.open("w", encoding="utf-8") as handle:
            json.dump(self._policy, handle, indent=2, sort_keys=True)
            handle.write("\n")
        tmp.replace(self._path)
