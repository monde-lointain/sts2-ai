"""Diff analysis: categorize upstream `git diff --name-status` into 19 buckets.

Runs ``git diff --name-status -M <from_tag> <to_tag>`` against the upstream
tree (NOT the sts2-ai monorepo), categorizes each path, handles renames
(``R{score}``), detects encounter-RNG-driven spawns for auto-DEFER (path-scoped
— only inside ``src/Core/Models/Encounters/**``), auto-discovers characters via
``entity_extract.discover_characters``, and tags entries by character.

Bucket categorization rules (match in order — first match wins):

==================== =========================================================
Bucket               Pattern
==================== =========================================================
multiplayer          starts with ``src/Core/Multiplayer/``
modding              starts with ``src/Core/Modding/``
ui-only              starts with ``src/Core/UI/`` or ``src/Core/Localization/``
                     or ``src/Core/Helpers/Localization/``
art-audio-binding    starts with ``src/Core/Audio/`` or ``src/Core/VFX/``
                     or ``src/Core/Animations/``
random               starts with ``src/Core/Random/``
combat-engine        starts with ``src/Core/Combat/`` or ``src/Core/Hooks/``
                     or ``src/Core/GameActions/`` or ``src/Core/Commands/``
cards                starts with ``src/Core/Models/Cards/``
relics               starts with ``src/Core/Models/Relics/``
powers               starts with ``src/Core/Models/Powers/``
monsters             starts with ``src/Core/Models/Monsters/``
encounters           starts with ``src/Core/Models/Encounters/``
events               starts with ``src/Core/Models/Events/``
potions              starts with ``src/Core/Models/Potions/``
afflictions          starts with ``src/Core/Models/Afflictions/``
enchantments         starts with ``src/Core/Models/Enchantments/``
modifiers            starts with ``src/Core/Models/Modifiers/``
acts                 starts with ``src/Core/Models/Acts/``
characters           starts with ``src/Core/Models/Characters/``
card-pools           starts with ``src/Core/Models/CardPools/``
relic-pools          starts with ``src/Core/Models/RelicPools/``
potion-pools         starts with ``src/Core/Models/PotionPools/``
orbs                 starts with ``src/Core/Models/Orbs/`` (auto-DEFER)
model-bases          matches ``src/Core/Models/[A-Za-z]+Model\\.cs$`` (top-level)
                     OR is ``src/Core/Models/ModelDb.cs``
scenes-gameplay      starts with ``scenes/combat/`` or ``scenes/encounters/``
                     or ``scenes/cards/`` or ``scenes/orbs/``
                     or ``scenes/relics/`` or ``scenes/creature_visuals/``
                     or ``scenes/rooms/``
scenes-ui            starts with ``scenes/`` (any other scenes path)
root-config          matches ``^[^/]*\\.(csproj|sln|godot|json|lock.json)$``
                     OR is ``global.json``, ``packages.lock.json``,
                     ``project.godot``
other                anything else
==================== =========================================================

Encounter-RNG-driven spawns: a file under ``src/Core/Models/Encounters/`` whose
source contains ``Rng.NextItem(``, ``Rng.NextBool(``, or ``Rng.NextInt(`` is
auto-DEFERred per B.1-ε (caller decides; we only flag).

Module imports only stdlib (re, subprocess, pathlib, dataclasses, typing) plus
``upstream_sync.entity_extract``.
"""

from __future__ import annotations

import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Literal

from upstream_sync.entity_extract import discover_characters

DiffStatus = Literal["A", "M", "D", "R"]

# ---------- Bucket name constants ----------

BUCKET_CARDS = "cards"
BUCKET_RELICS = "relics"
BUCKET_POWERS = "powers"
BUCKET_MONSTERS = "monsters"
BUCKET_ENCOUNTERS = "encounters"
BUCKET_EVENTS = "events"
BUCKET_POTIONS = "potions"
BUCKET_AFFLICTIONS = "afflictions"
BUCKET_ENCHANTMENTS = "enchantments"
BUCKET_MODIFIERS = "modifiers"
BUCKET_ACTS = "acts"
BUCKET_CHARACTERS = "characters"
BUCKET_CARD_POOLS = "card-pools"
BUCKET_RELIC_POOLS = "relic-pools"
BUCKET_POTION_POOLS = "potion-pools"
BUCKET_COMBAT_ENGINE = "combat-engine"
BUCKET_RANDOM = "random"
BUCKET_MODEL_BASES = "model-bases"
BUCKET_ORBS = "orbs"
BUCKET_MULTIPLAYER = "multiplayer"
BUCKET_MODDING = "modding"
BUCKET_UI = "ui-only"
BUCKET_ART_AUDIO = "art-audio-binding"
BUCKET_SCENES_GAMEPLAY = "scenes-gameplay"
BUCKET_SCENES_UI = "scenes-ui"
BUCKET_ROOT_CONFIG = "root-config"
BUCKET_OTHER = "other"


