"""Tests for upstream_sync.patch_notes: Steam News fetch + BBCode parse."""

from __future__ import annotations

import json
import urllib.error
from io import BytesIO
from pathlib import Path

import pytest

from upstream_sync import patch_notes
from upstream_sync.patch_notes import (
    STEAM_NEWS_API,
    ParsedNote,
    PatchNote,
    fetch_patch_notes,
    parse_bbcode,
)

FIXTURE_PATH = Path(__file__).parent / "fixtures" / "patch_notes.sample.json"


def _fixture_bytes() -> bytes:
    return FIXTURE_PATH.read_bytes()


class _MockResponse:
    """Minimal urllib.response stand-in: only needs `.read()` -> bytes."""

    def __init__(self, payload: bytes, status: int = 200):
        self._payload = payload
        self.status = status

    def read(self) -> bytes:
        return self._payload

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False


def _make_urlopen(payload: bytes):
    """Build a one-shot _urlopen mock that always returns payload."""
    calls: list[str] = []

    def _urlopen(url, timeout=None):
        calls.append(str(url))
        return _MockResponse(payload)

    _urlopen.calls = calls  # type: ignore[attr-defined]
    return _urlopen


# ---------------------------------------------------------------------------
# Public surface
# ---------------------------------------------------------------------------


def test_steam_news_api_url_template():
    """STEAM_NEWS_API must be a formatable URL template."""
    url = STEAM_NEWS_API.format(appid="2868840", count=20)
    assert "appid=2868840" in url
    assert "count=20" in url
    assert "format=json" in url


def test_patch_note_is_frozen_dataclass():
    note = PatchNote(gid="g", title="t", date=1, contents="c", url="u", version_hint=None)
    with pytest.raises(Exception):
        note.title = "other"  # type: ignore[misc]


def test_parsed_note_is_frozen_dataclass():
    parsed = ParsedNote(sections=[], entities=[], items=[])
    with pytest.raises(Exception):
        parsed.sections = ["x"]  # type: ignore[misc]


# ---------------------------------------------------------------------------
# fetch_patch_notes — filtering, parsing, version_hint
# ---------------------------------------------------------------------------


def test_fetch_filters_to_steam_patchnotes_only():
    """feed_type=0 (external) is dropped; only feed_type=1 + patchnotes tag kept."""
    notes = fetch_patch_notes(_urlopen=_make_urlopen(_fixture_bytes()))
    assert len(notes) == 2
    # API response order preserved (newest first)
    assert notes[0].gid == "1832065502816737"
    assert notes[1].gid == "1832065502813730"


def test_fetch_populates_patch_note_fields():
    notes = fetch_patch_notes(_urlopen=_make_urlopen(_fixture_bytes()))
    first = notes[0]
    assert first.title == "Beta Hotfix Notes - v0.105.1"
    assert first.date == 1778296466
    assert first.url == "https://example.steam/news/1832065502816737"
    assert "Aeonglass" in first.contents


def test_fetch_extracts_version_hint_from_title():
    notes = fetch_patch_notes(_urlopen=_make_urlopen(_fixture_bytes()))
    assert notes[0].version_hint == "v0.105.1"
    assert notes[1].version_hint == "v0.105.0"


def test_fetch_version_hint_none_when_no_pattern():
    """Title without v\\d+.\\d+.\\d+ → version_hint is None."""
    payload = json.dumps(
        {
            "appnews": {
                "appid": 2868840,
                "newsitems": [
                    {
                        "gid": "x",
                        "title": "Some update without version",
                        "url": "u",
                        "contents": "",
                        "date": 1,
                        "feed_type": 1,
                        "tags": ["patchnotes"],
                    }
                ],
                "count": 1,
            }
        }
    ).encode()
    notes = fetch_patch_notes(_urlopen=_make_urlopen(payload))
    assert len(notes) == 1
    assert notes[0].version_hint is None


def test_fetch_url_uses_app_id_and_count():
    urlopen = _make_urlopen(_fixture_bytes())
    fetch_patch_notes(app_id="9999", count=7, _urlopen=urlopen)
    assert urlopen.calls  # type: ignore[attr-defined]
    called_url = urlopen.calls[0]  # type: ignore[attr-defined]
    assert "appid=9999" in called_url
    assert "count=7" in called_url


# ---------------------------------------------------------------------------
# Caching
# ---------------------------------------------------------------------------


