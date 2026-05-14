"""Tests for upstream_sync.port_decisions: per-sync port-decision rendering.

Covers all 14 edge cases from the W5-A spec. Golden-file tests byte-compare
rendered output against committed fixtures.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from upstream_sync import port_decisions
from upstream_sync.correlate import CorrelationMap, Match
from upstream_sync.diff_analyze import (
    BUCKET_ART_AUDIO,
    BUCKET_CARDS,
    BUCKET_COMBAT_ENGINE,
    BUCKET_ENCOUNTERS,
    BUCKET_MODDING,
    BUCKET_MONSTERS,
    BUCKET_MULTIPLAYER,
    BUCKET_ORBS,
    BUCKET_OTHER,
    BUCKET_RELICS,
    BUCKET_ROOT_CONFIG,
    BUCKET_SCENES_GAMEPLAY,
    BUCKET_SCENES_UI,
    BUCKET_UI,
    DiffEntry,
    DiffReport,
)
from upstream_sync.port_decisions import (
    PortRow,
    Q4Advisory,
    RenderInputs,
    assign_decision,
    build_port_rows,
    build_q4_advisory,
    render,
    write_doc,
)


# --------------------------------------------------------------------------- #
# Builders                                                                    #
# --------------------------------------------------------------------------- #


def _entry(
    path: str,
    *,
    status: str = "M",
    character_tag: str | None = None,
    rename_from: str | None = None,
    rename_score: int | None = None,
) -> DiffEntry:
    return DiffEntry(
        status=status,  # type: ignore[arg-type]
        path=path,
        rename_from=rename_from,
        rename_score=rename_score,
        character_tag=character_tag,
        line_delta=None,
    )


def _report(
    buckets: dict[str, list[DiffEntry]],
    *,
    encounter_rng_defers: list[DiffEntry] | None = None,
    unmatched_paths: list[str] | None = None,
    from_tag: str = "v0.103.2",
    to_tag: str = "v0.105.1",
) -> DiffReport:
    return DiffReport(
        from_tag=from_tag,
        to_tag=to_tag,
        buckets=buckets,
        renames=[],
        character_tags_seen=set(),
        encounter_rng_defers=encounter_rng_defers or [],
        unmatched_paths=unmatched_paths or [],
        discovered_characters=set(),
    )


def _corr(matches: dict[str, list[Match]] | None = None) -> CorrelationMap:
    return CorrelationMap(matches=matches or {}, unmatched_notes=[])


def _match(
    diff_path: str,
    entity: str,
    *,
    gid: str = "1832",
    score: float = 1.0,
    excerpt: str = "Buffed Strike: damage 6→7",
) -> Match:
    return Match(
        diff_path=diff_path,
        note_gid=gid,
        note_title="Patch v0.105.1",
        section="content & balance",
        entity=entity,
        score=score,
        excerpt=excerpt,
    )


def _inputs(
    diff_report: DiffReport,
    *,
    correlation_map: CorrelationMap | None = None,
    q4_advisory: Q4Advisory | None = None,
    from_buildid: str | None = "22000000",
    to_buildid: str = "22000001",
    generated_at: str = "2026-05-14T00:00:00Z",
    tool_version: str = "0.1.0",
    priority_character: str = "Silent",
) -> RenderInputs:
    return RenderInputs(
        diff_report=diff_report,
        correlation_map=correlation_map or _corr(),
        q4_advisory=q4_advisory or Q4Advisory(added=[], removed=[]),
        from_buildid=from_buildid,
        to_buildid=to_buildid,
        generated_at=generated_at,
        tool_version=tool_version,
        priority_character=priority_character,
    )


# --------------------------------------------------------------------------- #
# assign_decision — one test per rule                                         #
# --------------------------------------------------------------------------- #


def test_assign_decision_multiplayer_bucket_ignored():
    entry = _entry("src/Core/Multiplayer/Lobby.cs")
    decision, trig, reason = assign_decision(entry, BUCKET_MULTIPLAYER)
    assert decision == "IGNORE"
    assert trig is None
    assert "multiplayer" in reason.lower()


def test_assign_decision_modding_bucket_ignored():
    entry = _entry("src/Core/Modding/Mod.cs")
    decision, _, _ = assign_decision(entry, BUCKET_MODDING)
    assert decision == "IGNORE"


def test_assign_decision_ui_only_bucket_ignored():
    entry = _entry("src/Core/UI/Foo.cs")
    decision, _, _ = assign_decision(entry, BUCKET_UI)
    assert decision == "IGNORE"


def test_assign_decision_art_audio_bucket_ignored():
    entry = _entry("src/Core/Audio/Bar.cs")
    decision, _, _ = assign_decision(entry, BUCKET_ART_AUDIO)
    assert decision == "IGNORE"


def test_assign_decision_orbs_deferred():
    entry = _entry("src/Core/Models/Orbs/LightningOrb.cs")
    decision, trig, reason = assign_decision(entry, BUCKET_ORBS)
    assert decision == "DEFER"
    assert trig is not None
    assert "orbs" in trig.lower() or "mechanics-notes" in trig.lower()
    assert "orb" in reason.lower()


def test_assign_decision_deleted_gameplay_file():
    entry = _entry("src/Core/Models/Cards/OldCard.cs", status="D")
    decision, trig, reason = assign_decision(entry, BUCKET_CARDS)
    assert decision == "DELETE"
    assert trig is None
    assert "removed" in reason.lower() or "delete" in reason.lower()


def test_assign_decision_added_gameplay_file():
    entry = _entry("src/Core/Models/Cards/NewCard.cs", status="A")
    decision, trig, reason = assign_decision(entry, BUCKET_CARDS)
    assert decision == "PORT"
    assert trig is None
    assert "cards" in reason.lower() or "new" in reason.lower()


def test_assign_decision_modified_gameplay_file():
    entry = _entry("src/Core/Models/Cards/Strike.cs", status="M")
    decision, trig, reason = assign_decision(entry, BUCKET_CARDS)
    assert decision == "PORT"
    assert trig is None
    assert "modified" in reason.lower() or "cards" in reason.lower()


def test_assign_decision_renamed_gameplay_file():
    entry = _entry(
        "src/Core/Models/Cards/NewName.cs",
        status="R",
        rename_from="src/Core/Models/Cards/OldName.cs",
        rename_score=95,
    )
    decision, trig, reason = assign_decision(entry, BUCKET_CARDS)
    assert decision == "PORT"
    assert trig is None
    assert "renamed" in reason.lower()
    assert "OldName" in reason or "src/Core/Models/Cards/OldName.cs" in reason


def test_assign_decision_scenes_ui_ignored():
    entry = _entry("scenes/menu/main.tscn")
    decision, _, reason = assign_decision(entry, BUCKET_SCENES_UI)
    assert decision == "IGNORE"
    assert "scene" in reason.lower() or "ui" in reason.lower()


def test_assign_decision_scenes_gameplay_ported():
    entry = _entry("scenes/combat/combat_scene.tscn")
    decision, _, reason = assign_decision(entry, BUCKET_SCENES_GAMEPLAY)
    assert decision == "PORT"
    assert "scene" in reason.lower() or "gameplay" in reason.lower()


def test_assign_decision_root_config_ported():
    entry = _entry("project.godot")
    decision, _, _ = assign_decision(entry, BUCKET_ROOT_CONFIG)
    assert decision == "PORT"


def test_assign_decision_other_bucket_surface_no_action():
    entry = _entry("misc/some_weird_path.cs")
    decision, _, reason = assign_decision(entry, BUCKET_OTHER)
    assert decision == "SURFACE-NO-ACTION"
    assert "allowlist" in reason.lower() or "unbucketed" in reason.lower()


# --------------------------------------------------------------------------- #
# build_port_rows                                                             #
# --------------------------------------------------------------------------- #


def test_build_port_rows_orders_by_path_within_bucket():
    e1 = _entry("src/Core/Models/Cards/Zoom.cs", status="A")
    e2 = _entry("src/Core/Models/Cards/Apple.cs", status="M")
    e3 = _entry("src/Core/Models/Cards/Banana.cs", status="M")
    report = _report({BUCKET_CARDS: [e1, e2, e3]})
    rows_by_bucket = build_port_rows(report, _corr(), "Silent")
    assert BUCKET_CARDS in rows_by_bucket
    paths = [r.path for r in rows_by_bucket[BUCKET_CARDS]]
    assert paths == sorted(paths)


def test_build_port_rows_omits_empty_buckets():
    report = _report({BUCKET_CARDS: []})
    rows_by_bucket = build_port_rows(report, _corr(), "Silent")
    assert BUCKET_CARDS not in rows_by_bucket or rows_by_bucket[BUCKET_CARDS] == []


def test_build_port_rows_pulls_correlation_hint_when_present():
    entry = _entry("src/Core/Models/Cards/Strike.cs", status="M")
    report = _report({BUCKET_CARDS: [entry]})
    corr = _corr(
        matches={
            "src/Core/Models/Cards/Strike.cs": [
                _match(
                    "src/Core/Models/Cards/Strike.cs",
                    "Strike",
                    gid="1832",
                    excerpt="Buffed Strike: damage 6 to 7",
                )
            ]
        }
    )
    rows = build_port_rows(report, corr, "Silent")[BUCKET_CARDS]
    assert len(rows) == 1
    hint = rows[0].patch_notes_hint
    assert hint is not None
    assert "PCN" in hint
    assert "1832" in hint
    assert "Strike" in hint


def test_build_port_rows_hint_none_when_no_correlation():
    entry = _entry("src/Core/Models/Cards/Strike.cs", status="M")
    report = _report({BUCKET_CARDS: [entry]})
    rows = build_port_rows(report, _corr(), "Silent")[BUCKET_CARDS]
    assert rows[0].patch_notes_hint is None


def test_build_port_rows_non_priority_character_surfaced():
    entry = _entry(
        "src/Core/Models/Cards/IroncladStrike.cs",
        status="M",
        character_tag="Ironclad",
    )
    report = _report({BUCKET_CARDS: [entry]})
    rows = build_port_rows(report, _corr(), "Silent")[BUCKET_CARDS]
    assert rows[0].decision == "SURFACE-NO-ACTION"
    assert rows[0].character_tag == "Ironclad"


def test_build_port_rows_priority_character_ported():
    entry = _entry(
        "src/Core/Models/Cards/SilentStrike.cs",
        status="M",
        character_tag="Silent",
    )
    report = _report({BUCKET_CARDS: [entry]})
    rows = build_port_rows(report, _corr(), "Silent")[BUCKET_CARDS]
    assert rows[0].decision == "PORT"


def test_build_port_rows_encounter_rng_defer():
    entry = _entry("src/Core/Models/Encounters/SlimesWeak.cs", status="M")
    report = _report(
        {BUCKET_ENCOUNTERS: [entry]},
        encounter_rng_defers=[entry],
    )
    rows = build_port_rows(report, _corr(), "Silent")[BUCKET_ENCOUNTERS]
    assert rows[0].decision == "DEFER"
    assert rows[0].re_eval_trigger is not None
    assert "B.1" in rows[0].re_eval_trigger or "encounter" in rows[0].re_eval_trigger.lower()


# --------------------------------------------------------------------------- #
# build_q4_advisory                                                           #
# --------------------------------------------------------------------------- #


def _write_card(tree: Path, rel_path: str, class_name: str) -> None:
    target = tree / rel_path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        "namespace Foo;\n"
        f"public class {class_name} : CardModel {{ }}\n",
        encoding="utf-8",
    )


def _write_monster(tree: Path, rel_path: str, class_name: str) -> None:
    target = tree / rel_path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        "namespace Foo;\n"
        f"public class {class_name} : MonsterModel {{ }}\n",
        encoding="utf-8",
    )


def test_build_q4_advisory_added_cards_and_monsters(tmp_path: Path):
    tree = tmp_path / "upstream"
    _write_card(tree, "src/Core/Models/Cards/Aeonglass.cs", "Aeonglass")
    _write_monster(tree, "src/Core/Models/Monsters/Crusher.cs", "Crusher")

    added_card = _entry("src/Core/Models/Cards/Aeonglass.cs", status="A")
    added_monster = _entry("src/Core/Models/Monsters/Crusher.cs", status="A")
    modified_card = _entry("src/Core/Models/Cards/Aeonglass.cs", status="M")

    report = _report(
        {
            BUCKET_CARDS: [added_card, modified_card],
            BUCKET_MONSTERS: [added_monster],
        }
    )
    advisory = build_q4_advisory(report, tree)
    ids = {entity_id for entity_id, _ in advisory.added}
    kinds = {kind for _, kind in advisory.added}
    assert "Aeonglass" in ids
    assert "Crusher" in ids
    assert "card" in kinds
    assert "monster" in kinds


def test_build_q4_advisory_deleted_file_inferred_from_bucket(tmp_path: Path):
    tree = tmp_path / "upstream"
    tree.mkdir(parents=True)  # file doesn't exist — deleted

    deleted = _entry("src/Core/Models/Cards/Removed.cs", status="D")
    report = _report({BUCKET_CARDS: [deleted]})
    advisory = build_q4_advisory(report, tree)
    assert ("Removed", "card") in advisory.removed


# --------------------------------------------------------------------------- #
# render — empty diff short-circuit + smoke golden                            #
# --------------------------------------------------------------------------- #


GOLDEN_DIR = Path(__file__).parent / "fixtures"


def _read_golden(name: str) -> str:
    return (GOLDEN_DIR / name).read_text(encoding="utf-8")


def test_render_empty_diff_short_circuits():
    report = _report({})  # no buckets, no entries
    out = render(_inputs(report))
    expected = _read_golden("golden_port_decisions.empty.md")
    assert out == expected


def test_render_empty_diff_with_empty_bucket_lists():
    """Bucket present but empty still counts as empty."""
    report = _report({BUCKET_CARDS: [], BUCKET_RELICS: []})
    out = render(_inputs(report))
    # Should be the empty-template output, not full structure
    assert "no changes" in out.lower() or "no source-tracked" in out.lower()


def _smoke_inputs() -> RenderInputs:
    entries_cards = [
        _entry("src/Core/Models/Cards/SilentStrike.cs", status="M", character_tag="Silent"),
        _entry("src/Core/Models/Cards/NewCard.cs", status="A"),
    ]
    entries_monsters = [
        _entry("src/Core/Models/Monsters/Crusher.cs", status="A"),
    ]
    entries_ironclad = [
        _entry(
            "src/Core/Models/Cards/IroncladStrike.cs",
            status="M",
            character_tag="Ironclad",
        ),
    ]
    entries_orbs = [
        _entry("src/Core/Models/Orbs/LightningOrb.cs", status="M"),
    ]
    entries_ui = [
        _entry("src/Core/UI/MainMenu.cs", status="M"),
    ]
    report = _report(
        {
            BUCKET_CARDS: sorted(
                entries_cards + entries_ironclad, key=lambda e: e.path
            ),
            BUCKET_MONSTERS: entries_monsters,
            BUCKET_ORBS: entries_orbs,
            BUCKET_UI: entries_ui,
        },
        unmatched_paths=[],
    )
    corr = _corr(
        matches={
            "src/Core/Models/Cards/SilentStrike.cs": [
                _match(
                    "src/Core/Models/Cards/SilentStrike.cs",
                    "SilentStrike",
                    gid="1832",
                    excerpt="Buffed SilentStrike: damage 6 to 7",
                )
            ]
        }
    )
    advisory = Q4Advisory(
        added=[("NewCard", "card"), ("Crusher", "monster")],
        removed=[],
    )
    return _inputs(
        report,
        correlation_map=corr,
        q4_advisory=advisory,
        from_buildid="22000000",
        to_buildid="22000001",
        generated_at="2026-05-14T00:00:00Z",
        tool_version="0.1.0",
        priority_character="Silent",
    )


def test_render_smoke_matches_golden():
    out = render(_smoke_inputs())
    expected = _read_golden("golden_port_decisions.smoke.md")
    assert out == expected


def test_render_non_empty_includes_summary():
    out = render(_smoke_inputs())
    assert "## Summary" in out
    assert "| Bucket |" in out


def test_render_includes_future_character_section():
    out = render(_smoke_inputs())
    assert "Future-character" in out
    assert "Ironclad" in out


def test_render_unmatched_paths_section_only_when_non_empty():
    """Unmatched section is omitted when no unmatched paths."""
    out = render(_smoke_inputs())
    assert "Unmatched upstream paths" not in out

    entry = _entry("src/Core/Models/Cards/Strike.cs", status="M")
    report = _report(
        {BUCKET_CARDS: [entry], BUCKET_OTHER: [_entry("weird/path/x.cs")]},
        unmatched_paths=["weird/path/x.cs"],
    )
    out2 = render(_inputs(report))
    assert "Unmatched upstream paths" in out2
    assert "weird/path/x.cs" in out2


def test_render_patch_notes_hint_format():
    out = render(_smoke_inputs())
    # PCN format: "PCN: '<excerpt>' (gid <gid>)"
    assert "PCN:" in out
    assert "1832" in out


def test_render_downstream_regen_conditional_on_combat_engine():
    entry = _entry("src/Core/Combat/CombatEngine.cs", status="M")
    report = _report({BUCKET_COMBAT_ENGINE: [entry]})
    out = render(_inputs(report))
    assert "seed-pinner" in out


def test_render_downstream_regen_conditional_on_cards():
    entry = _entry("src/Core/Models/Cards/Strike.cs", status="M")
    report = _report({BUCKET_CARDS: [entry]})
    out = render(_inputs(report))
    assert "seed-pinner" in out


def test_render_downstream_regen_skip_seed_pinner_for_only_ignore_buckets():
    entry = _entry("src/Core/UI/Foo.cs", status="M")
    report = _report({BUCKET_UI: [entry]})
    out = render(_inputs(report))
    # Should not recommend seed-pinner (no gameplay changes)
    assert "seed-pinner" not in out


# --------------------------------------------------------------------------- #
# write_doc                                                                   #
# --------------------------------------------------------------------------- #


def _setup_monorepo(tmp_path: Path) -> Path:
    root = tmp_path / "monorepo"
    specs = root / "engine" / "headless" / "docs" / "specs"
    specs.mkdir(parents=True)
    return root


def test_write_doc_assigns_next_prefix(tmp_path: Path):
    root = _setup_monorepo(tmp_path)
    specs = root / "engine" / "headless" / "docs" / "specs"
    (specs / "01-foo.md").write_text("dummy", encoding="utf-8")
    (specs / "02-bar.md").write_text("dummy", encoding="utf-8")

    path = write_doc("rendered", root, "v0.103.2-to-v0.105.1")
    assert path.is_absolute()
    assert path.name.startswith("03-")
    assert "v0.103.2-to-v0.105.1" in path.name
    assert path.read_text(encoding="utf-8") == "rendered"


def test_write_doc_idempotent_overwrites_same_version_range(tmp_path: Path):
    root = _setup_monorepo(tmp_path)
    specs = root / "engine" / "headless" / "docs" / "specs"
    (specs / "01-existing.md").write_text("dummy", encoding="utf-8")

    p1 = write_doc("first", root, "v0.103.2-to-v0.105.1")
    p2 = write_doc("second", root, "v0.103.2-to-v0.105.1")
    assert p1 == p2
    assert p2.read_text(encoding="utf-8") == "second"
    # No duplicate; only one matching file in dir
    matches = [p for p in specs.iterdir() if "v0.103.2-to-v0.105.1" in p.name]
    assert len(matches) == 1


def test_write_doc_first_doc_in_empty_specs(tmp_path: Path):
    root = _setup_monorepo(tmp_path)
    path = write_doc("rendered", root, "v0.0.1-to-v0.0.2")
    assert path.name.startswith("01-")


def test_write_doc_skips_non_numeric_prefix(tmp_path: Path):
    """Files like ``modules/foo.md`` or ``readme.md`` shouldn't break prefix detection."""
    root = _setup_monorepo(tmp_path)
    specs = root / "engine" / "headless" / "docs" / "specs"
    (specs / "readme.md").write_text("not numbered", encoding="utf-8")
    (specs / "01-real.md").write_text("dummy", encoding="utf-8")
    path = write_doc("rendered", root, "v0.0.5-to-v0.0.6")
    assert path.name.startswith("02-")


