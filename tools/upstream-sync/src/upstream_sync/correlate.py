"""BBCode-aware correlation between Steam patch notes and diff hits.

This module's output is *advisory* — labeled "Patch-notes HINT" in
port-decision documents, never "truth." A correlation is a suggestion that
``[b]Entity[/b]`` in a Steam announcement *may* correspond to a particular
diff entry; the human port-decider remains authoritative. The hint exists to
guide attention, not to override judgment.

Inputs:
    * a :class:`upstream_sync.diff_analyze.DiffReport` (W3) — categorized
      ``git diff --name-status`` against the upstream tree.
    * a list of :class:`upstream_sync.patch_notes.PatchNote` (W2-E) — Steam
      Community Announcements tagged ``patchnotes``.

Output: a :class:`CorrelationMap` of per-diff-path top-N matches sorted by
score descending, plus the set of patch-note gids that produced no matches
anywhere ("unmatched notes" — useful for QA: a hint we couldn't place is
worth surfacing).

Section-header semantics
========================

The ``[h2]Section[/h2]`` headers in Mega Crit patch notes are used two ways:

1. **Bucket routing.** :data:`SECTION_BUCKET_HINTS` maps a normalized section
   name to a (possibly empty) list of diff-bucket names. If non-empty, the
   correlator only attempts matches against those buckets. Empty list means
   "no filter; consider all buckets" — the right behavior for sections like
   ``Bug Fixes`` whose contents are too heterogeneous to route on header
   alone (the individual ``[b]Entity[/b]`` does the routing).

2. **Character scoping.** Section headers that name a character (e.g.
   ``[h2]Silent:[/h2]``) scope every entity mentioned in the section to that
   character. Entities are then only matched against diff entries whose
   ``character_tag`` matches (or is ``None``) — preventing
   ``[b]Strike[/b]`` under ``[h2]Silent:[/h2]`` from matching
   ``Cards/StrikeIronclad.cs``.

Q1-ADR-013 Element 1 reminder
=============================

Per the Q1-ADR-013 framing principle, *correlator output is HINT, never
authoritative*. Downstream port-decision rendering must label every hint as
"Patch-notes HINT" and never as "ground truth."
"""

from __future__ import annotations

import re
from dataclasses import dataclass

from upstream_sync.diff_analyze import DiffEntry, DiffReport
from upstream_sync.patch_notes import PatchNote, parse_bbcode

__all__ = [
    "CHARACTER_SECTION_HEADERS",
    "SECTION_BUCKET_HINTS",
    "CorrelationMap",
    "Match",
    "character_scope_filter",
    "correlate",
    "extract_file_stem",
    "normalize_section_header",
    "score_entity_match",
]


# ---------------------------------------------------------------------------
# Public constants
# ---------------------------------------------------------------------------

# Mapping from BBCode section header (normalized) to which diff buckets it
# hints at. Keys are case-insensitive (lowercased) with trailing colons
# stripped — i.e. the output of :func:`normalize_section_header`.
#
# Empty list means "no bucket filter; match anywhere." This is the right
# behavior for sections like ``Bug Fixes`` whose contents span multiple
# bucket categories; per-item content drives routing instead.
SECTION_BUCKET_HINTS: dict[str, list[str]] = {
    "content & balance": ["cards", "relics", "powers", "potions"],
    "enemies": ["monsters", "encounters"],
    "bug fixes": [],  # too broad — leave empty; per-item content drives match
    "ancients": [],  # DEFER per mechanics_notes_backlog; no PORT hint
    "multiplayer": [],  # IGNORE per Q1-ADR-009; no hint
    "potions & relics": ["potions", "relics"],
    "art": [],  # mostly IGNORE
    "writing": [],  # mostly IGNORE
    "audio": [],  # mostly IGNORE
    "user interface & experience": [],  # mostly IGNORE
}