# ---------- Data classes ----------


@dataclass(frozen=True)
class DiffEntry:
    """One entry from `git diff --name-status -M`.

    For renames (``status == "R"``), ``path`` is the destination,
    ``rename_from`` is the source, and ``rename_score`` is the similarity
    score (0-100). For all other statuses, ``rename_from`` and
    ``rename_score`` are ``None``.

    ``character_tag`` is the canonical character name (e.g. ``"Silent"``) if
    the filename or any path component matches a member of the auto-discovered
    roster; otherwise ``None``.

    ``line_delta`` is reserved for a future ``git diff --stat`` enhancement;
    always ``None`` in v1.
    """

    status: DiffStatus
    path: str
    rename_from: str | None
    rename_score: int | None
    character_tag: str | None
    line_delta: int | None


@dataclass(frozen=True)
class DiffReport:
    """Result of :func:`analyze_diff`."""

    from_tag: str
    to_tag: str
    buckets: dict[str, list[DiffEntry]]
    renames: list[DiffEntry]
    character_tags_seen: set[str]
    encounter_rng_defers: list[DiffEntry]
    unmatched_paths: list[str]
    discovered_characters: set[str]


# ---------- Path categorizer ----------

# Patterns are ordered; first match wins. Each entry is (predicate, bucket).
# Predicate is a callable taking the path string and returning bool.

_MODEL_BASES_RE = re.compile(r"^src/Core/Models/[A-Za-z]+Model\.cs$")
_ROOT_CONFIG_RE = re.compile(
    r"^[^/]+\.(csproj|sln|godot|json)$|^[^/]+\.lock\.json$"
)

_SCENES_GAMEPLAY_PREFIXES = (
    "scenes/combat/",
    "scenes/encounters/",
    "scenes/cards/",
    "scenes/orbs/",
    "scenes/relics/",
    "scenes/creature_visuals/",
    "scenes/rooms/",
)


def _is_model_bases(path: str) -> bool:
    return path == "src/Core/Models/ModelDb.cs" or bool(
        _MODEL_BASES_RE.match(path)
    )


def _is_scenes_gameplay(path: str) -> bool:
    return any(path.startswith(p) for p in _SCENES_GAMEPLAY_PREFIXES)


def _is_root_config(path: str) -> bool:
    return bool(_ROOT_CONFIG_RE.match(path))


