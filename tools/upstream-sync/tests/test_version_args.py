"""Tests for upstream_sync.version_args: --version + --version-from-buildid resolution."""

from __future__ import annotations

from datetime import UTC, datetime

import pytest

from upstream_sync import version_args
from upstream_sync.version_args import VersionSpec, parse_version_spec

# ---- Happy paths -------------------------------------------------------------


def test_explicit_version_returns_non_synthetic_spec():
    spec = parse_version_spec("v0.105.1", False, None)
    assert spec == VersionSpec(raw="v0.105.1", is_synthetic=False)


def test_synthetic_version_from_buildid_with_injected_today():
    today = datetime(2026, 5, 14, tzinfo=UTC)
    spec = parse_version_spec(None, True, "22823976", today=today)
    assert spec == VersionSpec(raw="build-22823976-2026-05-14", is_synthetic=True)


def test_four_segment_explicit_version_accepted():
    spec = parse_version_spec("v0.105.1.2", False, None)
    assert spec == VersionSpec(raw="v0.105.1.2", is_synthetic=False)


def test_today_defaults_to_wall_clock_when_omitted():
    """When `today` is omitted, function uses datetime.now(UTC) — must parse OK."""
    spec = parse_version_spec(None, True, "1")
    assert spec.is_synthetic is True
    assert spec.raw.startswith("build-1-")
    # YYYY-MM-DD suffix: 10 chars after "build-1-"
    suffix = spec.raw[len("build-1-") :]
    assert len(suffix) == 10
    datetime.strptime(suffix, "%Y-%m-%d")  # raises if not valid date


# ---- Mutual-exclusion / required-arg errors ---------------------------------


def test_both_explicit_and_buildid_is_mutually_exclusive():
    with pytest.raises(ValueError, match="mutually exclusive"):
        parse_version_spec("v0.105.1", True, "22823976")


def test_neither_supplied_raises_required():
    with pytest.raises(ValueError, match="required"):
        parse_version_spec(None, False, None)


def test_synthetic_without_buildid_raises():
    with pytest.raises(ValueError):
        parse_version_spec(None, True, None)


# ---- Validation errors ------------------------------------------------------


def test_explicit_version_missing_v_prefix_rejected():
    with pytest.raises(ValueError):
        parse_version_spec("0.105.1", False, None)


def test_explicit_version_too_few_segments_rejected():
    with pytest.raises(ValueError):
        parse_version_spec("v0.10", False, None)


def test_buildid_with_non_digits_rejected():
    with pytest.raises(ValueError):
        parse_version_spec(None, True, "abc123")


# ---- Public-surface manifest ------------------------------------------------


def test_public_surface():
    """Lock the public surface so accidental additions are caught."""
    assert hasattr(version_args, "VersionSpec")
    assert hasattr(version_args, "parse_version_spec")
    # VersionSpec is a frozen dataclass with exactly these fields.
    fields = set(VersionSpec.__dataclass_fields__)
    assert fields == {"raw", "is_synthetic"}


def test_version_spec_is_frozen():
    spec = parse_version_spec("v0.105.1", False, None)
    with pytest.raises(Exception):  # FrozenInstanceError
        spec.raw = "v9.9.9"  # type: ignore[misc]