# Character section headers (normalized) — entities mentioned under these
# sections are scoped to that character for matching. These are SEED
# defaults; the actual character set is passed via the
# ``discovered_characters`` arg to :func:`correlate` so new characters
# auto-match.
CHARACTER_SECTION_HEADERS: set[str] = {
    "silent",
    "defect",
    "ironclad",
    "regent",
    "necrobinder",
    "watcher",
}


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Match:
    """Single correlation between a patch-note entity reference and a diff entry.

    A ``Match`` is *advisory only* — it represents a hint that the human port
    decider should consider, never a ground-truth claim.
    """

    diff_path: str  # the diff entry's path
    note_gid: str  # the patch-note's gid
    note_title: str  # for context
    section: str  # the [h2] section (normalized); "" if outside any
    entity: str  # the [b]...[/b] entity name as it appears in patch notes
    score: float  # 0.0 - 1.0 confidence (1.0 = exact stem match)
    excerpt: str  # ~excerpt_chars of surrounding patch-note content, BBCode stripped


@dataclass(frozen=True)
class CorrelationMap:
    """Per-diff-path correlation results.

    Output of :func:`correlate`. Carries *advisory* hints only; downstream
    port-decision docs must surface these as "Patch-notes HINT" and never
    treat them as authoritative.
    """

    matches: dict[str, list[Match]]  # diff_path -> top-N matches sorted by score desc
    unmatched_notes: list[str]  # patch-note gids that produced zero matches anywhere


# ---------------------------------------------------------------------------
# Normalization helpers
# ---------------------------------------------------------------------------


def normalize_section_header(raw: str) -> str:
    """Strip whitespace, lowercase, strip trailing colon. Idempotent.

    Used to canonicalize both BBCode ``[h2]`` section names *and* the keys
    of :data:`SECTION_BUCKET_HINTS` / :data:`CHARACTER_SECTION_HEADERS` so
    lookups are case-insensitive and tolerant of trailing colons (e.g.
    ``CONTENT & BALANCE:`` -> ``content & balance``).
    """
    return raw.strip().lower().rstrip(":").strip()


def extract_file_stem(diff_path: str) -> str:
    """Return the filename without its final extension.

    >>> extract_file_stem("src/Core/Models/Cards/StrikeSilent.cs")
    'StrikeSilent'
    >>> extract_file_stem("Aeonglass.cs")
    'Aeonglass'
    """
    filename = diff_path.rsplit("/", 1)[-1]
    dot = filename.rfind(".")
    if dot <= 0:  # no extension or hidden file
        return filename
    return filename[:dot]


# ---------------------------------------------------------------------------
# Scoring
# ---------------------------------------------------------------------------

