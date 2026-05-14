"""Lightweight C# class-declaration parsing for entity classification.

Two purposes:
  (a) Per-file entity classification (cards, relics, powers, monsters, etc.)
      by matching `class Foo : <BaseModel>` declarations.
  (b) Character auto-discovery: scanning `src/Core/Models/Characters/` to
      learn the character roster (Silent, Defect, Ironclad, ...) without
      hardcoding.

Used downstream by:
  - W3 diff_analyze (character partitioning)
  - W5 port_decisions (Q4 advisory section)

Limitations (best-effort regex, NOT a real C# parser):
  - Detects only top-level `class Name : <BaseModel>` patterns where the base
    matches `BASE_TO_KIND`. Nested classes, generic-base inheritance lists
    longer than one type, and interfaces are not parsed.
  - Comments and string literals are stripped before matching, but the
    stripper is regex-based and handles only common cases (// line, /* */
    block, and "double-quoted" strings without escaped quotes inside).
    Verbatim strings (@"...") and interpolated strings ($"...") are treated
    as plain double-quoted strings — sufficient for production .cs files
    where class declarations don't appear inside strings.
  - Preprocessor directives (#if / #region) are not interpreted; their
    surrounding code is parsed normally.

Module imports only stdlib: re, pathlib, dataclasses, typing.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

EntityKind = Literal[
    "card",
    "relic",
    "power",
    "monster",
    "potion",
    "encounter",
    "event",
    "affliction",
    "enchantment",
    "modifier",
    "act",
    "character",
    "orb",
]

# Maps the C# base class to our EntityKind enum.
BASE_TO_KIND: dict[str, EntityKind] = {
    "CardModel": "card",
    "RelicModel": "relic",
    "PowerModel": "power",
    "MonsterModel": "monster",
    "PotionModel": "potion",
    "EncounterModel": "encounter",
    "EventModel": "event",
    "AfflictionModel": "affliction",
    "EnchantmentModel": "enchantment",
    "ModifierModel": "modifier",
    "ActModel": "act",
    "CharacterModel": "character",
    "OrbModel": "orb",
}

_CLASS_RE = re.compile(
    r"\bclass\s+(\w+)\s*:\s*("
    + "|".join(re.escape(b) for b in BASE_TO_KIND)
    + r")\b"
)

# Strip block comments, line comments, and double-quoted strings so the
# class-decl regex doesn't false-match inside them. Order matters: strings
# can contain "//", and block comments can contain quote chars.
_STRIP_RE = re.compile(
    r"/\*.*?\*/"          # block comment (non-greedy, DOTALL)
    r'|//[^\n]*'          # line comment to end-of-line
    r'|"(?:\\.|[^"\\])*"', # double-quoted string (handles \" escapes)
    re.DOTALL,
)


@dataclass(frozen=True)
class Entity:
    """A single entity declaration discovered in a .cs file."""

    id: str
    kind: EntityKind
    file_path: Path


def _strip_noise(source: str) -> str:
    """Replace comments and strings with whitespace of equal length.

    Preserves byte offsets so regex line/col reporting (if ever added)
    remains stable; structurally equivalent to deletion for our use.
    """
    def _blank(match: re.Match[str]) -> str:
        text = match.group(0)
        # Preserve newlines so line counts stay stable; everything else
        # becomes a space.
        return "".join("\n" if ch == "\n" else " " for ch in text)

    return _STRIP_RE.sub(_blank, source)


def extract_entities(file_path: Path) -> list[Entity]:
    """Parse a single .cs file; return all entity declarations found.

    Returns an empty list if no matching class declarations exist, or if
    the file does not exist. Multiple matches per file are allowed (rare
    but legal in C#).

    Skips matches inside line comments (//), block comments (/* ... */),
    and double-quoted string literals.
    """
    try:
        source = Path(file_path).read_text(encoding="utf-8", errors="replace")
    except (FileNotFoundError, IsADirectoryError, PermissionError):
        return []

    cleaned = _strip_noise(source)
    results: list[Entity] = []
    for match in _CLASS_RE.finditer(cleaned):
        class_name = match.group(1)
        base = match.group(2)
        results.append(
            Entity(id=class_name, kind=BASE_TO_KIND[base], file_path=file_path)
        )
    return results


def _characters_root(upstream_tree: Path) -> Path:
    return upstream_tree / "src" / "Core" / "Models" / "Characters"


def discover_characters(upstream_tree: Path) -> set[str]:
    """Auto-discover the character roster from an upstream tree.

    Unions two sources:
      (a) Immediate subdirectories of `<upstream_tree>/src/Core/Models/Characters/`
          (e.g. ``Silent/``, ``Defect/``). Directory names are character IDs.
      (b) Classes inheriting ``CharacterModel`` anywhere under
          ``<upstream_tree>/src/`` (uses :func:`extract_entities` and filters
          to ``kind == "character"``).

    Returns an empty set if ``<upstream_tree>/src/Core/Models/Characters/``
    does not exist (graceful — caller may be pointed at a non-canonical tree).
    """
    chars_root = _characters_root(upstream_tree)
    if not chars_root.is_dir():
        return set()

    roster: set[str] = set()

    # Source (a): immediate subdirectories of Characters/.
    for child in chars_root.iterdir():
        if child.is_dir():
            roster.add(child.name)

    # Source (b): every class inheriting CharacterModel anywhere under src/.
    src_root = upstream_tree / "src"
    if src_root.is_dir():
        for cs_file in src_root.rglob("*.cs"):
            for entity in extract_entities(cs_file):
                if entity.kind == "character":
                    roster.add(entity.id)

    return roster


def warn_on_roster_drift(previous: set[str], current: set[str]) -> str | None:
    """Compare previous vs current character set.

    Returns a one-line warning if the rosters differ, else ``None``.
    Added/removed names are sorted for stable diff output.
    """
    added = sorted(current - previous)
    removed = sorted(previous - current)
    if not added and not removed:
        return None

    parts: list[str] = []
    if added:
        parts.append(f"added {', '.join(added)}")
    if removed:
        parts.append(f"removed {', '.join(removed)}")
    return "Character roster changed: " + "; ".join(parts)