# Order matters — first match wins. Each rule is (predicate, bucket).
_BUCKET_RULES: list[tuple[Callable[[str], bool], str]] = [
    (lambda p: p.startswith("src/Core/Multiplayer/"), BUCKET_MULTIPLAYER),
    (lambda p: p.startswith("src/Core/Modding/"), BUCKET_MODDING),
    (
        lambda p: (
            p.startswith("src/Core/UI/")
            or p.startswith("src/Core/Localization/")
            or p.startswith("src/Core/Helpers/Localization/")
        ),
        BUCKET_UI,
    ),
    (
        lambda p: (
            p.startswith("src/Core/Audio/")
            or p.startswith("src/Core/VFX/")
            or p.startswith("src/Core/Animations/")
        ),
        BUCKET_ART_AUDIO,
    ),
    (lambda p: p.startswith("src/Core/Random/"), BUCKET_RANDOM),
    (
        lambda p: (
            p.startswith("src/Core/Combat/")
            or p.startswith("src/Core/Hooks/")
            or p.startswith("src/Core/GameActions/")
            or p.startswith("src/Core/Commands/")
        ),
        BUCKET_COMBAT_ENGINE,
    ),
    (lambda p: p.startswith("src/Core/Models/Cards/"), BUCKET_CARDS),
    (lambda p: p.startswith("src/Core/Models/Relics/"), BUCKET_RELICS),
    (lambda p: p.startswith("src/Core/Models/Powers/"), BUCKET_POWERS),
    (lambda p: p.startswith("src/Core/Models/Monsters/"), BUCKET_MONSTERS),
    (lambda p: p.startswith("src/Core/Models/Encounters/"), BUCKET_ENCOUNTERS),
    (lambda p: p.startswith("src/Core/Models/Events/"), BUCKET_EVENTS),
    (lambda p: p.startswith("src/Core/Models/Potions/"), BUCKET_POTIONS),
    (lambda p: p.startswith("src/Core/Models/Afflictions/"), BUCKET_AFFLICTIONS),
    (lambda p: p.startswith("src/Core/Models/Enchantments/"), BUCKET_ENCHANTMENTS),
    (lambda p: p.startswith("src/Core/Models/Modifiers/"), BUCKET_MODIFIERS),
    (lambda p: p.startswith("src/Core/Models/Acts/"), BUCKET_ACTS),
    (lambda p: p.startswith("src/Core/Models/Characters/"), BUCKET_CHARACTERS),
    (lambda p: p.startswith("src/Core/Models/CardPools/"), BUCKET_CARD_POOLS),
    (lambda p: p.startswith("src/Core/Models/RelicPools/"), BUCKET_RELIC_POOLS),
    (lambda p: p.startswith("src/Core/Models/PotionPools/"), BUCKET_POTION_POOLS),
    (lambda p: p.startswith("src/Core/Models/Orbs/"), BUCKET_ORBS),
    (_is_model_bases, BUCKET_MODEL_BASES),
    (_is_scenes_gameplay, BUCKET_SCENES_GAMEPLAY),
    (lambda p: p.startswith("scenes/"), BUCKET_SCENES_UI),
    (_is_root_config, BUCKET_ROOT_CONFIG),
]


def bucket_for_path(path: str) -> str:
    """Categorize a path into one of the BUCKET_* constants.

    First-match-wins on the rules listed in the module docstring. Returns
    ``BUCKET_OTHER`` if nothing matches.
    """
    for predicate, bucket in _BUCKET_RULES:
        if predicate(path):
            return bucket
    return BUCKET_OTHER


# ---------- Encounter-RNG DEFER detection ----------

_ENCOUNTERS_PREFIX = "src/Core/Models/Encounters/"
_RNG_TOKENS = ("Rng.NextItem(", "Rng.NextBool(", "Rng.NextInt(")


def is_encounter_rng_defer(upstream_tree: Path, entry_path: str) -> bool:
    """True if ``entry_path`` is under ``src/Core/Models/Encounters/`` and the
    file at ``upstream_tree/entry_path`` contains ``Rng.NextItem(``,
    ``Rng.NextBool(``, or ``Rng.NextInt(`` in its source.

    Returns ``False`` for non-encounter paths regardless of content (path-scoped
    per B.1-ε). Returns ``False`` if the file does not exist (e.g. deleted).
    """
    if not entry_path.startswith(_ENCOUNTERS_PREFIX):
        return False

    target = upstream_tree / entry_path
    try:
        source = target.read_text(encoding="utf-8", errors="replace")
    except (FileNotFoundError, IsADirectoryError, PermissionError):
        return False

    return any(token in source for token in _RNG_TOKENS)


# ---------- Character tagging ----------


def character_tag_for_path(path: str, characters: set[str]) -> str | None:
    """Match the path's filename and any path component against the roster
    (case-insensitively). Returns the canonical character name (as in
    ``characters``) on match, else ``None``.

    Matching rules:
      - For each canonical name in ``characters``, the lowercased name must
        appear as a substring of the lowercased filename (last component) OR
        as a complete path-component anywhere in the path.
      - A name that only appears as a strict prefix of a *different* substring
        does not match (we lowercase both sides, so e.g. ``"iron"`` does not
        match ``"Ironclad"`` because the roster has ``"Ironclad"``, not
        ``"iron"``).
    """
    if not characters:
        return None

    lower_path = path.lower()
    filename = path.rsplit("/", 1)[-1].lower()
    components = {c.lower() for c in path.split("/")}

    for canonical in characters:
        key = canonical.lower()
        # (a) any path component equals the character (case-insensitive)
        if key in components:
            return canonical
        # (b) character name appears as substring of the filename
        if key in filename:
            return canonical
        # (c) component contains character name as substring (handles e.g.
        # "StrikeSilent.cs" under arbitrary subdir whose path may match)
        if key in lower_path:
            return canonical

    return None


