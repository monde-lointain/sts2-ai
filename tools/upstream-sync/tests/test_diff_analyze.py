"""Tests for upstream_sync.diff_analyze: path categorization + diff parsing.

Covers all 17 edge cases from the W3-A spec. Uses subprocess injection via
`_subprocess_run` and tmp_path-backed upstream trees — no real `git` calls.
"""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import pytest

from upstream_sync import diff_analyze
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
    analyze_diff,
    bucket_for_path,
    character_tag_for_path,
    is_encounter_rng_defer,
)

FIXTURES = Path(__file__).parent / "fixtures"


# ---------- bucket_for_path (Edge case 1: each pattern) ----------


def test_bucket_multiplayer():
    assert bucket_for_path("src/Core/Multiplayer/Host.cs") == BUCKET_MULTIPLAYER


def test_bucket_modding():
    assert bucket_for_path("src/Core/Modding/Loader.cs") == BUCKET_MODDING


def test_bucket_ui_core_ui():
    assert bucket_for_path("src/Core/UI/Menu.cs") == BUCKET_UI


def test_bucket_ui_localization():
    assert bucket_for_path("src/Core/Localization/Strings.cs") == BUCKET_UI


def test_bucket_ui_helpers_localization():
    assert bucket_for_path("src/Core/Helpers/Localization/L10n.cs") == BUCKET_UI


def test_bucket_art_audio_audio():
    assert bucket_for_path("src/Core/Audio/SoundBank.cs") == BUCKET_ART_AUDIO


def test_bucket_art_audio_vfx():
    assert bucket_for_path("src/Core/VFX/Particles.cs") == BUCKET_ART_AUDIO


def test_bucket_art_audio_animations():
    assert bucket_for_path("src/Core/Animations/Rig.cs") == BUCKET_ART_AUDIO


def test_bucket_random():
    assert bucket_for_path("src/Core/Random/Rng.cs") == BUCKET_RANDOM


def test_bucket_combat_engine_combat():
    assert bucket_for_path("src/Core/Combat/CombatManager.cs") == BUCKET_COMBAT_ENGINE


def test_bucket_combat_engine_hooks():
    assert bucket_for_path("src/Core/Hooks/HookManager.cs") == BUCKET_COMBAT_ENGINE


def test_bucket_combat_engine_gameactions():
    assert bucket_for_path("src/Core/GameActions/AttackAction.cs") == BUCKET_COMBAT_ENGINE


def test_bucket_combat_engine_commands():
    assert bucket_for_path("src/Core/Commands/PlayCard.cs") == BUCKET_COMBAT_ENGINE


def test_bucket_cards():
    assert bucket_for_path("src/Core/Models/Cards/StrikeSilent.cs") == BUCKET_CARDS


def test_bucket_relics():
    assert bucket_for_path("src/Core/Models/Relics/RubyHeart.cs") == BUCKET_RELICS


def test_bucket_powers():
    assert bucket_for_path("src/Core/Models/Powers/StrengthPower.cs") == BUCKET_POWERS


def test_bucket_monsters():
    assert bucket_for_path("src/Core/Models/Monsters/Aeonglass.cs") == BUCKET_MONSTERS


def test_bucket_encounters():
    assert bucket_for_path("src/Core/Models/Encounters/CultistsNormal.cs") == BUCKET_ENCOUNTERS


def test_bucket_events():
    assert bucket_for_path("src/Core/Models/Events/MysteriousSphere.cs") == BUCKET_EVENTS


def test_bucket_potions():
    assert bucket_for_path("src/Core/Models/Potions/HealPotion.cs") == BUCKET_POTIONS


def test_bucket_afflictions():
    assert bucket_for_path("src/Core/Models/Afflictions/Weak.cs") == BUCKET_AFFLICTIONS


def test_bucket_enchantments():
    assert bucket_for_path("src/Core/Models/Enchantments/Ench.cs") == BUCKET_ENCHANTMENTS


def test_bucket_modifiers():
    assert bucket_for_path("src/Core/Models/Modifiers/Mod.cs") == BUCKET_MODIFIERS


