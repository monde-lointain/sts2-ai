"""Steam News API fetcher + BBCode patch-note parser.

Provides:
    * ``fetch_patch_notes`` — pulls newest Steam announcements for a given
      ``appid``, filters to items tagged ``patchnotes`` whose ``feed_type``
      is ``1`` (i.e., Steam Community Announcements, not syndicated press).
      Supports on-disk caching (per ``count``), bounded HTTP retry, and
      graceful degradation to ``[]`` on persistent failure or unauthenticated
      4xx.
    * ``parse_bbcode`` — best-effort extractor for the BBCode body structure
      used in Mega Crit STS2 patch notes (``[h2]``, ``[b]``, ``[list]``/``[*]``).

The module imports only standard library modules so the upstream-sync tool
can run in a barebones Python environment.
"""

from __future__ import annotations

import json
import re
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

STEAM_NEWS_API = (
    "https://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/"
    "?appid={appid}&count={count}&format=json"
)

# HTTP retry tuning. Three attempts on transient 429/5xx; exponential 1s/4s/16s
# (we only sleep *between* attempts, so two sleeps for three attempts).
_RETRY_ATTEMPTS = 3
_RETRY_BACKOFF_SECONDS = (1, 4, 16)
_RETRY_STATUSES = {429, 500, 502, 503, 504}
_UNAUTH_STATUSES = {401, 403}

_VERSION_HINT_RE = re.compile(r"v\d+\.\d+\.\d+")


@dataclass(frozen=True)
class PatchNote:
    """A single Steam Community Announcement tagged as patch notes."""

    gid: str
    title: str
    date: int  # epoch seconds
    contents: str  # raw BBCode body
    url: str
    version_hint: str | None  # e.g. "v0.105.1" if parseable from title


@dataclass(frozen=True)
class ParsedNote:
    """Structured view of a PatchNote's BBCode body."""

    sections: list[str]  # [h2] headings in document order
    entities: list[tuple[str, str]]  # (section_or_empty, entity_name)
    items: list[tuple[str, str]]  # (section_or_empty, item_text)


# ---------------------------------------------------------------------------
# Fetch
# ---------------------------------------------------------------------------


def _cache_path(cache_dir: Path, count: int) -> Path:
    return cache_dir / f"patch_notes_{count}.json"


def _filter_and_build(payload: dict) -> list[PatchNote]:
    """Filter raw Steam payload to patch-note items; build PatchNote list."""
    items = payload.get("appnews", {}).get("newsitems", [])
    notes: list[PatchNote] = []
    for item in items:
        tags = item.get("tags") or []
        if "patchnotes" not in tags:
            continue
        if item.get("feed_type") != 1:
            continue
        title = item.get("title", "")
        match = _VERSION_HINT_RE.search(title)
        notes.append(
            PatchNote(
                gid=str(item.get("gid", "")),
                title=title,
                date=int(item.get("date", 0)),
                contents=item.get("contents", ""),
                url=item.get("url", ""),
                version_hint=match.group(0) if match else None,
            )
        )
    return notes


def _warn(msg: str) -> None:
    print(f"[upstream-sync] warning: {msg}", file=sys.stderr)


def _http_get(
    url: str,
    urlopen,
) -> bytes:
    """GET ``url`` with bounded retries on 429/5xx/URLError.

    Raises:
        urllib.error.HTTPError: on unauthenticated 4xx (401/403) — caller
            should degrade gracefully.
        RuntimeError: after exhausting retries on transient failures.
    """
    last_exc: Exception | None = None
    for attempt in range(_RETRY_ATTEMPTS):
        try:
            with urlopen(url, timeout=30) as resp:
                return resp.read()
        except urllib.error.HTTPError as exc:
            if exc.code in _UNAUTH_STATUSES:
                raise  # do not retry auth failures
            if exc.code not in _RETRY_STATUSES:
                # Other 4xx: treat as terminal but recoverable failure
                raise
            last_exc = exc
        except urllib.error.URLError as exc:
            last_exc = exc

        if attempt < _RETRY_ATTEMPTS - 1:
            time.sleep(_RETRY_BACKOFF_SECONDS[attempt])

    raise RuntimeError(
        f"upstream-sync: exhausted {_RETRY_ATTEMPTS} attempts: {last_exc!r}"
    )


