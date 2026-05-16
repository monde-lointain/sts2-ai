"""Per-sync port-decision markdown rendering.

Given a :class:`DiffReport` (W3), a :class:`CorrelationMap` (W4), and an
optional baseline, emit a structured markdown document the Q1 lead reviews.

Decisions per row: ``PORT`` / ``DELETE`` / ``DEFER`` / ``IGNORE`` /
``SURFACE-NO-ACTION``. Empty-diff short-circuit emits a one-page note.

Output is *advisory* (Q1-ADR-013 Element 1) — patch-note correlations are
labeled "Patch-notes HINT" and never authoritative. Q4 registry updates are
recommendations only; ADR-003 stability rules apply.

Public surface:

* Dataclasses: :class:`PortRow`, :class:`Q4Advisory`, :class:`RenderInputs`
* Functions: :func:`assign_decision`, :func:`build_port_rows`,
  :func:`build_q4_advisory`, :func:`render`, :func:`write_doc`

Imports: stdlib + ``jinja2`` + ``upstream_sync.{diff_analyze, correlate,
entity_extract}``.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

import jinja2

from upstream_sync.correlate import CorrelationMap, Match
from upstream_sync.diff_analyze import (
    BUCKET_ACTS,
    BUCKET_AFFLICTIONS,
    BUCKET_ART_AUDIO,
    BUCKET_CARD_POOLS,
    BUCKET_CARDS,
    BUCKET_CHARACTERS,
    BUCKET_COMBAT_ENGINE,
    BUCKET_ENCHANTMENTS,
    BUCKET_ENCOUNTERS,
    BUCKET_EVENTS,
    BUCKET_MODDING,
    BUCKET_MODEL_BASES,
    BUCKET_MODIFIERS,
    BUCKET_MONSTERS,
    BUCKET_MULTIPLAYER,
    BUCKET_ORBS,
    BUCKET_OTHER,
    BUCKET_POTION_POOLS,
    BUCKET_POTIONS,
    BUCKET_POWERS,
    BUCKET_RANDOM,
    BUCKET_RELIC_POOLS,
    BUCKET_RELICS,
    BUCKET_ROOT_CONFIG,
    BUCKET_SCENES_GAMEPLAY,
    BUCKET_SCENES_UI,
    BUCKET_UI,
    DiffEntry,
    DiffReport,
)
from upstream_sync.entity_extract import EntityKind, extract_entities

__all__ = [
    "IGNORE_BUCKETS",
    "PRIORITY_BUCKETS",
    "DecisionKind",
    "PortRow",
    "Q4Advisory",
    "RenderInputs",
    "assign_decision",
    "bucket_titles",
    "build_port_rows",
    "build_q4_advisory",
    "render",
    "write_doc",
]


DecisionKind = Literal["PORT", "DELETE", "DEFER", "IGNORE", "SURFACE-NO-ACTION"]


# --------------------------------------------------------------------------- #
# Constants                                                                   #
# --------------------------------------------------------------------------- #


# Phase-1A Silent-priority section order — first match drives the per-bucket
# heading order in the doc.
PRIORITY_BUCKETS: list[str] = [
    BUCKET_COMBAT_ENGINE,
    BUCKET_RANDOM,
    BUCKET_MODEL_BASES,
    BUCKET_CARDS,
    BUCKET_RELICS,
    BUCKET_POWERS,
    BUCKET_POTIONS,
    BUCKET_MONSTERS,
    BUCKET_ENCOUNTERS,
    BUCKET_EVENTS,
    BUCKET_CHARACTERS,
    BUCKET_ACTS,
    BUCKET_CARD_POOLS,
    BUCKET_RELIC_POOLS,
    BUCKET_POTION_POOLS,
    BUCKET_AFFLICTIONS,
    BUCKET_ENCHANTMENTS,
    BUCKET_MODIFIERS,
]

# Buckets where every entry is IGNOREd by policy (Q1-ADR-009 / spec).
IGNORE_BUCKETS: list[str] = [
    BUCKET_MULTIPLAYER,
    BUCKET_MODDING,
    BUCKET_UI,
    BUCKET_ART_AUDIO,
    BUCKET_SCENES_UI,
]

# Human-readable bucket titles for section headings.
bucket_titles: dict[str, str] = {
    BUCKET_COMBAT_ENGINE: "Combat engine",
    BUCKET_RANDOM: "Random",
    BUCKET_MODEL_BASES: "Model bases",
    BUCKET_CARDS: "Cards",
    BUCKET_RELICS: "Relics",
    BUCKET_POWERS: "Powers",
    BUCKET_POTIONS: "Potions",
    BUCKET_MONSTERS: "Monsters",
    BUCKET_ENCOUNTERS: "Encounters",
    BUCKET_EVENTS: "Events",
    BUCKET_CHARACTERS: "Characters",
    BUCKET_ACTS: "Acts",
    BUCKET_CARD_POOLS: "Card pools",
    BUCKET_RELIC_POOLS: "Relic pools",
    BUCKET_POTION_POOLS: "Potion pools",
    BUCKET_AFFLICTIONS: "Afflictions",
    BUCKET_ENCHANTMENTS: "Enchantments",
    BUCKET_MODIFIERS: "Modifiers",
    BUCKET_ORBS: "Orbs",
    BUCKET_SCENES_GAMEPLAY: "Scenes (gameplay)",
    BUCKET_SCENES_UI: "Scenes (UI)",
    BUCKET_ROOT_CONFIG: "Root config",
    BUCKET_MULTIPLAYER: "Multiplayer",
    BUCKET_MODDING: "Modding",
    BUCKET_UI: "UI / Localization",
    BUCKET_ART_AUDIO: "Art / Audio / VFX",
    BUCKET_OTHER: "Other",
}

# Buckets considered "gameplay" — used both by the Q4 advisory builder
# (registry updates) and by downstream-regen recommendations.
_GAMEPLAY_BUCKETS: set[str] = {
    BUCKET_CARDS,
    BUCKET_RELICS,
    BUCKET_POWERS,
    BUCKET_POTIONS,
    BUCKET_MONSTERS,
    BUCKET_ENCOUNTERS,
    BUCKET_EVENTS,
    BUCKET_AFFLICTIONS,
    BUCKET_ENCHANTMENTS,
    BUCKET_MODIFIERS,
    BUCKET_ACTS,
    BUCKET_CHARACTERS,
    BUCKET_CARD_POOLS,
    BUCKET_RELIC_POOLS,
    BUCKET_POTION_POOLS,
    BUCKET_ORBS,
}

# Inverse of BASE_TO_KIND keys per bucket — used to infer EntityKind for
# deleted files where we can't parse contents.
_BUCKET_TO_KIND: dict[str, EntityKind] = {
    BUCKET_CARDS: "card",
    BUCKET_RELICS: "relic",
    BUCKET_POWERS: "power",
    BUCKET_POTIONS: "potion",
    BUCKET_MONSTERS: "monster",
    BUCKET_ENCOUNTERS: "encounter",
    BUCKET_EVENTS: "event",
    BUCKET_AFFLICTIONS: "affliction",
    BUCKET_ENCHANTMENTS: "enchantment",
    BUCKET_MODIFIERS: "modifier",
    BUCKET_ACTS: "act",
    BUCKET_CHARACTERS: "character",
    BUCKET_ORBS: "orb",
}


# --------------------------------------------------------------------------- #
# Dataclasses                                                                 #
# --------------------------------------------------------------------------- #


@dataclass(frozen=True)
class PortRow:
    """One row in the per-bucket decision table."""

    path: str
    status: str  # M/A/D/R{score}
    line_delta: int | None  # None for v1
    character_tag: str | None
    decision: DecisionKind
    re_eval_trigger: str | None
    patch_notes_hint: str | None
    rationale: str


@dataclass(frozen=True)
class Q4Advisory:
    """Recommended Q4 token registry updates (advisory)."""

    added: list[tuple[str, str]]  # (entity_id, kind)
    removed: list[tuple[str, str]]  # (entity_id, kind)


@dataclass(frozen=True)
class RenderInputs:
    """All inputs port_decisions needs to render."""

    diff_report: DiffReport
    correlation_map: CorrelationMap
    q4_advisory: Q4Advisory
    from_buildid: str | None
    to_buildid: str
    generated_at: str
    tool_version: str
    priority_character: str


# --------------------------------------------------------------------------- #
# assign_decision                                                             #
# --------------------------------------------------------------------------- #

# Buckets in the IGNORE-by-policy set (BUCKET_SCENES_UI handled separately
# below to keep its rationale distinct, per spec table).
_FLAT_IGNORE_BUCKETS: frozenset[str] = frozenset(
    {
        BUCKET_MULTIPLAYER,
        BUCKET_MODDING,
        BUCKET_UI,
        BUCKET_ART_AUDIO,
    }
)


def assign_decision(entry: DiffEntry, bucket: str) -> tuple[DecisionKind, str | None, str]:
    """Heuristic per-row decision assignment.

    Rules apply in priority order; the first match wins. Caller is
    responsible for the encounter-RNG-DEFER override (it has the
    diff_report context this function doesn't); see :func:`build_port_rows`.
    """
    if bucket in _FLAT_IGNORE_BUCKETS:
        return ("IGNORE", None, f"{bucket} per Q1-ADR-009/spec")

    if bucket == BUCKET_ORBS:
        return (
            "DEFER",
            "pending orbs mechanics-notes-backlog item",
            "Orbs system mechanics not yet documented",
        )

    # Gameplay buckets — decide based on status.
    if bucket in _GAMEPLAY_BUCKETS:
        if entry.status == "D":
            return ("DELETE", None, "Upstream removed this file")
        if entry.status == "A":
            return ("PORT", None, f"New file in {bucket}")
        if entry.status == "M":
            return ("PORT", None, f"Modified file in {bucket}")
        if entry.status == "R":
            return (
                "PORT",
                None,
                f"Renamed {entry.rename_from} -> {entry.path}",
            )
        return ("PORT", None, "Default action")

    if bucket == BUCKET_SCENES_UI:
        return ("IGNORE", None, "UI-only scene")

    if bucket == BUCKET_SCENES_GAMEPLAY:
        return ("PORT", None, "Gameplay-relevant scene")

    if bucket == BUCKET_ROOT_CONFIG:
        return ("PORT", None, "Root project config")

    if bucket == BUCKET_OTHER:
        return (
            "SURFACE-NO-ACTION",
            None,
            "Unbucketed path; review allowlist",
        )

    return ("PORT", None, "Default action")


# --------------------------------------------------------------------------- #
# build_port_rows                                                             #
# --------------------------------------------------------------------------- #


def _format_status(entry: DiffEntry) -> str:
    """Format status for the table — ``R{score}`` for renames."""
    if entry.status == "R" and entry.rename_score is not None:
        return f"R{entry.rename_score}"
    return entry.status


def _format_hint(match: Match | None) -> str | None:
    """Format the top-1 correlation match as ``PCN: '<excerpt>' (gid <gid>)``."""
    if match is None:
        return None
    excerpt = match.excerpt.replace("'", "’")  # avoid quote-in-quote
    return f"PCN: '{excerpt}' (gid {match.note_gid})"


def _is_priority_char(character_tag: str | None, priority: str) -> bool:
    """True if the entry is unassigned or matches the priority character."""
    if character_tag is None:
        return True
    return character_tag.lower() == priority.lower()


def build_port_rows(
    diff_report: DiffReport,
    correlation_map: CorrelationMap,
    priority_character: str,
) -> dict[str, list[PortRow]]:
    """For each bucket with entries, emit a sorted list of :class:`PortRow`.

    Encounter-RNG-DEFER override: entries present in
    ``diff_report.encounter_rng_defers`` get the DEFER decision regardless
    of their bucket-based default.

    Future-character override: a row whose ``character_tag`` is set and
    does NOT match ``priority_character`` becomes ``SURFACE-NO-ACTION``.
    """
    rng_defer_paths: set[str] = {e.path for e in diff_report.encounter_rng_defers}

    out: dict[str, list[PortRow]] = {}
    for bucket, entries in diff_report.buckets.items():
        if not entries:
            continue
        rows: list[PortRow] = []
        for entry in sorted(entries, key=lambda e: e.path):
            # Encounter-RNG-DEFER takes precedence over everything except the
            # bucket-level IGNORE rules (those buckets don't overlap with the
            # encounters bucket).
            if entry.path in rng_defer_paths:
                decision: DecisionKind = "DEFER"
                trig: str | None = "pending B.1-ε encounter-RNG plumbing"
                rationale = "Encounter uses Rng.NextItem/NextBool/NextInt; defer until B.1-ε"
            # Non-priority-character override: if a row has an explicit
            # tag that's not the priority character, surface only.
            elif entry.character_tag is not None and not _is_priority_char(
                entry.character_tag, priority_character
            ):
                decision = "SURFACE-NO-ACTION"
                trig = None
                rationale = f"Future-character file: {entry.character_tag}"
            else:
                decision, trig, rationale = assign_decision(entry, bucket)

            matches = correlation_map.matches.get(entry.path, [])
            top = matches[0] if matches else None
            row = PortRow(
                path=entry.path,
                status=_format_status(entry),
                line_delta=entry.line_delta,
                character_tag=entry.character_tag,
                decision=decision,
                re_eval_trigger=trig,
                patch_notes_hint=_format_hint(top),
                rationale=rationale,
            )
            rows.append(row)
        out[bucket] = rows
    return out


# --------------------------------------------------------------------------- #
# build_q4_advisory                                                           #
# --------------------------------------------------------------------------- #


_PATH_STEM_RE = re.compile(r"([^/]+?)\.cs$")


def _file_stem(path: str) -> str:
    m = _PATH_STEM_RE.search(path)
    if m:
        return m.group(1)
    # Fallback: last path component without extension.
    name = path.rsplit("/", 1)[-1]
    dot = name.rfind(".")
    return name if dot <= 0 else name[:dot]


def build_q4_advisory(
    diff_report: DiffReport,
    upstream_tree: Path,
) -> Q4Advisory:
    """Walk gameplay-bucket diff entries; emit registry add/remove suggestions.

    ADDED or MODIFIED files in a gameplay bucket are parsed via
    :func:`upstream_sync.entity_extract.extract_entities`. Each discovered
    entity contributes one ``(entity_id, kind)`` to :attr:`Q4Advisory.added`.

    DELETED files can't be parsed (the file is gone from upstream). For v1,
    use the file stem as ``entity_id`` and infer ``kind`` from the bucket
    (best-effort).
    """
    added: list[tuple[str, str]] = []
    removed: list[tuple[str, str]] = []
    seen_added: set[tuple[str, str]] = set()
    seen_removed: set[tuple[str, str]] = set()

    for bucket, entries in diff_report.buckets.items():
        if bucket not in _GAMEPLAY_BUCKETS:
            continue
        for entry in entries:
            if entry.status in ("A", "M"):
                full = upstream_tree / entry.path
                for ent in extract_entities(full):
                    key = (ent.id, ent.kind)
                    if key not in seen_added:
                        seen_added.add(key)
                        added.append(key)
            elif entry.status == "D":
                stem = _file_stem(entry.path)
                kind = _BUCKET_TO_KIND.get(bucket)
                if kind is None:
                    continue
                key = (stem, kind)
                if key not in seen_removed:
                    seen_removed.add(key)
                    removed.append(key)
            elif entry.status == "R":
                # Renames: treat the new path as add-like (parse the new file)
                # and the old path as removal advisory.
                full = upstream_tree / entry.path
                for ent in extract_entities(full):
                    key = (ent.id, ent.kind)
                    if key not in seen_added:
                        seen_added.add(key)
                        added.append(key)
                if entry.rename_from:
                    stem = _file_stem(entry.rename_from)
                    kind = _BUCKET_TO_KIND.get(bucket)
                    if kind is not None:
                        key = (stem, kind)
                        if key not in seen_removed:
                            seen_removed.add(key)
                            removed.append(key)

    return Q4Advisory(added=added, removed=removed)


# --------------------------------------------------------------------------- #
# render                                                                      #
# --------------------------------------------------------------------------- #


_TEMPLATES_DIR = Path(__file__).resolve().parent.parent.parent / "docs"
_TEMPLATE_FULL = "port-decision-template.md"
_TEMPLATE_EMPTY = "port-decision-empty.md"


def _jinja_env() -> jinja2.Environment:
    env = jinja2.Environment(
        loader=jinja2.FileSystemLoader(str(_TEMPLATES_DIR)),
        autoescape=False,
        keep_trailing_newline=True,
        trim_blocks=False,
        lstrip_blocks=False,
    )
    env.filters["priority_char_filter"] = _priority_char_filter
    return env


def _priority_char_filter(rows: list[PortRow], priority_character: str) -> list[PortRow]:
    """Keep rows whose character_tag is None or matches priority_character."""
    return [r for r in rows if _is_priority_char(r.character_tag, priority_character)]


def _has_any_entries(diff_report: DiffReport) -> bool:
    return any(entries for entries in diff_report.buckets.values())


def _gather_non_priority_rows(
    rows_by_bucket: dict[str, list[PortRow]],
    priority_character: str,
) -> list[PortRow]:
    """All rows whose character_tag is set and does NOT match priority."""
    out: list[PortRow] = []
    for rows in rows_by_bucket.values():
        for row in rows:
            if row.character_tag is not None and not _is_priority_char(
                row.character_tag, priority_character
            ):
                out.append(row)
    out.sort(key=lambda r: (r.character_tag or "", r.path))
    return out


def _gather_encounter_rng_rows(
    rows_by_bucket: dict[str, list[PortRow]],
) -> list[PortRow]:
    out: list[PortRow] = []
    encounter_rows = rows_by_bucket.get(BUCKET_ENCOUNTERS, [])
    for row in encounter_rows:
        if row.decision == "DEFER" and row.re_eval_trigger and "B.1" in row.re_eval_trigger:
            out.append(row)
    return out


def render(inputs: RenderInputs) -> str:
    """Render the markdown port-decision doc via Jinja2.

    Empty-diff short-circuit: if the diff report has zero entries across
    all buckets, render the one-page no-changes template instead.
    """
    env = _jinja_env()

    if not _has_any_entries(inputs.diff_report):
        template = env.get_template(_TEMPLATE_EMPTY)
        return template.render(inputs=inputs)

    rows_by_bucket = build_port_rows(
        inputs.diff_report,
        inputs.correlation_map,
        inputs.priority_character,
    )

    non_priority_rows = _gather_non_priority_rows(rows_by_bucket, inputs.priority_character)
    encounter_rng_rows = _gather_encounter_rng_rows(rows_by_bucket)

    # Downstream-regen flags.
    bucket_names = set(rows_by_bucket.keys())
    has_combat_engine = BUCKET_COMBAT_ENGINE in bucket_names
    has_cards = BUCKET_CARDS in bucket_names
    has_monsters = BUCKET_MONSTERS in bucket_names

    template = env.get_template(_TEMPLATE_FULL)
    return template.render(
        inputs=inputs,
        rows_by_bucket=rows_by_bucket,
        PRIORITY_BUCKETS=PRIORITY_BUCKETS,
        IGNORE_BUCKETS=IGNORE_BUCKETS,
        bucket_titles=bucket_titles,
        future_character_rows=non_priority_rows,
        non_priority_count=len(non_priority_rows),
        encounter_rng_defer_rows=encounter_rng_rows,
        has_combat_engine_changes=has_combat_engine,
        has_cards_changes=has_cards,
        has_monster_changes=has_monsters,
    )


# --------------------------------------------------------------------------- #
# write_doc                                                                   #
# --------------------------------------------------------------------------- #


_SPECS_REL = Path("engine") / "headless" / "docs" / "specs"
_DOC_PREFIX_RE = re.compile(r"^(\d{2,})-")


def write_doc(rendered_markdown: str, monorepo_root: Path, version_range: str) -> Path:
    """Write rendered doc to engine/headless/docs/specs/0N-<version-range>-port-decisions.md.

    Idempotent: if a doc for the same ``version_range`` already exists, the
    same numeric prefix is reused and the file is overwritten — no
    duplicates.
    """
    specs_dir = monorepo_root / _SPECS_REL
    specs_dir.mkdir(parents=True, exist_ok=True)

    # Idempotency check: existing file containing the version_range slug.
    suffix_marker = f"-{version_range}-port-decisions.md"
    for existing in specs_dir.iterdir():
        if existing.is_file() and existing.name.endswith(suffix_marker):
            existing.write_text(rendered_markdown, encoding="utf-8")
            return existing.resolve()

    # Choose next prefix.
    max_prefix = 0
    for existing in specs_dir.iterdir():
        if not existing.is_file():
            continue
        m = _DOC_PREFIX_RE.match(existing.name)
        if m:
            try:
                num = int(m.group(1))
            except ValueError:
                continue
            max_prefix = max(max_prefix, num)
    next_prefix = max_prefix + 1
    name = f"{next_prefix:02d}-{version_range}-port-decisions.md"
    target = specs_dir / name
    target.write_text(rendered_markdown, encoding="utf-8")
    return target.resolve()