# --------------------------------------------------------------------------- #
# Edge case: encounter-RNG DEFER rows in rendered output                      #
# --------------------------------------------------------------------------- #


def test_render_lists_encounter_rng_defer_rows():
    entry = _entry("src/Core/Models/Encounters/SlimesWeak.cs", status="M")
    report = _report(
        {BUCKET_ENCOUNTERS: [entry]},
        encounter_rng_defers=[entry],
    )
    out = render(_inputs(report))
    # Section heading exists and lists the deferred encounter
    assert "Encounter-RNG-driven spawns" in out
    assert "SlimesWeak.cs" in out


# --------------------------------------------------------------------------- #
# Q4 advisory: rename handling                                                #
# --------------------------------------------------------------------------- #


def test_build_q4_advisory_rename_adds_new_and_removes_old(tmp_path: Path):
    tree = tmp_path / "upstream"
    _write_card(tree, "src/Core/Models/Cards/NewName.cs", "NewName")
    renamed = _entry(
        "src/Core/Models/Cards/NewName.cs",
        status="R",
        rename_from="src/Core/Models/Cards/OldName.cs",
        rename_score=95,
    )
    report = _report({BUCKET_CARDS: [renamed]})
    advisory = build_q4_advisory(report, tree)
    added_ids = {eid for eid, _ in advisory.added}
    removed_ids = {eid for eid, _ in advisory.removed}
    assert "NewName" in added_ids
    assert "OldName" in removed_ids


# --------------------------------------------------------------------------- #
# assign_decision: encounters bucket modified-file falls through to PORT      #
# --------------------------------------------------------------------------- #


def test_assign_decision_encounters_modified_ports():
    entry = _entry("src/Core/Models/Encounters/StaticEncounter.cs", status="M")
    decision, _, _ = assign_decision(entry, BUCKET_ENCOUNTERS)
    assert decision == "PORT"