def test_bucket_acts():
    assert bucket_for_path("src/Core/Models/Acts/Act1.cs") == BUCKET_ACTS


def test_bucket_characters():
    assert bucket_for_path("src/Core/Models/Characters/Silent/Silent.cs") == BUCKET_CHARACTERS


def test_bucket_card_pools():
    assert bucket_for_path("src/Core/Models/CardPools/SilentPool.cs") == BUCKET_CARD_POOLS


def test_bucket_relic_pools():
    assert bucket_for_path("src/Core/Models/RelicPools/CommonPool.cs") == BUCKET_RELIC_POOLS


def test_bucket_potion_pools():
    assert bucket_for_path("src/Core/Models/PotionPools/PotionPool.cs") == BUCKET_POTION_POOLS


def test_bucket_orbs():
    assert bucket_for_path("src/Core/Models/Orbs/LightningOrb.cs") == BUCKET_ORBS


# Edge case 10: model-bases regex
def test_bucket_model_bases_card_model():
    assert bucket_for_path("src/Core/Models/CardModel.cs") == BUCKET_MODEL_BASES


def test_bucket_model_bases_monster_model():
    assert bucket_for_path("src/Core/Models/MonsterModel.cs") == BUCKET_MODEL_BASES


def test_bucket_model_bases_model_db():
    assert bucket_for_path("src/Core/Models/ModelDb.cs") == BUCKET_MODEL_BASES


def test_bucket_model_bases_NOT_subdir_path():
    # Subdir path goes to cards, NOT model-bases
    assert bucket_for_path("src/Core/Models/Cards/StrikeSilent.cs") == BUCKET_CARDS


def test_bucket_model_bases_NOT_random_xmodel():
    # CardModel.cs in a sub-dir should NOT match model-bases regex
    assert bucket_for_path("src/Core/Models/Cards/CardModel.cs") == BUCKET_CARDS