def fetch_patch_notes(
    app_id: str = "2868840",
    count: int = 20,
    cache_dir: Path | None = None,
    *,
    _urlopen=None,
) -> list[PatchNote]:
    """Fetch and filter patch notes from the Steam News API.

    Filter: items where ``tags`` contains ``"patchnotes"`` AND
    ``feed_type == 1``. Returns ``list[PatchNote]`` in API response order
    (newest first).

    Caching: if ``cache_dir`` is provided, the raw JSON payload is written
    to ``cache_dir/patch_notes_<count>.json`` on successful fetch. On
    subsequent calls with the same ``cache_dir`` and ``count``, the cached
    payload is returned without any network access. Cache invalidation is
    manual (delete the file). No TTL in v1.

    Retry: 3 attempts on HTTPError 429/5xx with backoff 1s / 4s.
    Graceful degradation: on unauthenticated 4xx (401/403) or 3+ consecutive
    network errors, returns ``[]`` and logs a warning to stderr.

    The ``_urlopen`` argument is the injection point for tests; defaults
    to :func:`urllib.request.urlopen`.
    """
    urlopen = _urlopen if _urlopen is not None else urllib.request.urlopen

    # Cache hit: read JSON directly, skip the network entirely.
    if cache_dir is not None:
        cached = _cache_path(cache_dir, count)
        if cached.exists():
            try:
                return _filter_and_build(json.loads(cached.read_bytes()))
            except (OSError, json.JSONDecodeError) as exc:
                _warn(f"cache read failed ({cached}): {exc!r}; refetching")

    url = STEAM_NEWS_API.format(appid=app_id, count=count)
    try:
        raw = _http_get(url, urlopen)
    except urllib.error.HTTPError as exc:
        _warn(f"unauthenticated/terminal HTTP {exc.code} from Steam News API; returning []")
        return []
    except RuntimeError as exc:
        _warn(f"{exc}; returning []")
        return []

    try:
        payload = json.loads(raw)
    except json.JSONDecodeError as exc:
        _warn(f"malformed JSON from Steam News API: {exc!r}; returning []")
        return []

    if cache_dir is not None:
        try:
            cache_dir.mkdir(parents=True, exist_ok=True)
            _cache_path(cache_dir, count).write_bytes(raw)
        except OSError as exc:
            _warn(f"cache write failed: {exc!r}")

    return _filter_and_build(payload)


# ---------------------------------------------------------------------------
# Parse
# ---------------------------------------------------------------------------

# Case-insensitive matchers. We intentionally use non-greedy bodies and DOTALL
# so multi-line content inside [list]...[/list] is captured.
_H2_RE = re.compile(r"\[h2\](.*?)\[/h2\]", re.IGNORECASE | re.DOTALL)
_B_RE = re.compile(r"\[b\](.*?)\[/b\]", re.IGNORECASE | re.DOTALL)
_LIST_RE = re.compile(r"\[list\](.*?)\[/list\]", re.IGNORECASE | re.DOTALL)


def _strip_bbcode(text: str) -> str:
    """Remove residual BBCode tags (e.g. inner [b]...[/b]) and trim."""
    return re.sub(r"\[/?[a-zA-Z][^\]]*\]", "", text).strip()


def _section_for(offset: int, sections: list[tuple[int, str]]) -> str:
    """Return the most recent [h2] section name at or before `offset`."""
    current = ""
    for start, name in sections:
        if start <= offset:
            current = name
        else:
            break
    return current


def parse_bbcode(content: str) -> ParsedNote:
    """Parse a BBCode-formatted Steam announcement body.

    Extracts:
        * ``[h2]Section[/h2]`` → ``sections`` list (document order).
        * ``[b]Entity[/b]`` → ``entities`` list, each paired with the most
          recent ``[h2]`` section name (or ``""`` if no preceding section).
        * ``[*]Item`` inside ``[list]...[/list]`` → ``items`` list, paired
          with the most recent ``[h2]`` section.

    Malformed BBCode (e.g. unclosed tags) results in best-effort extraction;
    this function never raises. Tag matching is case-insensitive.
    """
    if not content:
        return ParsedNote(sections=[], entities=[], items=[])

    try:
        # 1. Sections in document order. Trailing colons (common in Mega Crit
        #    patch notes like ``[h2]CONTENT & BALANCE:[/h2]``) are stripped
        #    so section names round-trip cleanly into downstream correlation.
        section_spans: list[tuple[int, str]] = [
            (m.start(), m.group(1).strip().rstrip(":").strip())
            for m in _H2_RE.finditer(content)
        ]
        sections = [name for _, name in section_spans]

        # 2. Entities: every [b]...[/b] in the document, paired to nearest
        #    preceding h2. We deliberately walk the full document — entities
        #    inside [list] blocks (e.g. "[*]Nerfed [b]Blade of Ink[/b] ...")
        #    are part of the patch-notes structure W4 will correlate.
        entities: list[tuple[str, str]] = []
        for m in _B_RE.finditer(content):
            name = m.group(1).strip()
            entities.append((_section_for(m.start(), section_spans), name))

        # 3. Items: every [*]... inside each [list]...[/list]. An item runs
        #    from one [*] to the next [*] (or end of list).
        items: list[tuple[str, str]] = []
        for list_match in _LIST_RE.finditer(content):
            body = list_match.group(1)
            list_offset = list_match.start()
            # Split on [*]; first chunk is preamble (often whitespace).
            parts = re.split(r"\[\*\]", body, flags=re.IGNORECASE)
            for chunk in parts[1:]:
                item_text = _strip_bbcode(chunk)
                if not item_text:
                    continue
                items.append((_section_for(list_offset, section_spans), item_text))

        return ParsedNote(sections=sections, entities=entities, items=items)
    except Exception as exc:  # noqa: BLE001 — best-effort by contract
        _warn(f"parse_bbcode best-effort fallback: {exc!r}")
        return ParsedNote(sections=[], entities=[], items=[])
