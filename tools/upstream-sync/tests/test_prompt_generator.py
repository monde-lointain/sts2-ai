"""Tests for upstream_sync.prompt_generator.

Snapshot tests use frozen fixtures in tests/fixtures/mini_port_decisions.json
and tests/snapshots/*.txt — avoids drift when the live port-decisions doc changes.

Snapshot generation: run with UPDATE_SNAPSHOTS=1 to regenerate.
"""

from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from upstream_sync.diff_analyze import BUCKET_CARDS, BUCKET_MONSTERS
from upstream_sync.prompt_generator import (
    PromptInputs,
    _RowView,
    load_row_from_sidecar,
    render_prompt,
    template_for_bucket,
)

FIXTURES = Path(__file__).parent / "fixtures"
SNAPSHOTS = Path(__file__).parent / "snapshots"

_MINI_SIDECAR = FIXTURES / "mini_port_decisions.json"

# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------


def _make_inputs(row: dict, **overrides) -> PromptInputs:
    defaults: dict = {
        "row": row,
        "version": "v0.105.1",
        "wave": "5",
        "stream_id": "B.2",
        "expected_sha": "274beca9d2dd3307248575f0adefa90bf270746c",
    }
    defaults.update(overrides)
    return PromptInputs(**defaults)


def _load_mini_rows() -> list[dict]:
    data = json.loads(_MINI_SIDECAR.read_text(encoding="utf-8"))
    return data["rows"]


def _row_by_path(rows: list[dict], path: str) -> dict:
    for r in rows:
        if r["path"] == path:
            return r
    raise KeyError(path)


# ---------------------------------------------------------------------------
# template_for_bucket
# ---------------------------------------------------------------------------


def test_template_for_bucket_monsters():
    assert template_for_bucket(BUCKET_MONSTERS) == "monster.j2"


def test_template_for_bucket_cards_fallback():
    assert template_for_bucket(BUCKET_CARDS) == "generic-port.j2"


def test_template_for_bucket_unknown_fallback():
    assert template_for_bucket("totally-unknown-bucket") == "generic-port.j2"


# ---------------------------------------------------------------------------
# _RowView
# ---------------------------------------------------------------------------


def test_row_view_attribute_access():
    view = _RowView({"path": "foo.cs", "decision": "PORT"})
    assert view.path == "foo.cs"
    assert view.decision == "PORT"


def test_row_view_missing_attribute_returns_none():
    view = _RowView({"path": "foo.cs"})
    assert view.character_tag is None


def test_row_view_repr():
    view = _RowView({"x": 1})
    assert "_RowView" in repr(view)


# ---------------------------------------------------------------------------
# render_prompt — structural checks (not byte-exact)
# ---------------------------------------------------------------------------


def test_render_prompt_monster_contains_preflight():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert "PRE-FLIGHT" in result
    assert "274beca" in result


def test_render_prompt_monster_contains_row_path():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert "LeafSlime.cs" in result


def test_render_prompt_monster_contains_version():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert "v0.105.1" in result


def test_render_prompt_monster_uses_monster_template():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    # monster.j2 specific content
    assert "GenerateMonsters" in result or "stat block" in result


def test_render_prompt_generic_fallback_for_cards():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Cards/Strike_Silent.cs")
    result = render_prompt(_make_inputs(row))
    assert "PRE-FLIGHT" in result
    assert "DISPATCHER" in result  # generic-port.j2 placeholder marker


def test_render_prompt_contains_absolute_path_mandate():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert "ABSOLUTE-PATH MANDATE" in result


def test_render_prompt_contains_owned_forbidden_sections():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert "OWNED" in result or "Files OWNED" in result
    assert "FORBIDDEN" in result or "Files FORBIDDEN" in result


def test_render_prompt_ends_with_newline():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    assert result.endswith("\n")


# ---------------------------------------------------------------------------
# load_row_from_sidecar
# ---------------------------------------------------------------------------


def test_load_row_from_sidecar_found():
    row = load_row_from_sidecar(_MINI_SIDECAR, "src/Core/Models/Monsters/LeafSlime.cs")
    assert row["decision"] == "PORT"
    assert row["bucket"] == "monsters"


def test_load_row_from_sidecar_not_found():
    with pytest.raises(ValueError, match="No row with path"):
        load_row_from_sidecar(_MINI_SIDECAR, "nonexistent/path.cs")


def test_load_row_from_sidecar_file_not_found():
    with pytest.raises(FileNotFoundError):
        load_row_from_sidecar(FIXTURES / "does_not_exist.json", "x")


# ---------------------------------------------------------------------------
# Snapshot tests
# ---------------------------------------------------------------------------

_UPDATE = os.environ.get("UPDATE_SNAPSHOTS", "").lower() in ("1", "true", "yes")


def _snapshot_path(name: str) -> Path:
    return SNAPSHOTS / name


