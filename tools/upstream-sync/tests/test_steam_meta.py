"""Tests for upstream_sync.steam_meta: Valve VDF appmanifest parsing."""

from __future__ import annotations

from dataclasses import FrozenInstanceError, fields, is_dataclass
from pathlib import Path

import pytest

from upstream_sync import steam_meta
from upstream_sync.steam_meta import SteamMeta, parse_appmanifest

FIXTURE = Path(__file__).parent / "fixtures" / "appmanifest_2868840.sample.acf"


def test_parse_happy_path():
    meta = parse_appmanifest(FIXTURE)
    assert meta.buildid == "22823976"
    assert meta.installdir == "Slay the Spire 2"
    assert meta.lastupdated == 1778289900


def test_lastupdated_is_int():
    meta = parse_appmanifest(FIXTURE)
    assert isinstance(meta.lastupdated, int)
    assert not isinstance(meta.lastupdated, bool)  # bool is subclass of int


def test_returns_steammeta_instance():
    meta = parse_appmanifest(FIXTURE)
    assert isinstance(meta, SteamMeta)


def test_steammeta_is_frozen_dataclass():
    assert is_dataclass(SteamMeta)
    meta = parse_appmanifest(FIXTURE)
    with pytest.raises(FrozenInstanceError):
        meta.buildid = "0"  # type: ignore[misc]


def test_steammeta_field_types():
    """Public surface must be exactly buildid: str, installdir: str, lastupdated: int."""
    type_map = {f.name: f.type for f in fields(SteamMeta)}
    assert type_map == {"buildid": "str", "installdir": "str", "lastupdated": "int"}


def test_tabs_vs_spaces_between_key_and_value(tmp_path):
    """Steam mixes tabs and spaces — parser must accept either."""
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '    "buildid" "111"\n'                # spaces only
        '\t"installdir"\t\t"Foo"\n'            # tabs only
        '"LastUpdated" \t  "1700000000"\n'     # mixed
        "}\n"
    )
    meta = parse_appmanifest(acf)
    assert meta.buildid == "111"
    assert meta.installdir == "Foo"
    assert meta.lastupdated == 1700000000


def test_nested_objects_skipped(tmp_path):
    """Nested objects inside AppState must not be recursed into; only top-level scalars count."""
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"buildid"\t\t"42"\n'
        '\t"installdir"\t\t"Game"\n'
        '\t"LastUpdated"\t\t"100"\n'
        '\t"InstalledDepots"\n'
        "\t{\n"
        '\t\t"buildid"\t\t"WRONG"\n'      # must NOT be picked up
        '\t\t"installdir"\t\t"WRONG"\n'
        "\t}\n"
        "}\n"
    )
    meta = parse_appmanifest(acf)
    assert meta.buildid == "42"
    assert meta.installdir == "Game"
    assert meta.lastupdated == 100


def test_deeply_nested_objects_skipped(tmp_path):
    """Multi-level nesting (e.g. InstalledDepots > 2868841 > manifest) must be skipped."""
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"buildid"\t\t"5"\n'
        '\t"installdir"\t\t"D"\n'
        '\t"LastUpdated"\t\t"7"\n'
        '\t"InstalledDepots"\n'
        "\t{\n"
        '\t\t"2868841"\n'
        "\t\t{\n"
        '\t\t\t"manifest"\t\t"XYZ"\n'
        "\t\t}\n"
        "\t}\n"
        "}\n"
    )
    meta = parse_appmanifest(acf)
    assert meta.buildid == "5"
    assert meta.installdir == "D"
    assert meta.lastupdated == 7


def test_missing_file_raises_filenotfounderror(tmp_path):
    with pytest.raises(FileNotFoundError):
        parse_appmanifest(tmp_path / "does_not_exist.acf")


def test_missing_buildid_raises_valueerror(tmp_path):
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"installdir"\t\t"X"\n'
        '\t"LastUpdated"\t\t"1"\n'
        "}\n"
    )
    with pytest.raises(ValueError, match="buildid"):
        parse_appmanifest(acf)


def test_missing_installdir_raises_valueerror(tmp_path):
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"buildid"\t\t"1"\n'
        '\t"LastUpdated"\t\t"1"\n'
        "}\n"
    )
    with pytest.raises(ValueError, match="installdir"):
        parse_appmanifest(acf)


def test_missing_lastupdated_raises_valueerror(tmp_path):
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"buildid"\t\t"1"\n'
        '\t"installdir"\t\t"X"\n'
        "}\n"
    )
    with pytest.raises(ValueError, match="LastUpdated"):
        parse_appmanifest(acf)


def test_no_appstate_block_raises_valueerror(tmp_path):
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"SomethingElse"\n'
        "{\n"
        '\t"buildid"\t\t"1"\n'
        "}\n"
    )
    with pytest.raises(ValueError, match="AppState"):
        parse_appmanifest(acf)


def test_appstate_header_without_brace_raises_valueerror(tmp_path):
    """AppState header not followed by '{' (even across blank lines) -> ValueError."""
    acf = tmp_path / "m.acf"
    acf.write_text('"AppState"\n\n"buildid" "1"\n')
    with pytest.raises(ValueError, match="AppState"):
        parse_appmanifest(acf)


def test_appstate_header_with_blank_lines_before_brace(tmp_path):
    """Blank lines between AppState header and '{' must be tolerated."""
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "\n"
        "\n"
        "{\n"
        '\t"buildid"\t\t"9"\n'
        '\t"installdir"\t\t"Q"\n'
        '\t"LastUpdated"\t\t"3"\n'
        "}\n"
    )
    meta = parse_appmanifest(acf)
    assert meta.buildid == "9"


def test_lastupdated_non_numeric_raises_valueerror(tmp_path):
    """A non-integer LastUpdated value must produce a ValueError."""
    acf = tmp_path / "m.acf"
    acf.write_text(
        '"AppState"\n'
        "{\n"
        '\t"buildid"\t\t"1"\n'
        '\t"installdir"\t\t"X"\n'
        '\t"LastUpdated"\t\t"not-a-number"\n'
        "}\n"
    )
    with pytest.raises(ValueError):
        parse_appmanifest(acf)


def test_public_surface_only():
    """Module must expose exactly SteamMeta + parse_appmanifest in its public surface."""
    public = {name for name in dir(steam_meta) if not name.startswith("_")}
    # Filter out re-exported stdlib symbols (Path, dataclass, etc.) by checking definitions.
    declared_here = {
        name
        for name in public
        if getattr(getattr(steam_meta, name), "__module__", None)
        == "upstream_sync.steam_meta"
    }
    assert declared_here == {"SteamMeta", "parse_appmanifest"}


def test_stdlib_only_imports():
    """Module may only import from stdlib (re, pathlib, dataclasses)."""
    import ast

    src = Path(steam_meta.__file__).read_text()
    tree = ast.parse(src)
    allowed = {"re", "pathlib", "dataclasses", "__future__"}
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                root = alias.name.split(".")[0]
                assert root in allowed, f"disallowed import: {alias.name}"
        elif isinstance(node, ast.ImportFrom):
            assert node.module is not None
            root = node.module.split(".")[0]
            assert root in allowed, f"disallowed import: {node.module}"