# Edge case 11: scenes-gameplay vs scenes-ui
def test_bucket_scenes_gameplay_combat():
    assert bucket_for_path("scenes/combat/combat.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_encounters():
    assert bucket_for_path("scenes/encounters/cultists.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_cards():
    assert bucket_for_path("scenes/cards/card_view.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_orbs():
    assert bucket_for_path("scenes/orbs/lightning.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_relics():
    assert bucket_for_path("scenes/relics/ruby.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_creature_visuals():
    assert bucket_for_path("scenes/creature_visuals/snake.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_gameplay_rooms():
    assert bucket_for_path("scenes/rooms/elite.tscn") == BUCKET_SCENES_GAMEPLAY


def test_bucket_scenes_ui_main_menu():
    assert bucket_for_path("scenes/main_menu/main_menu.tscn") == BUCKET_SCENES_UI


def test_bucket_scenes_ui_settings():
    assert bucket_for_path("scenes/settings/audio.tscn") == BUCKET_SCENES_UI


# Edge case 12: root-config
def test_bucket_root_config_csproj():
    assert bucket_for_path("sts2.csproj") == BUCKET_ROOT_CONFIG


def test_bucket_root_config_sln():
    assert bucket_for_path("sts2.sln") == BUCKET_ROOT_CONFIG


def test_bucket_root_config_project_godot():
    assert bucket_for_path("project.godot") == BUCKET_ROOT_CONFIG


def test_bucket_root_config_global_json():
    assert bucket_for_path("global.json") == BUCKET_ROOT_CONFIG


def test_bucket_root_config_packages_lock():
    assert bucket_for_path("packages.lock.json") == BUCKET_ROOT_CONFIG


def test_bucket_root_config_NOT_nested():
    # Nested .csproj falls through to "other"
    assert bucket_for_path("subdir/foo.csproj") == BUCKET_OTHER


# Edge case 9: other fallthrough
def test_bucket_other_unrelated():
    assert bucket_for_path("unrelated/something/new.txt") == BUCKET_OTHER


def test_bucket_other_empty():
    assert bucket_for_path("README.md") == BUCKET_OTHER


def test_bucket_match_order_multiplayer_takes_precedence():
    # If a path looked like both multiplayer and combat-engine, multiplayer wins.
    # (Synthetic — real source wouldn't nest these, but order must be deterministic.)
    assert bucket_for_path("src/Core/Multiplayer/CombatSync.cs") == BUCKET_MULTIPLAYER


# ---------- character_tag_for_path (Edge cases 4, 5, 6) ----------


def test_character_tag_strikesilent_silent():
    """Edge case 4: filename includes character name."""
    result = character_tag_for_path("src/Core/Models/Cards/StrikeSilent.cs", {"Silent", "Defect"})
    assert result == "Silent"


def test_character_tag_character_dir():
    """Path component matches character (Characters/Defect/Defect.cs)."""
    result = character_tag_for_path(
        "src/Core/Models/Characters/Defect/Defect.cs",
        {"Silent", "Defect", "Ironclad"},
    )
    assert result == "Defect"


def test_character_tag_no_match():
    """Edge case 5: no character in path → None."""
    assert character_tag_for_path("src/Core/Combat/CombatManager.cs", {"Silent", "Defect"}) is None


def test_character_tag_case_insensitive():
    """Edge case 6: 'striksilent.cs' theoretical → 'Silent'."""
    result = character_tag_for_path("striksilent.cs", {"Silent", "Defect"})
    assert result == "Silent"


def test_character_tag_empty_roster():
    """Empty roster → always None."""
    assert character_tag_for_path("src/Core/Models/Cards/StrikeSilent.cs", set()) is None


def test_character_tag_returns_canonical_casing():
    """The returned name uses canonical casing from the roster, not the path."""
    # Path has lowercase, roster has canonical casing
    result = character_tag_for_path("src/foo/SILENT.cs", {"Silent"})
    assert result == "Silent"


def test_character_tag_iron_substring_does_not_match_ironclad():
    """Substring without character name → no match."""
    # 'iron' alone shouldn't match 'Ironclad'.
    # Use a path that has 'iron' but not 'ironclad'.
    result = character_tag_for_path("src/foo/iron.cs", {"Ironclad"})
    assert result is None


# ---------- is_encounter_rng_defer (Edge cases 7, 8) ----------


def _make_tree(tmp_path: Path, rel_path: str, content: str) -> Path:
    """Create upstream_tree/<rel_path> with content; return tmp_path."""
    target = tmp_path / rel_path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content)
    return tmp_path


def test_rng_defer_encounter_with_nextitem(tmp_path):
    """Edge case 7: encounter file with Rng.NextItem( → True."""
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Encounters/X.cs",
        "MonsterModel m = base.Rng.NextItem(items);\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Encounters/X.cs") is True


def test_rng_defer_encounter_with_nextbool(tmp_path):
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Encounters/Y.cs",
        "bool b = base.Rng.NextBool();\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Encounters/Y.cs") is True


def test_rng_defer_encounter_with_nextint(tmp_path):
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Encounters/Z.cs",
        "int n = base.Rng.NextInt(3);\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Encounters/Z.cs") is True


def test_rng_defer_encounter_no_rng(tmp_path):
    """Encounter file without RNG → False."""
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Encounters/Static.cs",
        "namespace Foo { class Static {} }\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Encounters/Static.cs") is False


def test_rng_defer_cards_with_rng_NOT_flagged(tmp_path):
    """Edge case 7 part 2: same content in Cards/ → NOT flagged (path-scoped)."""
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Cards/X.cs",
        "MonsterModel m = base.Rng.NextItem(items);\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Cards/X.cs") is False


def test_rng_defer_missing_file(tmp_path):
    """Edge case 8: encounter file deleted (missing) → False (no crash)."""
    assert is_encounter_rng_defer(tmp_path, "src/Core/Models/Encounters/Gone.cs") is False


def test_rng_defer_orbs_NOT_flagged(tmp_path):
    """Orb files with RNG are NOT flagged by this function (only encounters)."""
    tree = _make_tree(
        tmp_path,
        "src/Core/Models/Orbs/Lightning.cs",
        "base.Rng.NextItem(items);\n",
    )
    assert is_encounter_rng_defer(tree, "src/Core/Models/Orbs/Lightning.cs") is False


