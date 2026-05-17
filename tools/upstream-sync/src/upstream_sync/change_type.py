"""Change-type classifier for Steam patch-note excerpt lines.

Heuristic pattern-matching to extract a ``(change_type, magnitude)`` pair from
a single natural-language patch-note line such as:

    "Buffed Untouchable card: upgraded Block gain increased From +2 -> +3"
    → ("buffed", "+2→+3")

    "Nerfed Blade of Ink card: Inky enchantment damage decreased from +2 -> +1"
    → ("nerfed", "+2→+1")

    "Fixed Aeonglass icon/intent display"
    → ("fixed", None)

Design notes
============

This module is intentionally heuristic — it is NOT authoritative. Downstream
consumers must surface output as advisory (per Q1-ADR-013 framing principle).

Approach
--------

``change_type`` classification uses keyword-prefix matching (first token of the
line wins, case-insensitive). Ordering is deliberate: "nerfed" before "changed"
so "nerfed something" doesn't fall into "changed".

``magnitude`` extraction looks for common arrow notation (``+N → +M``,
``from N to M``, ``N → M``) and returns a compact form like ``"+2→+3"``.

Accuracy target: ≥70% on the cached patch-note corpus. This is a heuristic;
lower accuracy for edge-case entries (phrased as "Improved X" vs "Buffed X")
is acceptable. If corpus accuracy drops below 70%, flag the discrepancy in the
engineer report so the project-lead can decide whether to accept it or invest
in a richer NLP approach.

Public surface
--------------

    classify_change_type(excerpt: str) → tuple[str, str | None]

``change_type`` values (exclusive set):
    "buffed" | "nerfed" | "added" | "removed" | "fixed" | "changed" |
    "reworked" | "unclassified"

``magnitude``: compact string like ``"+2→+3"`` or ``None``.
"""

from __future__ import annotations

import re

__all__ = ["classify_change_type"]


# ---------------------------------------------------------------------------
# Change-type keyword rules
# ---------------------------------------------------------------------------
# Each entry: (regex_pattern, change_type).  Patterns are anchored to test the
# first ~40 chars of the normalized line.  First match wins.

_CHANGE_TYPE_RULES: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"\bbuff(?:ed|s|ing)?\b", re.IGNORECASE), "buffed"),
    (re.compile(r"\bnerf(?:ed|s|ing)?\b", re.IGNORECASE), "nerfed"),
    (re.compile(r"\badd(?:ed|s|ing)?\b", re.IGNORECASE), "added"),
    (re.compile(r"\bremov(?:ed|es|ing)?\b", re.IGNORECASE), "removed"),
    (re.compile(r"\bdelet(?:ed|es|ing)?\b", re.IGNORECASE), "removed"),
    (re.compile(r"\bfix(?:ed|es|ing)?\b", re.IGNORECASE), "fixed"),
    (re.compile(r"\brework(?:ed|s|ing)?\b", re.IGNORECASE), "reworked"),
    (re.compile(r"\bredesign(?:ed|s|ing)?\b", re.IGNORECASE), "reworked"),
    (re.compile(r"\boverhaul(?:ed|s|ing)?\b", re.IGNORECASE), "reworked"),
    (re.compile(r"\bchanged?\b", re.IGNORECASE), "changed"),
    (re.compile(r"\bupdat(?:ed|es|ing)?\b", re.IGNORECASE), "changed"),
    (re.compile(r"\btweak(?:ed|s|ing)?\b", re.IGNORECASE), "changed"),
    (re.compile(r"\badjust(?:ed|s|ing)?\b", re.IGNORECASE), "changed"),
    (re.compile(r"\bmodif(?:ied|ies|ying)?\b", re.IGNORECASE), "changed"),
    (re.compile(r"\bincreas(?:ed|es|ing)?\b", re.IGNORECASE), "buffed"),
    (re.compile(r"\bdecreas(?:ed|es|ing)?\b", re.IGNORECASE), "nerfed"),
    (re.compile(r"\bimprove[sd]?\b", re.IGNORECASE), "buffed"),
    (re.compile(r"\breduced?\b", re.IGNORECASE), "nerfed"),
    (re.compile(r"\bnew\b", re.IGNORECASE), "added"),
    (re.compile(r"\bintroduc(?:ed|es|ing)?\b", re.IGNORECASE), "added"),
]


# ---------------------------------------------------------------------------
# Magnitude extraction
# ---------------------------------------------------------------------------
# Patterns in priority order; first match wins.

# Numeric token: integer or decimal, optionally prefixed with +/-.
_NUM = r"[+-]?\d+(?:\.\d+)?"

_MAGNITUDE_PATTERNS: list[re.Pattern[str]] = [
    # "+2 -> +3" / "+2 -> 3" / "2->3" / "+2 → +3" (arrow variants)
    re.compile(
        rf"({_NUM})\s*(?:->|→|–>)\s*({_NUM})",
        re.IGNORECASE,
    ),
    # "from +2 to +3" / "from 4 to 5"
    re.compile(
        rf"\bfrom\s+({_NUM})\s+to\s+({_NUM})\b",
        re.IGNORECASE,
    ),
    # "from +2(+3) -> +1(+2)" or "26(34) -> 28(36)" (bracketed variant used by Mega Crit)
    re.compile(
        rf"({_NUM}(?:\({_NUM}\))?)\s*(?:->|→|–>)\s*({_NUM}(?:\({_NUM}\))?)",
        re.IGNORECASE,
    ),
    # Standalone delta like "+1" at end of clause after colon
    re.compile(
        r":\s*([+-]\d+(?:\.\d+)?)\s*$",
        re.IGNORECASE,
    ),
]


def _extract_magnitude(text: str) -> str | None:
    """Return a compact magnitude string or None."""
    for pat in _MAGNITUDE_PATTERNS:
        m = pat.search(text)
        if m:
            groups = [g for g in m.groups() if g is not None]
            if len(groups) >= 2:
                # Two-value form: normalize arrow
                return f"{groups[0]}→{groups[1]}"
            if len(groups) == 1:
                return groups[0]
    return None


# ---------------------------------------------------------------------------
# Public function
# ---------------------------------------------------------------------------


def classify_change_type(excerpt: str) -> tuple[str, str | None]:
    """Classify a patch-note excerpt line into ``(change_type, magnitude)``.

    Parameters
    ----------
    excerpt:
        A single natural-language line from a Steam patch note (BBCode tags
        should be stripped before calling — pass ``ParsedNote.items`` text).

    Returns
    -------
    (change_type, magnitude)
        ``change_type`` is one of: ``"buffed"``, ``"nerfed"``, ``"added"``,
        ``"removed"``, ``"fixed"``, ``"changed"``, ``"reworked"``,
        ``"unclassified"``.
        ``magnitude`` is a compact string (e.g. ``"+2→+3"``) or ``None``.

    Notes
    -----
    - Classification is keyword-based; first rule that matches wins.
    - Magnitude extraction is heuristic; always returns ``None`` for
      non-numeric changes (e.g. behaviour rewrites).
    - Output is ADVISORY — do NOT treat as authoritative truth.
    """
    if not excerpt or not excerpt.strip():
        return ("unclassified", None)

    text = excerpt.strip()

    # Determine change_type by scanning rules in order.
    change_type = "unclassified"
    for pat, ct in _CHANGE_TYPE_RULES:
        if pat.search(text):
            change_type = ct
            break

    # Magnitude: only meaningful for buff/nerf/changed.
    magnitude: str | None = None
    if change_type in ("buffed", "nerfed", "changed", "reworked"):
        magnitude = _extract_magnitude(text)

    return (change_type, magnitude)