# ---------- Diff parsing ----------

_RENAME_RE = re.compile(r"^R(\d+)$")


def _parse_diff_line(line: str) -> tuple[DiffStatus, str, str | None, int | None] | None:
    """Parse a single porcelain line from `git diff --name-status -M`.

    Returns ``(status, path, rename_from, rename_score)`` or ``None`` if the
    line is empty or malformed.
    """
    if not line.strip():
        return None

    parts = line.split("\t")
    if len(parts) < 2:
        return None

    status_token = parts[0].strip()

    # Rename: "R{score}\told\tnew"
    rename_match = _RENAME_RE.match(status_token)
    if rename_match:
        if len(parts) < 3:
            return None
        score = int(rename_match.group(1))
        old_path = parts[1]
        new_path = parts[2]
        return ("R", new_path, old_path, score)

    # Simple statuses: A / M / D (and we accept C as M-ish here, but the spec
    # only lists A/M/D/R, so anything else falls through as unmapped).
    if status_token == "A":
        return ("A", parts[1], None, None)
    if status_token == "M":
        return ("M", parts[1], None, None)
    if status_token == "D":
        return ("D", parts[1], None, None)

    return None


# ---------- Main entry point ----------


def analyze_diff(
    from_tag: str,
    to_tag: str,
    upstream_tree: Path,
    *,
    priority_character: str = "Silent",
    _subprocess_run: Callable[..., Any] | None = None,
) -> DiffReport:
    """Run ``git diff --name-status -M <from_tag> <to_tag>`` in
    ``upstream_tree``, parse the output, and produce a :class:`DiffReport`.

    For each entry:
      - :func:`bucket_for_path` assigns a bucket
      - :func:`character_tag_for_path` tags by auto-discovered character
      - :func:`is_encounter_rng_defer` flags B.1-ε DEFERs

    Renames appear in their destination bucket AND in ``report.renames``.
    Within each bucket, entries are sorted by path. ``line_delta`` is always
    ``None`` in v1 (no ``--stat`` call).

    ``priority_character`` is informational only; partitioning into
    Silent-priority vs Future-character sections is W5's job.
    """
    runner = _subprocess_run if _subprocess_run is not None else subprocess.run

    completed = runner(
        ["git", "diff", "--name-status", "-M", from_tag, to_tag],
        cwd=upstream_tree,
        capture_output=True,
        text=True,
        check=False,
    )

    stdout = getattr(completed, "stdout", "") or ""

    discovered = discover_characters(upstream_tree)

    buckets: dict[str, list[DiffEntry]] = {}
    renames: list[DiffEntry] = []
    character_tags_seen: set[str] = set()
    encounter_rng_defers: list[DiffEntry] = []
    unmatched_paths: list[str] = []

    for line in stdout.splitlines():
        parsed = _parse_diff_line(line)
        if parsed is None:
            continue
        status, path, rename_from, rename_score = parsed

        bucket = bucket_for_path(path)
        char_tag = character_tag_for_path(path, discovered)
        entry = DiffEntry(
            status=status,
            path=path,
            rename_from=rename_from,
            rename_score=rename_score,
            character_tag=char_tag,
            line_delta=None,
        )

        buckets.setdefault(bucket, []).append(entry)

        if status == "R":
            renames.append(entry)

        if char_tag is not None:
            character_tags_seen.add(char_tag)

        if bucket == BUCKET_OTHER:
            unmatched_paths.append(path)

        if is_encounter_rng_defer(upstream_tree, path):
            encounter_rng_defers.append(entry)

    # Sort each bucket by path for deterministic output.
    for bucket_name in list(buckets.keys()):
        buckets[bucket_name].sort(key=lambda e: e.path)

    return DiffReport(
        from_tag=from_tag,
        to_tag=to_tag,
        buckets=buckets,
        renames=renames,
        character_tags_seen=character_tags_seen,
        encounter_rng_defers=encounter_rng_defers,
        unmatched_paths=unmatched_paths,
        discovered_characters=discovered,
    )