def test_cache_write_then_read(tmp_path):
    """First call writes cache; second call reads from cache (no urlopen invoked)."""
    urlopen1 = _make_urlopen(_fixture_bytes())
    first = fetch_patch_notes(cache_dir=tmp_path, _urlopen=urlopen1)
    assert urlopen1.calls  # type: ignore[attr-defined]
    assert len(first) == 2
    # Cache file present
    cache_files = list(tmp_path.glob("patch_notes_*.json"))
    assert cache_files, "expected a cache file to be written"

    # Second call: _urlopen must not be called
    def _exploding_urlopen(url, timeout=None):
        raise AssertionError("urlopen should not be called on cache hit")

    second = fetch_patch_notes(cache_dir=tmp_path, _urlopen=_exploding_urlopen)
    assert len(second) == 2
    assert second[0].gid == first[0].gid


def test_cache_miss_then_fetch(tmp_path):
    """No cache file present → falls through to urlopen."""
    urlopen = _make_urlopen(_fixture_bytes())
    notes = fetch_patch_notes(cache_dir=tmp_path, count=20, _urlopen=urlopen)
    assert urlopen.calls  # type: ignore[attr-defined]
    assert len(notes) == 2


def test_cache_key_is_count(tmp_path):
    """Different `count` values must use distinct cache files."""
    urlopen_a = _make_urlopen(_fixture_bytes())
    fetch_patch_notes(cache_dir=tmp_path, count=5, _urlopen=urlopen_a)
    urlopen_b = _make_urlopen(_fixture_bytes())
    fetch_patch_notes(cache_dir=tmp_path, count=10, _urlopen=urlopen_b)
    files = sorted(p.name for p in tmp_path.glob("patch_notes_*.json"))
    assert len(files) == 2


# ---------------------------------------------------------------------------
# Retry + graceful degradation
# ---------------------------------------------------------------------------


def _http_error(code: int) -> urllib.error.HTTPError:
    return urllib.error.HTTPError(
        url="x",
        code=code,
        msg="err",
        hdrs=None,
        fp=BytesIO(b""),  # type: ignore[arg-type]
    )


def test_retry_recovers_after_429(monkeypatch):
    """429 then 200 → succeeds after retry; verifies backoff is invoked."""
    sleeps: list[float] = []
    monkeypatch.setattr(patch_notes.time, "sleep", sleeps.append)

    payload = _fixture_bytes()
    state = {"attempts": 0}

    def _flaky(url, timeout=None):
        state["attempts"] += 1
        if state["attempts"] == 1:
            raise _http_error(429)
        return _MockResponse(payload)

    notes = fetch_patch_notes(_urlopen=_flaky)
    assert len(notes) == 2
    assert state["attempts"] == 2
    assert sleeps == [1]  # backoff after first failure


def test_retry_recovers_after_5xx(monkeypatch):
    monkeypatch.setattr(patch_notes.time, "sleep", lambda s: None)
    payload = _fixture_bytes()
    state = {"attempts": 0}

    def _flaky(url, timeout=None):
        state["attempts"] += 1
        if state["attempts"] < 3:
            raise _http_error(503)
        return _MockResponse(payload)

    notes = fetch_patch_notes(_urlopen=_flaky)
    assert len(notes) == 2
    assert state["attempts"] == 3


def test_three_consecutive_failures_return_empty(monkeypatch, capsys):
    monkeypatch.setattr(patch_notes.time, "sleep", lambda s: None)
    attempts = {"n": 0}

    def _always_500(url, timeout=None):
        attempts["n"] += 1
        raise _http_error(500)

    notes = fetch_patch_notes(_urlopen=_always_500)
    assert notes == []
    assert attempts["n"] == 3
    err = capsys.readouterr().err
    assert "warn" in err.lower() or "fail" in err.lower() or "error" in err.lower()


def test_unauthenticated_4xx_returns_empty(monkeypatch, capsys):
    monkeypatch.setattr(patch_notes.time, "sleep", lambda s: None)

    def _forbidden(url, timeout=None):
        raise _http_error(403)

    notes = fetch_patch_notes(_urlopen=_forbidden)
    assert notes == []
    err = capsys.readouterr().err
    assert err  # warning emitted


def test_unauthenticated_401_returns_empty(monkeypatch, capsys):
    monkeypatch.setattr(patch_notes.time, "sleep", lambda s: None)

    def _unauth(url, timeout=None):
        raise _http_error(401)

    notes = fetch_patch_notes(_urlopen=_unauth)
    assert notes == []