def _check_or_update(name: str, actual: str) -> None:
    snap = _snapshot_path(name)
    if _UPDATE:
        SNAPSHOTS.mkdir(parents=True, exist_ok=True)
        snap.write_text(actual, encoding="utf-8")
        return
    if not snap.exists():
        pytest.fail(f"Snapshot {snap} does not exist. Run with UPDATE_SNAPSHOTS=1 to generate.")
    expected = snap.read_text(encoding="utf-8")
    assert actual == expected, (
        f"Snapshot mismatch for {name}. Run with UPDATE_SNAPSHOTS=1 to update."
    )


def test_snapshot_monster_prompt():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Monsters/LeafSlime.cs")
    result = render_prompt(_make_inputs(row))
    _check_or_update("monster_LeafSlime.txt", result)


def test_snapshot_generic_card_prompt():
    rows = _load_mini_rows()
    row = _row_by_path(rows, "src/Core/Models/Cards/Strike_Silent.cs")
    result = render_prompt(_make_inputs(row))
    _check_or_update("generic_Strike_Silent.txt", result)


# ---------------------------------------------------------------------------
# Concern #4 — Hint-present + hint-absent snapshot tests
# ---------------------------------------------------------------------------


def _make_untouchable_row() -> dict:
    """Synthesize a port-decision row for Untouchable.cs with a populated hint."""
    return {
        "path": "src/Core/Models/Cards/Untouchable.cs",
        "bucket": "cards",
        "git_status": "M",
        "line_delta": 4,
        "character_tag": "Silent",
        "decision": "PORT",
        "status": "PENDING",
        "re_eval_trigger": None,
        "patch_notes_hint": {
            "change_type": "buffed",
            "magnitude": "+2→+3",
            "version": "Beta Hotfix Notes - v0.105.1",
            "excerpt": "Buffed Untouchable card: upgraded Block gain increased From +2 -> +3",
            "gid": "1832065502816737",
            "claim_only_candidate": False,
        },
        "rationale": "Modified file in cards",
        "wave": 10.5,
        "stream_id": "10.5.α",
    }


def _make_no_hint_row() -> dict:
    """Synthesize a port-decision row with no patch-notes hint."""
    return {
        "path": "src/Core/Models/Cards/SilentStrike.cs",
        "bucket": "cards",
        "git_status": "M",
        "line_delta": 2,
        "character_tag": "Silent",
        "decision": "PORT",
        "status": "PENDING",
        "re_eval_trigger": None,
        "patch_notes_hint": None,
        "rationale": "Modified file in cards",
        "wave": None,
        "stream_id": None,
    }


def test_hint_present_contains_header():
    """Hint-present: prompt must contain the ## ⚠ Patch-notes hint header."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "## ⚠ Patch-notes hint" in result


def test_hint_present_contains_change_type_block():
    """Hint-present: prompt must contain the Change type line for Untouchable."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "**Change type**" in result
    assert "buffed" in result


def test_hint_present_contains_magnitude():
    """Hint-present: magnitude is rendered when non-null."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "+2→+3" in result


def test_hint_present_contains_excerpt():
    """Hint-present: excerpt text is surfaced."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "Buffed Untouchable card" in result


def test_hint_present_contains_gid():
    """Hint-present: gid is referenced."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "1832065502816737" in result


def test_hint_present_cross_check_instruction():
    """Hint-present: REQUIRED cross-check instruction is present."""
    row = _make_untouchable_row()
    result = render_prompt(_make_inputs(row))
    assert "REQUIRED" in result
    assert "patch-notes-claim-vs-code-diff-mismatch" in result


def test_hint_absent_contains_header():
    """Hint-absent: prompt still contains the ## ⚠ Patch-notes hint header."""
    row = _make_no_hint_row()
    result = render_prompt(_make_inputs(row))
    assert "## ⚠ Patch-notes hint" in result


def test_hint_absent_contains_no_hint_line():
    """Hint-absent: the 'no hint surfaced' fallback line is present."""
    row = _make_no_hint_row()
    result = render_prompt(_make_inputs(row))
    assert "No patch-notes hint surfaced for this row" in result


def test_hint_absent_no_change_type_block():
    """Hint-absent: no Change type block rendered."""
    row = _make_no_hint_row()
    result = render_prompt(_make_inputs(row))
    assert "**Change type**" not in result


def test_hint_present_monster_template():
    """Hint-present works for monster.j2 as well."""
    row = _make_untouchable_row()
    row = {**row, "bucket": "monsters", "path": "src/Core/Models/Monsters/InkyMonster.cs"}
    result = render_prompt(_make_inputs(row))
    assert "## ⚠ Patch-notes hint" in result
    assert "**Change type**" in result
    assert "buffed" in result


def test_hint_absent_monster_template():
    """Hint-absent fallback works for monster.j2."""
    row = _make_no_hint_row()
    row = {**row, "bucket": "monsters", "path": "src/Core/Models/Monsters/SomeMonster.cs"}
    result = render_prompt(_make_inputs(row))
    assert "## ⚠ Patch-notes hint" in result
    assert "No patch-notes hint surfaced for this row" in result