# ---------- analyze_diff (Edge case 17: integration) ----------


def _build_upstream_tree(tmp_path: Path) -> Path:
    """Build a tmp_path tree that discover_characters will recognize."""
    chars = tmp_path / "src" / "Core" / "Models" / "Characters"
    for c in ("Silent", "Defect", "Ironclad"):
        d = chars / c
        d.mkdir(parents=True)
        (d / f"{c}.cs").write_text(f"namespace Foo;\npublic class {c} : CharacterModel {{ }}\n")
    # Also create the file that NewEncounter.cs renames to (so RNG check works)
    enc_dir = tmp_path / "src" / "Core" / "Models" / "Encounters"
    enc_dir.mkdir(parents=True)
    (enc_dir / "NewEncounter.cs").write_text(
        "namespace Foo;\nclass X { void M() { base.Rng.NextItem(items); } }\n"
    )
    return tmp_path


def _fake_subprocess_runner(stdout: str):
    """Return a callable mimicking subprocess.run that yields `stdout`."""

    def runner(*args, **kwargs):
        return SimpleNamespace(stdout=stdout, returncode=0, stderr="")

    return runner


def test_analyze_diff_integration(tmp_path):
    """Edge case 17: end-to-end with fixture text and mocked discover tree."""
    tree = _build_upstream_tree(tmp_path)
    fixture_text = (FIXTURES / "diff_name_status.sample.txt").read_text()

    report = analyze_diff(
        "v1.0.0",
        "v1.1.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(fixture_text),
    )

    assert isinstance(report, DiffReport)
    assert report.from_tag == "v1.0.0"
    assert report.to_tag == "v1.1.0"

    # Characters discovered from the fake tree
    assert report.discovered_characters == {"Silent", "Defect", "Ironclad"}

    # Spot-check bucket placements
    assert any(
        e.path == "src/Core/Combat/CombatManager.cs"
        for e in report.buckets.get(BUCKET_COMBAT_ENGINE, [])
    )
    assert any(
        e.path == "src/Core/Models/Monsters/Aeonglass.cs"
        for e in report.buckets.get(BUCKET_MONSTERS, [])
    )
    assert any(
        e.path == "src/Core/Models/Monsters/Doormaker.cs" and e.status == "D"
        for e in report.buckets.get(BUCKET_MONSTERS, [])
    )
    assert any(
        e.path == "src/Core/Models/Cards/StrikeSilent.cs" and e.character_tag == "Silent"
        for e in report.buckets.get(BUCKET_CARDS, [])
    )
    assert any(
        e.path == "src/Core/Models/Cards/StrikeIronclad.cs" and e.character_tag == "Ironclad"
        for e in report.buckets.get(BUCKET_CARDS, [])
    )
    assert any(
        e.path == "src/Core/Models/Orbs/LightningOrb.cs"
        for e in report.buckets.get(BUCKET_ORBS, [])
    )
    assert any(
        e.path == "src/Core/Multiplayer/MultiplayerHost.cs"
        for e in report.buckets.get(BUCKET_MULTIPLAYER, [])
    )
    assert any(e.path == "src/Core/Random/Rng.cs" for e in report.buckets.get(BUCKET_RANDOM, []))
    assert any(
        e.path == "src/Core/Models/ModelDb.cs" for e in report.buckets.get(BUCKET_MODEL_BASES, [])
    )
    assert any(
        e.path == "scenes/combat/combat.tscn"
        for e in report.buckets.get(BUCKET_SCENES_GAMEPLAY, [])
    )
    assert any(
        e.path == "scenes/main_menu/main_menu.tscn"
        for e in report.buckets.get(BUCKET_SCENES_UI, [])
    )
    assert any(e.path == "sts2.csproj" for e in report.buckets.get(BUCKET_ROOT_CONFIG, []))