def test_url_error_treated_as_network_failure(monkeypatch):
    monkeypatch.setattr(patch_notes.time, "sleep", lambda s: None)
    attempts = {"n": 0}

    def _url_error(url, timeout=None):
        attempts["n"] += 1
        raise urllib.error.URLError("dns fail")

    notes = fetch_patch_notes(_urlopen=_url_error)
    assert notes == []
    assert attempts["n"] == 3


# ---------------------------------------------------------------------------
# parse_bbcode
# ---------------------------------------------------------------------------


_V0_105_0_BODY = (
    "[h2]CONTENT & BALANCE:[/h2]\n"
    "[b]Silent:[/b]\n"
    "[list]\n"
    "[*]Nerfed [b]Blade of Ink[/b] card: damage decreased from +2 -> +1\n"
    "[/list]\n"
    "[b]Defect:[/b]\n"
    "[list]\n"
    "[*]Buffed [b]Hyperbeam[/b]: damage increased from 26(34) -> 28(36)\n"
    "[/list]\n"
    "[h2]BUG FIXES:[/h2]\n"
    "[b]Enemies:[/b]\n"
    "[list]\n"
    "[*]Fixed [b]Kaiser Crab[/b] appearing if you load into an already completed boss room\n"
    "[/list]"
)


def test_parse_bbcode_extracts_sections():
    """Spec edge case #8: trailing colons in [h2] stripped from section names."""
    parsed = parse_bbcode(_V0_105_0_BODY)
    assert parsed.sections == ["CONTENT & BALANCE", "BUG FIXES"]


def test_parse_bbcode_entities_tied_to_sections():
    """Spec edge case #9: entities paired with current section, order matters."""
    parsed = parse_bbcode(_V0_105_0_BODY)
    assert parsed.entities == [
        ("CONTENT & BALANCE", "Silent:"),
        ("CONTENT & BALANCE", "Blade of Ink"),
        ("CONTENT & BALANCE", "Defect:"),
        ("CONTENT & BALANCE", "Hyperbeam"),
        ("BUG FIXES", "Enemies:"),
        ("BUG FIXES", "Kaiser Crab"),
    ]


def test_parse_bbcode_items_tied_to_sections():
    parsed = parse_bbcode(_V0_105_0_BODY)
    # Three [*] entries total, each under a section.
    assert len(parsed.items) == 3
    sections = [s for s, _ in parsed.items]
    assert sections == ["CONTENT & BALANCE", "CONTENT & BALANCE", "BUG FIXES"]
    assert "Blade of Ink" in parsed.items[0][1]
    assert "Hyperbeam" in parsed.items[1][1]
    assert "Kaiser Crab" in parsed.items[2][1]


def test_parse_bbcode_entity_before_section_uses_empty_section():
    body = "[b]Orphan[/b]\n[h2]Section[/h2]\n[b]Inside[/b]"
    parsed = parse_bbcode(body)
    assert parsed.entities[0] == ("", "Orphan")
    assert parsed.entities[1] == ("Section", "Inside")


def test_parse_bbcode_tolerates_unclosed_tags():
    """Malformed BBCode must not raise; best-effort extraction."""
    parsed = parse_bbcode("[b]Strike")  # unclosed
    # We don't dictate whether Strike is recovered, only that it doesn't crash
    assert isinstance(parsed, ParsedNote)


def test_parse_bbcode_case_insensitive():
    body = "[H2]Upper[/H2]\n[B]Bold[/B]\n[LIST]\n[*]Item\n[/LIST]"
    parsed = parse_bbcode(body)
    assert parsed.sections == ["Upper"]
    assert parsed.entities == [("Upper", "Bold")]
    assert len(parsed.items) == 1
    assert parsed.items[0][0] == "Upper"
    assert "Item" in parsed.items[0][1]


def test_parse_bbcode_empty_content():
    parsed = parse_bbcode("")
    assert parsed.sections == []
    assert parsed.entities == []
    assert parsed.items == []


def test_parse_bbcode_multiple_items_in_one_list():
    body = "[h2]Section[/h2]\n[list]\n[*]First item\n[*]Second item\n[*]Third item\n[/list]"
    parsed = parse_bbcode(body)
    assert len(parsed.items) == 3
    assert all(s == "Section" for s, _ in parsed.items)
    assert [t.strip() for _, t in parsed.items] == ["First item", "Second item", "Third item"]