_CAMEL_SPLIT_RE = re.compile(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")


def _camel_words(stem: str) -> list[str]:
    """Split a CamelCase identifier into its constituent words.

    >>> _camel_words("FlashStrike")
    ['Flash', 'Strike']
    >>> _camel_words("StrikeSilent")
    ['Strike', 'Silent']
    >>> _camel_words("XMLParser")
    ['XML', 'Parser']
    """
    return [w for w in _CAMEL_SPLIT_RE.split(stem) if w]


def _normalize_entity(entity: str) -> str:
    """Strip whitespace and a single trailing colon; preserve case."""
    return entity.strip().rstrip(":").strip()


def score_entity_match(entity: str, file_stem: str) -> float:
    """Score the match between a BBCode entity name and a file stem.

    Scoring rules (highest score wins on overlap):

    - Exact case-insensitive match -> 1.0
    - file_stem starts with entity (case-insensitive) -> 0.7
    - entity is a word in file_stem (CamelCase split, case-insensitive) -> 0.5
    - Otherwise -> 0.0

    ``entity`` is normalized first: whitespace stripped, trailing colon
    dropped. Empty entity (after normalization) yields 0.0.
    """
    entity_norm = _normalize_entity(entity)
    if not entity_norm or not file_stem:
        return 0.0

    entity_lower = entity_norm.lower()
    stem_lower = file_stem.lower()

    if entity_lower == stem_lower:
        return 1.0

    if stem_lower.startswith(entity_lower):
        return 0.7

    # CamelCase word match
    words_lower = [w.lower() for w in _camel_words(file_stem)]
    if entity_lower in words_lower:
        return 0.5

    return 0.0


# ---------------------------------------------------------------------------
# Character scoping
# ---------------------------------------------------------------------------


def character_scope_filter(
    diff_entry_character: str | None,
    section_character: str | None,
) -> bool:
    """Return True if this (diff entry, section) pairing is character-consistent.

    Rules:
        - If ``section_character`` is None: don't filter (True)
        - If ``diff_entry_character`` is None: don't filter (True) — the diff
          entry doesn't make a competing character claim
        - If both are set and match case-insensitively: True
        - If both are set and don't match: False (filter this pairing out)

    The two-None case is the common one (no character signal on either side);
    we return True there as well.
    """
    if section_character is None:
        return True
    if diff_entry_character is None:
        return True
    return diff_entry_character.lower() == section_character.lower()


# ---------------------------------------------------------------------------
# Excerpt extraction
# ---------------------------------------------------------------------------

_BBCODE_TAG_RE = re.compile(r"\[/?[a-zA-Z][^\]]*\]")


def _strip_bbcode_tags(text: str) -> str:
    """Remove BBCode tags (e.g. ``[b]``, ``[/h2]``) from ``text``."""
    return _BBCODE_TAG_RE.sub("", text)


def _build_excerpt(content: str, entity: str, excerpt_chars: int) -> str:
    """Return a ~``excerpt_chars`` excerpt of ``content`` centered on the
    first ``[b]entity[/b]`` occurrence, with BBCode tags stripped.

    If the entity isn't found verbatim (case-insensitive), falls back to
    the first occurrence of the bare entity name. If still not found,
    returns the leading slice of ``content`` (BBCode stripped).
    """
    if not content:
        return ""

    half = max(excerpt_chars // 2, 0)
    needle = f"[b]{entity}[/b]"
    idx = content.lower().find(needle.lower())
    if idx < 0:
        idx = content.lower().find(entity.lower())
    if idx < 0:
        return _strip_bbcode_tags(content[:excerpt_chars]).strip()

    start = max(idx - half, 0)
    end = min(idx + len(needle) + half, len(content))
    return _strip_bbcode_tags(content[start:end]).strip()


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------


def _iter_diff_entries(
    diff_report: DiffReport,
    bucket_hints: list[str],
) -> list[DiffEntry]:
    """Flatten the diff_report's buckets, optionally filtered to bucket_hints.

    Empty ``bucket_hints`` means "no filter; emit all buckets."
    """
    if bucket_hints:
        hints_lower = {b.lower() for b in bucket_hints}
        return [
            entry
            for bucket_name, entries in diff_report.buckets.items()
            if bucket_name.lower() in hints_lower
            for entry in entries
        ]
    return [entry for entries in diff_report.buckets.values() for entry in entries]


def correlate(
    diff_report: DiffReport,
    patch_notes: list[PatchNote],
    *,
    discovered_characters: set[str] | None = None,
    top_n_per_path: int = 3,
    excerpt_chars: int = 120,
) -> CorrelationMap:
    """Build a :class:`CorrelationMap` from a :class:`DiffReport` and patch notes.

    Output is *advisory* (per Q1-ADR-013 Element 1) — every match is a hint
    the port-decision step may consider, never authoritative truth.

    Algorithm:

    1. Build the effective character roster as the union of
       ``discovered_characters`` (if provided) and
       :data:`CHARACTER_SECTION_HEADERS` (case-insensitive). This roster is
       used to decide whether a given ``[h2]`` section header is naming a
       character (and therefore scopes its entities).

    2. For each :class:`PatchNote`:

       a. ``parse_bbcode(note.contents)`` -> :class:`ParsedNote` with
          ``(section, entity)`` pairs.

       b. For each ``(section, entity)``:

          - Normalize the section header.
          - If the section is in the character roster, capture the
            canonical character name as ``section_character``.
          - Look up bucket hints via :data:`SECTION_BUCKET_HINTS`. Missing
            key or empty list means "no filter; consider all buckets."
            Character section headers themselves are *not* in
            SECTION_BUCKET_HINTS; they don't constrain buckets.
          - Iterate ``DiffEntry`` rows in the filtered buckets, apply
            :func:`character_scope_filter`, and score with
            :func:`score_entity_match`. Non-zero scores produce a
            :class:`Match`.

    3. Per diff path, sort matches by ``(score desc, date desc, gid asc)``
       and truncate to ``top_n_per_path``.

    4. A patch note whose entities produced zero matches anywhere — across
       *all* diff paths — appears in ``unmatched_notes``. A patch note
       with no entities at all is NOT marked unmatched (nothing to match).

    Parameters
    ----------
    diff_report:
        Output of :func:`upstream_sync.diff_analyze.analyze_diff`.
    patch_notes:
        Output of :func:`upstream_sync.patch_notes.fetch_patch_notes`.
    discovered_characters:
        Optional canonical character names from
        :func:`upstream_sync.entity_extract.discover_characters`. Unioned
        with :data:`CHARACTER_SECTION_HEADERS` so new characters route
        automatically. ``None`` (default) means use only the seed set.
    top_n_per_path:
        Per-diff-path bound on the number of matches retained (default 3).
    excerpt_chars:
        Approximate character count of the ``Match.excerpt`` window
        (default 120).

    Returns
    -------
    CorrelationMap
        Per-diff-path top-N matches plus unmatched note gids. All hint
        framing applies; downstream renderers must label "Patch-notes HINT."
    """
    # Effective character roster (normalized form for set membership testing).
    seed = set(CHARACTER_SECTION_HEADERS)
    if discovered_characters:
        seed |= {normalize_section_header(c) for c in discovered_characters}
    # Map normalized name -> canonical for character_scope_filter comparison.
    canonical_by_norm: dict[str, str] = {}
    for canonical in discovered_characters or set():
        canonical_by_norm[normalize_section_header(canonical)] = canonical
    # Seed-only entries: canonical form is title-cased version.
    for norm in seed:
        canonical_by_norm.setdefault(norm, norm.title())

    # Collect all matches by (diff_path, gid) so we can dedupe and sort later.
    # all_matches[diff_path] -> list[Match]; matched_gids tracks which notes
    # produced at least one match anywhere.
    all_matches: dict[str, list[Match]] = {}
    matched_gids: set[str] = set()
    seen_gids_with_entities: set[str] = set()
    # Keep a date lookup for tie-breaking.
    date_by_gid: dict[str, int] = {note.gid: note.date for note in patch_notes}

    for note in patch_notes:
        parsed = parse_bbcode(note.contents)
        if parsed.entities:
            seen_gids_with_entities.add(note.gid)

        for raw_section, raw_entity in parsed.entities:
            section_norm = normalize_section_header(raw_section)
            entity_norm = _normalize_entity(raw_entity)
            if not entity_norm:
                continue

            # Character scoping: if section names a character, use it.
            section_character: str | None = None
            if section_norm in seed:
                section_character = canonical_by_norm.get(section_norm, section_norm.title())

            # Bucket hint lookup. Character sections don't appear in
            # SECTION_BUCKET_HINTS; missing key = no filter.
            bucket_hints = SECTION_BUCKET_HINTS.get(section_norm, [])

            candidates = _iter_diff_entries(diff_report, bucket_hints)
            for entry in candidates:
                if not character_scope_filter(entry.character_tag, section_character):
                    continue
                stem = extract_file_stem(entry.path)
                score = score_entity_match(entity_norm, stem)
                if score <= 0.0:
                    continue
                excerpt = _build_excerpt(note.contents, raw_entity, excerpt_chars)
                match = Match(
                    diff_path=entry.path,
                    note_gid=note.gid,
                    note_title=note.title,
                    section=section_norm,
                    entity=raw_entity,
                    score=score,
                    excerpt=excerpt,
                )
                all_matches.setdefault(entry.path, []).append(match)
                matched_gids.add(note.gid)

    # Sort + truncate per path. Tie-break: score desc, date desc, gid asc.
    def _sort_key(m: Match) -> tuple[float, int, str]:
        return (-m.score, -date_by_gid.get(m.note_gid, 0), m.note_gid)

    sorted_matches: dict[str, list[Match]] = {}
    for path, matches in all_matches.items():
        # Within a single (path, gid) it's possible the same note matches
        # multiple times via different entity occurrences. We keep only the
        # highest-scoring per (path, gid) to honor the "even if mentioned 5
        # times" rule in the spec's edge case 4.
        best_per_gid: dict[str, Match] = {}
        for m in matches:
            existing = best_per_gid.get(m.note_gid)
            if existing is None or m.score > existing.score:
                best_per_gid[m.note_gid] = m
        deduped = list(best_per_gid.values())
        deduped.sort(key=_sort_key)
        sorted_matches[path] = deduped[:top_n_per_path]

    # Unmatched notes: had entities, but produced zero matches anywhere.
    # A note with no entities at all isn't 'unmatched' — there was nothing
    # to match. Sort gids deterministically.
    unmatched = sorted(seen_gids_with_entities - matched_gids)

    return CorrelationMap(matches=sorted_matches, unmatched_notes=unmatched)
