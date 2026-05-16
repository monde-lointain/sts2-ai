"""Tests for upstream_sync.entity_extract: class-decl parsing + char discovery."""

from __future__ import annotations

from pathlib import Path

import pytest

from upstream_sync import entity_extract
from upstream_sync.entity_extract import (
    BASE_TO_KIND,
    Entity,
    discover_characters,
    extract_entities,
    warn_on_roster_drift,
)

FIXTURES = Path(__file__).parent / "fixtures"


# ---------- extract_entities ----------


def test_extract_sample_card():
    """Edge case 1: sample_card.cs → single Entity(StrikeSilent, card)."""
    path = FIXTURES / "sample_card.cs"
    entities = extract_entities(path)
    assert len(entities) == 1
    e = entities[0]
    assert e.id == "StrikeSilent"
    assert e.kind == "card"
    assert e.file_path == path


def test_extract_no_matches(tmp_path):
    """Edge case 2: file with no class decl → []."""
    f = tmp_path / "empty.cs"
    f.write_text("namespace Foo;\n// nothing here\n")
    assert extract_entities(f) == []


def test_extract_missing_file(tmp_path):
    """Edge case 3: missing file → [] (not error)."""
    assert extract_entities(tmp_path / "does_not_exist.cs") == []


def test_extract_skips_line_comment(tmp_path):
    """Edge case 4: class decl inside // comment → [] (skipped)."""
    f = tmp_path / "lc.cs"
    f.write_text("namespace Foo;\n// public class FakeCard : CardModel { }\n")
    assert extract_entities(f) == []


def test_extract_skips_block_comment(tmp_path):
    """Edge case 5: class decl inside /* */ block → [] (skipped)."""
    f = tmp_path / "bc.cs"
    f.write_text("namespace Foo;\n/* public class FakeRelic : RelicModel { }\n   another line */\n")
    assert extract_entities(f) == []


def test_extract_two_matches(tmp_path):
    """Edge case 6: two matching classes → two Entities."""
    f = tmp_path / "two.cs"
    f.write_text(
        "namespace Foo;\npublic class A : CardModel { }\npublic sealed class B : RelicModel { }\n"
    )
    ents = extract_entities(f)
    assert len(ents) == 2
    ids = {e.id for e in ents}
    kinds = {e.id: e.kind for e in ents}
    assert ids == {"A", "B"}
    assert kinds["A"] == "card"
    assert kinds["B"] == "relic"


def test_extract_ignores_non_matching_base(tmp_path):
    """Edge case 7: non-matching base classes not in BASE_TO_KIND → ignored."""
    f = tmp_path / "nm.cs"
    f.write_text("namespace Foo;\npublic class Foo : SomeBase { }\npublic class Bar : Object { }\n")
    assert extract_entities(f) == []


def test_extract_skips_string_literal(tmp_path):
    """Spec mandates string-literal skipping."""
    f = tmp_path / "str.cs"
    f.write_text('namespace Foo;\nstring source = "public class Ghost : CardModel { }";\n')
    assert extract_entities(f) == []


def test_extract_all_kinds_covered(tmp_path):
    """Smoke: every base in BASE_TO_KIND maps to its declared kind."""
    body = "namespace Foo;\n"
    for i, base in enumerate(BASE_TO_KIND):
        body += f"public class C{i} : {base} {{ }}\n"
    f = tmp_path / "all.cs"
    f.write_text(body)
    ents = extract_entities(f)
    found = {e.kind for e in ents}
    assert found == set(BASE_TO_KIND.values())


def test_entity_is_frozen():
    """Entity must be a frozen dataclass."""
    e = Entity(id="Foo", kind="card", file_path=Path("/tmp/x.cs"))
    with pytest.raises(Exception):
        e.id = "Bar"  # type: ignore[misc]


# ---------- discover_characters ----------


def test_discover_subdirs_and_classes_union():
    """Edge case 8: subdir + class match are unioned."""
    root = FIXTURES / "sample_upstream_tree"
    found = discover_characters(root)
    assert found == {"Silent", "Defect", "Ironclad"}


def test_discover_only_subdirs(tmp_path):
    """Edge case 9: subdirs only, no class matches → subdir names."""
    chars = tmp_path / "src" / "Core" / "Models" / "Characters"
    chars.mkdir(parents=True)
    (chars / "Watcher").mkdir()
    (chars / "Hermit").mkdir()
    assert discover_characters(tmp_path) == {"Watcher", "Hermit"}


def test_discover_only_class_matches(tmp_path):
    """Edge case 10: class matches but no subdirs under Characters/."""
    chars = tmp_path / "src" / "Core" / "Models" / "Characters"
    chars.mkdir(parents=True)
    # Class file elsewhere under src/ — no Characters/* subdir
    other = tmp_path / "src" / "Core" / "Other"
    other.mkdir(parents=True)
    (other / "Sneak.cs").write_text("namespace Foo;\npublic class Sneak : CharacterModel { }\n")
    assert discover_characters(tmp_path) == {"Sneak"}


def test_discover_missing_characters_dir(tmp_path):
    """Edge case 11: tree without Characters/ → empty set (no error)."""
    (tmp_path / "src").mkdir()
    assert discover_characters(tmp_path) == set()


def test_discover_completely_empty_tree(tmp_path):
    """Even a totally bare tree shouldn't error."""
    assert discover_characters(tmp_path) == set()


def test_discover_ignores_files_in_characters_dir(tmp_path):
    """Files directly in Characters/ are NOT subdirs — only true subdirs count."""
    chars = tmp_path / "src" / "Core" / "Models" / "Characters"
    chars.mkdir(parents=True)
    (chars / "README.md").write_text("# stub\n")
    # No subdirs, no class matches → empty.
    assert discover_characters(tmp_path) == set()


# ---------- warn_on_roster_drift ----------


def test_drift_added():
    """Edge case 12: added single character."""
    msg = warn_on_roster_drift({"Silent", "Defect"}, {"Silent", "Defect", "Watcher"})
    assert msg == "Character roster changed: added Watcher"


def test_drift_no_change():
    """Edge case 13: identical sets → None."""
    assert warn_on_roster_drift({"Silent"}, {"Silent"}) is None


def test_drift_removed():
    """Edge case 14: removed single character."""
    msg = warn_on_roster_drift({"Silent", "Doormaker"}, {"Silent"})
    assert msg == "Character roster changed: removed Doormaker"


def test_drift_added_and_removed_sorted():
    """Both added and removed; names sorted."""
    msg = warn_on_roster_drift(
        {"Silent", "Doormaker", "Bilge"},
        {"Silent", "Watcher", "Ascended"},
    )
    assert msg == ("Character roster changed: added Ascended, Watcher; removed Bilge, Doormaker")


def test_drift_empty_to_populated():
    msg = warn_on_roster_drift(set(), {"Silent"})
    assert msg == "Character roster changed: added Silent"


def test_drift_populated_to_empty():
    msg = warn_on_roster_drift({"Silent"}, set())
    assert msg == "Character roster changed: removed Silent"


# ---------- module hygiene ----------


def test_module_stdlib_only():
    """Spec: module imports only stdlib (re, pathlib, dataclasses, typing)."""
    import ast

    src = Path(entity_extract.__file__).read_text()
    tree = ast.parse(src)
    allowed = {"re", "pathlib", "dataclasses", "typing", "__future__"}
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                root = alias.name.split(".")[0]
                assert root in allowed, f"forbidden import: {alias.name}"
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".")[0]
            assert root in allowed, f"forbidden import: {node.module}"
