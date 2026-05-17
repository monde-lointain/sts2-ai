"""Tests for upstream_sync.artifact_filters."""

from __future__ import annotations

import pytest

from upstream_sync.artifact_filters import (
    ARTIFACT_ALLOWLIST,
    ARTIFACT_REGEX,
    is_artifact,
)

# ---------- ARTIFACT_REGEX ----------


def test_regex_matches_y_prefix():
    assert ARTIFACT_REGEX.match("--y__GlobalClass.cs") is not None


def test_regex_matches_z_prefix():
    assert ARTIFACT_REGEX.match("--z__GlobalEnums.cs") is not None


def test_regex_rejects_normal_file():
    assert ARTIFACT_REGEX.match("Strike.cs") is None


def test_regex_rejects_partial_prefix():
    assert ARTIFACT_REGEX.match("-y__Something.cs") is None


def test_regex_requires_cs_suffix():
    assert ARTIFACT_REGEX.match("--y__Something.txt") is None


def test_regex_case_sensitive_prefix():
    # Must be lowercase y or z
    assert ARTIFACT_REGEX.match("--Y__Something.cs") is None


# ---------- ARTIFACT_ALLOWLIST ----------


def test_allowlist_is_frozenset():
    assert isinstance(ARTIFACT_ALLOWLIST, frozenset)


def test_allowlist_contains_known_artifacts():
    assert "--y__GlobalClass" in ARTIFACT_ALLOWLIST
    assert "--z__GlobalClass" in ARTIFACT_ALLOWLIST


def test_allowlist_does_not_contain_normal_names():
    assert "Strike" not in ARTIFACT_ALLOWLIST
    assert "LeafSlime" not in ARTIFACT_ALLOWLIST


# ---------- is_artifact ----------


@pytest.mark.parametrize(
    "path",
    [
        "--y__GlobalClass.cs",
        "--z__GlobalEnums.cs",
        "--y__SomeSynthetic.cs",
        # With directory prefix — filename still matches regex
        "src/Core/--z__GlobalClass.cs",
    ],
)
def test_is_artifact_true_for_regex_match(path: str):
    assert is_artifact(path) is True


@pytest.mark.parametrize(
    "path",
    [
        "src/Core/Models/Monsters/LeafSlime.cs",
        "src/Core/Combat/AbstractGameAction.cs",
        "Strike.cs",
        "GlobalClass.cs",  # no artifact prefix
    ],
)
def test_is_artifact_false_for_normal_files(path: str):
    assert is_artifact(path) is False


def test_is_artifact_true_for_allowlist_match():
    # The allowlist stem matches even if regex doesn't fire (belt-and-suspenders).
    # Build a path whose filename stem is in the allowlist.
    for stem in ARTIFACT_ALLOWLIST:
        path = f"{stem}.cs"
        assert is_artifact(path) is True


def test_is_artifact_false_for_no_extension():
    assert is_artifact("some_file") is False
