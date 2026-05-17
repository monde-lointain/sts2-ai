"""Tests for upstream_sync.content_allowlist: DLL-based game-content allowlist."""

from __future__ import annotations

import json

from upstream_sync.content_allowlist import (
    _cache_path_for_sha,
    _load_cache,
    _save_cache,
    build_allowlist,
    load_allowlist,
)
from upstream_sync.correlate import entity_in_allowlist

# ---------------------------------------------------------------------------
# entity_in_allowlist (via correlate module public surface)
# ---------------------------------------------------------------------------


def test_empty_allowlist_passes_all():
    """Empty allowlist disables filtering."""
    assert entity_in_allowlist("AnythingAtAll", frozenset()) is True
    assert entity_in_allowlist("UILabel", frozenset()) is True


def test_exact_match():
    al = frozenset({"Untouchable", "Inky", "NightmarePower"})
    assert entity_in_allowlist("Untouchable", al) is True
    assert entity_in_allowlist("Inky", al) is True


def test_case_insensitive_match():
    al = frozenset({"Untouchable"})
    assert entity_in_allowlist("untouchable", al) is True
    assert entity_in_allowlist("UNTOUCHABLE", al) is True


def test_entity_starts_with_allowlist_name():
    """'Inky enchantment' should pass when 'Inky' is in allowlist."""
    al = frozenset({"Inky"})
    assert entity_in_allowlist("Inky enchantment", al) is True
    assert entity_in_allowlist("Inky card", al) is True


def test_allowlist_name_starts_with_entity():
    """'Untouchable' entity when allowlist has 'UntouchablePlus'."""
    al = frozenset({"UntouchablePlus"})
    assert entity_in_allowlist("Untouchable", al) is True


def test_non_matching_entity_rejected():
    al = frozenset({"Untouchable", "Inky"})
    assert entity_in_allowlist("Artwork Credit", al) is False
    assert entity_in_allowlist("UI", al) is False
    assert entity_in_allowlist("The", al) is False


def test_trailing_colon_stripped():
    al = frozenset({"SilentStrike"})
    assert entity_in_allowlist("SilentStrike:", al) is True


def test_empty_entity_passes():
    """Empty entity always passes (no info to filter on)."""
    al = frozenset({"Something"})
    assert entity_in_allowlist("", al) is True
    assert entity_in_allowlist("  ", al) is True


# ---------------------------------------------------------------------------
# build_allowlist: DLL absent → empty frozenset (CI path)
# ---------------------------------------------------------------------------


def test_build_allowlist_absent_dll_returns_empty(tmp_path, capsys):
    """When DLL doesn't exist, returns empty frozenset and logs warning."""
    fake_dll = tmp_path / "nonexistent.dll"
    result = build_allowlist(
        dll_path=fake_dll,
        cache_dir=tmp_path / "cache",
        _dll_sha256_override=None,
    )
    assert result == frozenset()
    err = capsys.readouterr().err
    assert "not found" in err.lower() or "disabled" in err.lower()


def test_build_allowlist_cache_hit_skips_dll(tmp_path):
    """Cache hit returns allowlist without reading DLL."""
    cache_dir = tmp_path / "cache"
    sha = "a" * 64
    names = frozenset({"Untouchable", "Inky", "NightmarePower"})
    _save_cache(cache_dir, sha, names)

    # Pass a nonexistent DLL path — should use cache instead
    fake_dll = tmp_path / "ghost.dll"
    result = build_allowlist(
        dll_path=fake_dll,
        cache_dir=cache_dir,
        _dll_sha256_override=sha,
    )
    assert result == names


def test_build_allowlist_cache_miss_then_scans_dll(tmp_path):
    """Cache miss → scans DLL bytes → writes cache."""
    cache_dir = tmp_path / "cache"
    # Synthesize a minimal DLL-like byte stream containing a known CamelCase name
    # with a known suffix (e.g. "UntouchableCard").
    dll_bytes = b"\x00" + b"UntouchableCard\x00" + b"Inky\x00" + b"junkword\x00" * 10
    dll_path = tmp_path / "sts2.dll"
    dll_path.write_bytes(dll_bytes)

    result = build_allowlist(dll_path=dll_path, cache_dir=cache_dir)
    # UntouchableCard ends with "Card" suffix — should be in result.
    assert "UntouchableCard" in result
    # Cache file should now exist.
    cache_files = list(cache_dir.glob("content-allowlist-*.json"))
    assert cache_files


def test_save_and_load_cache_roundtrip(tmp_path):
    """Cache write/read roundtrip is lossless."""
    sha = "b" * 64
    names = frozenset({"PowerX", "RelicY", "CardZ"})
    _save_cache(tmp_path, sha, names)
    loaded = _load_cache(tmp_path, sha)
    assert loaded == names


def test_cache_miss_wrong_sha(tmp_path):
    """Cache miss when sha doesn't match stored sha."""
    sha1 = "a" * 64
    sha2 = "b" * 64
    names = frozenset({"SomeCard"})
    _save_cache(tmp_path, sha1, names)
    result = _load_cache(tmp_path, sha2)
    assert result is None


def test_load_allowlist_returns_none_when_uncached(tmp_path):
    """load_allowlist returns None when no cache file exists."""
    result = load_allowlist(cache_dir=tmp_path, sha256="c" * 64)
    assert result is None


def test_build_allowlist_from_pin_json(tmp_path):
    """build_allowlist reads sha from upstream-pin.json when provided."""
    sha = "d" * 64
    names = frozenset({"TestCard"})
    cache_dir = tmp_path / "cache"
    _save_cache(cache_dir, sha, names)

    pin_json = tmp_path / "upstream-pin.json"
    pin_json.write_text(
        json.dumps({"pinned_dll_sha256": sha, "pinned_version": "v0.105.1"}),
        encoding="utf-8",
    )
    result = build_allowlist(
        dll_path=tmp_path / "ghost.dll",  # won't be read (cache hit)
        cache_dir=cache_dir,
        pin_json_path=pin_json,
    )
    assert result == names


# ---------------------------------------------------------------------------
# Cache file path helpers
# ---------------------------------------------------------------------------


def test_cache_path_for_sha_uses_sha7(tmp_path):
    path = _cache_path_for_sha(tmp_path, "abcdef1")
    assert path.name == "content-allowlist-abcdef1.json"
    assert path.parent == tmp_path