def test_analyze_diff_renames(tmp_path):
    """Edge cases 2 & 3: R092 parsed correctly, appears in renames + bucket."""
    tree = _build_upstream_tree(tmp_path)
    fixture_text = (FIXTURES / "diff_name_status.sample.txt").read_text()

    report = analyze_diff(
        "v1.0.0",
        "v1.1.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(fixture_text),
    )

    # The rename entry must appear in encounters bucket (destination)
    enc_entries = report.buckets.get(BUCKET_ENCOUNTERS, [])
    rename_entries = [e for e in enc_entries if e.status == "R"]
    assert len(rename_entries) == 1
    r = rename_entries[0]
    assert r.path == "src/Core/Models/Encounters/NewEncounter.cs"
    assert r.rename_from == "src/Core/Models/Encounters/OldEncounter.cs"
    assert r.rename_score == 92

    # And it must also appear in report.renames
    assert len(report.renames) == 1
    assert report.renames[0].path == "src/Core/Models/Encounters/NewEncounter.cs"
    assert report.renames[0] is r  # same DiffEntry instance reused


def test_analyze_diff_unmatched_paths(tmp_path):
    """Edge case 9: 'other' bucket fallthrough → also in unmatched_paths."""
    tree = _build_upstream_tree(tmp_path)
    fixture_text = (FIXTURES / "diff_name_status.sample.txt").read_text()
    report = analyze_diff(
        "v1.0.0",
        "v1.1.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(fixture_text),
    )
    assert "unrelated/something/new.txt" in report.unmatched_paths


def test_analyze_diff_character_tags_seen(tmp_path):
    """Edge case 14: character_tags_seen is union of all entry tags."""
    tree = _build_upstream_tree(tmp_path)
    fixture_text = (FIXTURES / "diff_name_status.sample.txt").read_text()
    report = analyze_diff(
        "v1.0.0",
        "v1.1.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(fixture_text),
    )
    # StrikeSilent → Silent, StrikeIronclad → Ironclad
    assert report.character_tags_seen == {"Silent", "Ironclad"}


def test_analyze_diff_encounter_rng_defers_consistent(tmp_path):
    """Edge case 15: encounter_rng_defers subset is consistent with detection.

    The rename's destination is `Encounters/NewEncounter.cs`; our tree
    has that file with `Rng.NextItem`, so it must be flagged.
    """
    tree = _build_upstream_tree(tmp_path)
    fixture_text = (FIXTURES / "diff_name_status.sample.txt").read_text()
    report = analyze_diff(
        "v1.0.0",
        "v1.1.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(fixture_text),
    )
    flagged_paths = {e.path for e in report.encounter_rng_defers}
    assert "src/Core/Models/Encounters/NewEncounter.cs" in flagged_paths
    # All flagged entries must indeed be in encounters bucket
    for e in report.encounter_rng_defers:
        assert e.path.startswith("src/Core/Models/Encounters/")


def test_analyze_diff_empty_input(tmp_path):
    """Edge case 13: empty diff → empty buckets, no errors."""
    tree = _build_upstream_tree(tmp_path)
    report = analyze_diff(
        "v1.0.0",
        "v1.0.0",
        tree,
        _subprocess_run=_fake_subprocess_runner(""),
    )
    # All entry-bearing collections are empty
    total = sum(len(v) for v in report.buckets.values())
    assert total == 0
    assert report.renames == []
    assert report.character_tags_seen == set()
    assert report.encounter_rng_defers == []
    assert report.unmatched_paths == []
    # discovered_characters is still populated
    assert report.discovered_characters == {"Silent", "Defect", "Ironclad"}


def test_analyze_diff_buckets_ordered_by_path(tmp_path):
    """Within each bucket, entries are ordered by path."""
    tree = _build_upstream_tree(tmp_path)
    # Provide diff lines with paths out of order — analyzer must sort.
    fake = (
        "M\tsrc/Core/Models/Cards/ZebraCard.cs\n"
        "M\tsrc/Core/Models/Cards/AlphaCard.cs\n"
        "M\tsrc/Core/Models/Cards/MidCard.cs\n"
    )
    report = analyze_diff(
        "v1",
        "v2",
        tree,
        _subprocess_run=_fake_subprocess_runner(fake),
    )
    cards = report.buckets.get(BUCKET_CARDS, [])
    paths = [e.path for e in cards]
    assert paths == sorted(paths)


def test_analyze_diff_line_delta_none_in_v1(tmp_path):
    """Spec: line_delta left None in v1 (no --stat call)."""
    tree = _build_upstream_tree(tmp_path)
    report = analyze_diff(
        "v1",
        "v2",
        tree,
        _subprocess_run=_fake_subprocess_runner("M\tsrc/Core/Combat/X.cs\n"),
    )
    e = report.buckets[BUCKET_COMBAT_ENGINE][0]
    assert e.line_delta is None


# Edge case 16: discovered_characters comes from entity_extract
def test_analyze_diff_uses_discover_characters(tmp_path):
    """Roster comes from discover_characters(upstream_tree)."""
    # Build a tree where only Watcher exists
    chars = tmp_path / "src" / "Core" / "Models" / "Characters"
    (chars / "Watcher").mkdir(parents=True)
    (chars / "Watcher" / "Watcher.cs").write_text(
        "namespace Foo;\npublic class Watcher : CharacterModel {}\n"
    )
    fake = "M\tsrc/Core/Models/Cards/StrikeWatcher.cs\n"
    report = analyze_diff(
        "v1",
        "v2",
        tmp_path,
        _subprocess_run=_fake_subprocess_runner(fake),
    )
    assert report.discovered_characters == {"Watcher"}
    cards = report.buckets[BUCKET_CARDS]
    assert cards[0].character_tag == "Watcher"


# ---------- DiffEntry / DiffReport frozen ----------


def test_diff_entry_frozen():
    e = DiffEntry(
        status="M",
        path="x.cs",
        rename_from=None,
        rename_score=None,
        character_tag=None,
        line_delta=None,
    )
    with pytest.raises(Exception):
        e.path = "other.cs"  # type: ignore[misc]


def test_diff_report_frozen():
    r = DiffReport(
        from_tag="a",
        to_tag="b",
        buckets={},
        renames=[],
        character_tags_seen=set(),
        encounter_rng_defers=[],
        unmatched_paths=[],
        discovered_characters=set(),
    )
    with pytest.raises(Exception):
        r.from_tag = "z"  # type: ignore[misc]


# ---------- module hygiene ----------


def test_module_stdlib_plus_entity_extract_only():
    """Spec: only stdlib + upstream_sync.entity_extract."""
    import ast

    src = Path(diff_analyze.__file__).read_text()
    tree = ast.parse(src)
    allowed = {
        "re",
        "subprocess",
        "pathlib",
        "dataclasses",
        "typing",
        "__future__",
        "upstream_sync",
        "collections",  # collections.abc.Callable
    }
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                root = alias.name.split(".")[0]
                assert root in allowed, f"forbidden import: {alias.name}"
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".")[0]
            assert root in allowed, f"forbidden import: {node.module}"


def test_analyze_diff_calls_subprocess_with_correct_args(tmp_path):
    """analyze_diff invokes git diff --name-status -M in upstream_tree."""
    tree = _build_upstream_tree(tmp_path)
    captured: dict = {}

    def capture_runner(*args, **kwargs):
        captured["args"] = args
        captured["kwargs"] = kwargs
        return SimpleNamespace(stdout="", returncode=0, stderr="")

    analyze_diff(
        "v1.0",
        "v2.0",
        tree,
        _subprocess_run=capture_runner,
    )

    # Should pass the tags + flags + cwd=tree
    cmd = captured["args"][0]
    assert "git" in cmd[0]
    assert "diff" in cmd
    assert "--name-status" in cmd
    assert "-M" in cmd
    assert "v1.0" in cmd
    assert "v2.0" in cmd
    assert captured["kwargs"].get("cwd") == tree


def test_analyze_diff_priority_character_parameter(tmp_path):
    """priority_character is accepted (informational only in v1)."""
    tree = _build_upstream_tree(tmp_path)
    # Should not raise with an explicit priority_character
    report = analyze_diff(
        "v1",
        "v2",
        tree,
        priority_character="Defect",
        _subprocess_run=_fake_subprocess_runner(""),
    )
    assert isinstance(report, DiffReport)
