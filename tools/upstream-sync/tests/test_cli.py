"""Tests for upstream_sync.cli: subcommand dispatch + state file + locking.

All heavy ops (subprocess, network, fs mutations) are mocked. Coverage targets
the routing/glue logic and defensive behavior (KeyboardInterrupt, fcntl lock,
corrupted state).
"""

from __future__ import annotations

import errno
import json
from pathlib import Path
from typing import Any
from unittest.mock import MagicMock

import pytest

from upstream_sync import cli

# --------------------------------------------------------------------------- #
# Helpers                                                                     #
# --------------------------------------------------------------------------- #


def _write_appmanifest(
    steam_home: Path, buildid: str, installdir: str = "Slay the Spire 2"
) -> Path:
    """Create a minimal valid appmanifest_2868840.acf."""
    apps = steam_home / "steamapps"
    apps.mkdir(parents=True, exist_ok=True)
    target = apps / "appmanifest_2868840.acf"
    target.write_text(
        '"AppState"\n'
        "{\n"
        f'\t"buildid"\t\t"{buildid}"\n'
        f'\t"installdir"\t\t"{installdir}"\n'
        f'\t"LastUpdated"\t\t"1715000000"\n'
        "}\n",
        encoding="utf-8",
    )
    return target


def _make_monorepo(tmp_path: Path) -> Path:
    """Make a fake monorepo root with .git/ marker."""
    root = tmp_path / "monorepo"
    root.mkdir()
    (root / ".git").mkdir()
    return root


def _make_state(monorepo: Path, *, buildid: str, version: str) -> Path:
    state = {
        "tool_version": "0.1.0",
        "last_synced_buildid": buildid,
        "last_synced_version": version,
        "last_synced_at": "2026-05-01T12:00:00Z",
        "upstream_tree_path": "/tmp/upstream",
    }
    target = monorepo / ".upstream-sync-state.json"
    target.write_text(json.dumps(state), encoding="utf-8")
    return target


def _patch_flock_ok(monkeypatch: pytest.MonkeyPatch) -> None:
    """Disable fcntl lock acquisition so tests don't block."""
    monkeypatch.setattr(cli.fcntl, "flock", lambda *_a, **_kw: None)


# --------------------------------------------------------------------------- #
# State file helpers                                                          #
# --------------------------------------------------------------------------- #


class TestStateRoundTrip:
    def test_write_then_read_roundtrip(self, tmp_path: Path) -> None:
        monorepo = _make_monorepo(tmp_path)
        payload = {
            "tool_version": "0.1.0",
            "last_synced_buildid": "12345",
            "last_synced_version": "v0.103.2",
            "last_synced_at": "2026-05-14T10:00:00Z",
            "upstream_tree_path": "/tmp/sts2",
        }
        cli._write_state(monorepo, payload)
        result = cli._read_state(monorepo)
        assert result == payload

    def test_read_missing_returns_none(self, tmp_path: Path) -> None:
        monorepo = _make_monorepo(tmp_path)
        assert cli._read_state(monorepo) is None

    def test_read_corrupted_raises(self, tmp_path: Path) -> None:
        monorepo = _make_monorepo(tmp_path)
        (monorepo / ".upstream-sync-state.json").write_text("{this is not json", encoding="utf-8")
        with pytest.raises(RuntimeError, match="state file"):
            cli._read_state(monorepo)

    def test_write_is_atomic(self, tmp_path: Path) -> None:
        """The temp-file approach should leave no .tmp leftover on success."""
        monorepo = _make_monorepo(tmp_path)
        cli._write_state(monorepo, {"key": "value"})
        # Final state file exists; no .tmp residue.
        assert (monorepo / ".upstream-sync-state.json").is_file()
        leftovers = [p for p in monorepo.iterdir() if p.name.endswith(".tmp")]
        assert leftovers == []


# --------------------------------------------------------------------------- #
# Subcommand: check                                                           #
# --------------------------------------------------------------------------- #


class TestCheckSubcommand:
    def _setup_args(self, tmp_path: Path, monorepo: Path) -> dict[str, Path]:
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823976")
        return {"steam_home": steam_home, "monorepo": monorepo}

    def test_no_state_first_run(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        cfg_paths = self._setup_args(tmp_path, monorepo)
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        _patch_flock_ok(monkeypatch)
        rc = cli.main(["check", "--steam-home", str(cfg_paths["steam_home"])])
        assert rc == 0
        out = capsys.readouterr().out
        assert "22823976" in out
        assert "no prior sync" in out.lower()

    def test_no_diff_when_buildid_equal(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        cfg_paths = self._setup_args(tmp_path, monorepo)
        _make_state(monorepo, buildid="22823976", version="v0.103.2")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        _patch_flock_ok(monkeypatch)
        rc = cli.main(["check", "--steam-home", str(cfg_paths["steam_home"])])
        assert rc == 0
        out = capsys.readouterr().out.lower()
        assert "no patch detected" in out

    def test_new_patch_detected(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        cfg_paths = self._setup_args(tmp_path, monorepo)
        # State buildid LESS than appmanifest buildid (22823976).
        _make_state(monorepo, buildid="22000000", version="v0.103.2")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        # No patch notes (avoid network).
        monkeypatch.setattr(cli, "fetch_patch_notes", lambda *_a, **_kw: [])
        _patch_flock_ok(monkeypatch)
        rc = cli.main(["check", "--steam-home", str(cfg_paths["steam_home"])])
        assert rc == 0
        out = capsys.readouterr().out
        assert "NEW PATCH DETECTED" in out

    def test_backward_buildid_warning(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        cfg_paths = self._setup_args(tmp_path, monorepo)
        # State buildid GREATER than appmanifest buildid (22823976).
        _make_state(monorepo, buildid="30000000", version="v0.200.0")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        _patch_flock_ok(monkeypatch)
        rc = cli.main(["check", "--steam-home", str(cfg_paths["steam_home"])])
        assert rc == 0
        out = capsys.readouterr().out
        assert "WARNING" in out
        assert "revert" in out.lower()


# --------------------------------------------------------------------------- #
# Subcommand: extract                                                         #
# --------------------------------------------------------------------------- #


class TestExtractSubcommand:
    def test_extract_first_run_calls_bootstrap(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823976")
        upstream_tree = tmp_path / "godot-sts2"
        upstream_tree.mkdir()
        gdre = tmp_path / "gdre"
        gdre.touch()

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        bootstrap_mock = MagicMock(return_value="abc1234")
        monkeypatch.setattr(cli, "bootstrap", bootstrap_mock)
        # Should NOT be called on first run.
        monkeypatch.setattr(
            cli, "assert_clean", MagicMock(side_effect=AssertionError("must not be called"))
        )
        monkeypatch.setattr(
            cli, "extract_to_staging", MagicMock(side_effect=AssertionError("must not be called"))
        )
        _patch_flock_ok(monkeypatch)

        rc = cli.main(
            [
                "extract",
                "--version",
                "v0.103.2",
                "--steam-home",
                str(steam_home),
                "--upstream-tree",
                str(upstream_tree),
                "--gdre-bin",
                str(gdre),
            ]
        )
        assert rc == 0
        bootstrap_mock.assert_called_once()
        # State must be written.
        state = cli._read_state(monorepo)
        assert state is not None
        assert state["last_synced_buildid"] == "22823976"
        assert state["last_synced_version"] == "v0.103.2"

    def test_extract_subsequent_run_full_pipeline(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823977", installdir="Slay the Spire 2")
        # Pre-existing upstream tree as git repo (.git/ exists).
        upstream_tree = tmp_path / "godot-sts2"
        upstream_tree.mkdir()
        (upstream_tree / ".git").mkdir()
        gdre = tmp_path / "gdre"
        gdre.touch()
        # Steam library structure for pck path.
        (steam_home / "steamapps" / "common" / "Slay the Spire 2").mkdir(parents=True)
        # Prior state file.
        _make_state(monorepo, buildid="22000000", version="v0.103.2")

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        assert_clean_mock = MagicMock()
        extract_mock = MagicMock(
            return_value=MagicMock(
                staging_dir=tmp_path / "staging", file_count=10, unmatched_paths=[]
            )
        )
        rsync_mock = MagicMock()
        commit_mock = MagicMock(return_value="def5678")
        monkeypatch.setattr(cli, "assert_clean", assert_clean_mock)
        monkeypatch.setattr(cli, "extract_to_staging", extract_mock)
        monkeypatch.setattr(cli, "rsync_with_delete", rsync_mock)
        monkeypatch.setattr(cli, "commit_and_tag", commit_mock)
        monkeypatch.setattr(
            cli, "bootstrap", MagicMock(side_effect=AssertionError("must not be called"))
        )
        _patch_flock_ok(monkeypatch)

        rc = cli.main(
            [
                "extract",
                "--version",
                "v0.105.1",
                "--steam-home",
                str(steam_home),
                "--upstream-tree",
                str(upstream_tree),
                "--gdre-bin",
                str(gdre),
            ]
        )
        assert rc == 0
        assert_clean_mock.assert_called_once()
        extract_mock.assert_called_once()
        rsync_mock.assert_called_once()
        commit_mock.assert_called_once()
        state = cli._read_state(monorepo)
        assert state is not None
        assert state["last_synced_buildid"] == "22823977"
        assert state["last_synced_version"] == "v0.105.1"


# --------------------------------------------------------------------------- #
# Subcommand: diff                                                            #
# --------------------------------------------------------------------------- #


class TestDiffSubcommand:
    def test_defaults_to_last_two_tags(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        upstream_tree = tmp_path / "upstream"
        upstream_tree.mkdir()
        (upstream_tree / ".git").mkdir()

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        monkeypatch.setattr(
            cli, "list_tags", lambda *_a, **_kw: ["v0.100.0", "v0.103.2", "v0.105.1"]
        )

        captured_args: dict[str, str] = {}

        def fake_analyze(from_tag: str, to_tag: str, tree: Path, **kw: Any):
            captured_args["from_tag"] = from_tag
            captured_args["to_tag"] = to_tag
            return MagicMock(buckets={"cards": [MagicMock()], "relics": []})

        monkeypatch.setattr(cli, "analyze_diff", fake_analyze)
        _patch_flock_ok(monkeypatch)

        rc = cli.main(["diff", "--upstream-tree", str(upstream_tree)])
        assert rc == 0
        assert captured_args["from_tag"] == "v0.103.2"
        assert captured_args["to_tag"] == "v0.105.1"
        out = capsys.readouterr().out
        assert "cards" in out

    def test_explicit_from_to_used(self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
        monorepo = _make_monorepo(tmp_path)
        upstream_tree = tmp_path / "upstream"
        upstream_tree.mkdir()

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        monkeypatch.setattr(cli, "list_tags", lambda *_a, **_kw: ["v1", "v2", "v3"])
        captured: dict[str, Any] = {}

        def fake_analyze(from_tag: str, to_tag: str, tree: Path, **kw: Any):
            captured["from_tag"] = from_tag
            captured["to_tag"] = to_tag
            return MagicMock(buckets={})

        monkeypatch.setattr(cli, "analyze_diff", fake_analyze)
        _patch_flock_ok(monkeypatch)

        rc = cli.main(["diff", "--from", "v1", "--to", "v3", "--upstream-tree", str(upstream_tree)])
        assert rc == 0
        assert captured["from_tag"] == "v1"
        assert captured["to_tag"] == "v3"


# --------------------------------------------------------------------------- #
# Subcommand: port-decisions                                                  #
# --------------------------------------------------------------------------- #


class TestPortDecisionsSubcommand:
    def test_writes_doc(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        upstream_tree = tmp_path / "upstream"
        upstream_tree.mkdir()

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        monkeypatch.setattr(cli, "list_tags", lambda *_a, **_kw: ["v0.103.2", "v0.105.1"])
        monkeypatch.setattr(
            cli,
            "analyze_diff",
            lambda *_a, **_kw: MagicMock(buckets={}, discovered_characters=set()),
        )
        monkeypatch.setattr(cli, "fetch_patch_notes", lambda *_a, **_kw: [])
        monkeypatch.setattr(
            cli, "correlate", lambda *_a, **_kw: MagicMock(matches={}, unmatched_notes=[])
        )
        monkeypatch.setattr(
            cli, "build_q4_advisory", lambda *_a, **_kw: MagicMock(added=[], removed=[])
        )
        monkeypatch.setattr(cli, "render", lambda *_a, **_kw: "# rendered doc")
        target = monorepo / "engine/headless/docs/specs/01-vA-to-vB-port-decisions.md"
        monkeypatch.setattr(cli, "write_doc", lambda *_a, **_kw: target)
        _patch_flock_ok(monkeypatch)

        rc = cli.main(["port-decisions", "--upstream-tree", str(upstream_tree)])
        assert rc == 0
        out = capsys.readouterr().out
        assert str(target) in out


# --------------------------------------------------------------------------- #
# Subcommand: sync                                                            #
# --------------------------------------------------------------------------- #


class TestSyncSubcommand:
    def test_sync_full_pipeline(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823977")
        upstream_tree = tmp_path / "upstream"
        upstream_tree.mkdir()
        (upstream_tree / ".git").mkdir()
        gdre = tmp_path / "gdre"
        gdre.touch()
        _make_state(monorepo, buildid="22000000", version="v0.103.2")

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        monkeypatch.setattr(cli, "assert_clean", MagicMock())
        monkeypatch.setattr(
            cli,
            "extract_to_staging",
            MagicMock(
                return_value=MagicMock(
                    staging_dir=tmp_path / "stg", file_count=1, unmatched_paths=[]
                )
            ),
        )
        monkeypatch.setattr(cli, "rsync_with_delete", MagicMock())
        monkeypatch.setattr(cli, "commit_and_tag", MagicMock(return_value="aaa"))
        monkeypatch.setattr(cli, "list_tags", lambda *_a, **_kw: ["v0.103.2", "v0.105.1"])
        monkeypatch.setattr(
            cli,
            "analyze_diff",
            lambda *_a, **_kw: MagicMock(buckets={}, discovered_characters=set()),
        )
        monkeypatch.setattr(cli, "fetch_patch_notes", lambda *_a, **_kw: [])
        monkeypatch.setattr(
            cli, "correlate", lambda *_a, **_kw: MagicMock(matches={}, unmatched_notes=[])
        )
        monkeypatch.setattr(
            cli, "build_q4_advisory", lambda *_a, **_kw: MagicMock(added=[], removed=[])
        )
        monkeypatch.setattr(cli, "render", lambda *_a, **_kw: "# doc")
        out_path = monorepo / "engine/headless/docs/specs/01-v0.103.2-to-v0.105.1-port-decisions.md"
        monkeypatch.setattr(cli, "write_doc", lambda *_a, **_kw: out_path)
        _patch_flock_ok(monkeypatch)

        rc = cli.main(
            [
                "sync",
                "--version",
                "v0.105.1",
                "--steam-home",
                str(steam_home),
                "--upstream-tree",
                str(upstream_tree),
                "--gdre-bin",
                str(gdre),
            ]
        )
        assert rc == 0
        captured = capsys.readouterr().out
        assert "v0.105.1" in captured
        assert str(out_path) in captured

    def test_sync_dry_run_skips_extract(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823977")
        upstream_tree = tmp_path / "upstream"
        upstream_tree.mkdir()

        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        # GDRE-using helpers MUST NOT be called.
        monkeypatch.setattr(
            cli, "extract_to_staging", MagicMock(side_effect=AssertionError("nope"))
        )
        monkeypatch.setattr(cli, "rsync_with_delete", MagicMock(side_effect=AssertionError("nope")))
        monkeypatch.setattr(cli, "list_tags", lambda *_a, **_kw: ["v0.103.2", "v0.105.1"])
        monkeypatch.setattr(cli, "bootstrap", MagicMock(return_value="aaa"))
        monkeypatch.setattr(
            cli,
            "analyze_diff",
            lambda *_a, **_kw: MagicMock(buckets={}, discovered_characters=set()),
        )
        monkeypatch.setattr(cli, "fetch_patch_notes", lambda *_a, **_kw: [])
        monkeypatch.setattr(
            cli, "correlate", lambda *_a, **_kw: MagicMock(matches={}, unmatched_notes=[])
        )
        monkeypatch.setattr(
            cli, "build_q4_advisory", lambda *_a, **_kw: MagicMock(added=[], removed=[])
        )
        monkeypatch.setattr(cli, "render", lambda *_a, **_kw: "# doc")
        out_path = monorepo / "engine/headless/docs/specs/01-v0-to-v0-port-decisions.md"
        monkeypatch.setattr(cli, "write_doc", lambda *_a, **_kw: out_path)
        _patch_flock_ok(monkeypatch)

        rc = cli.main(
            [
                "sync",
                "--version",
                "v0.105.1",
                "--dry-run",
                "--steam-home",
                str(steam_home),
                "--upstream-tree",
                str(upstream_tree),
            ]
        )
        assert rc == 0


# --------------------------------------------------------------------------- #
# Defensive: SIGINT + lock                                                    #
# --------------------------------------------------------------------------- #


class TestDefensive:
    def test_keyboard_interrupt_returns_130(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823976")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        _patch_flock_ok(monkeypatch)

        def raise_ki(*_a: Any, **_kw: Any):
            raise KeyboardInterrupt

        monkeypatch.setattr(cli, "_cmd_check", raise_ki)
        rc = cli.main(["check", "--steam-home", str(steam_home)])
        assert rc == 130
        err = capsys.readouterr().err.lower()
        assert "abort" in err

    def test_concurrent_run_blocked(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "steam"
        _write_appmanifest(steam_home, buildid="22823976")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)

        def block(*_a: Any, **_kw: Any):
            raise BlockingIOError(errno.EAGAIN, "Resource temporarily unavailable")

        monkeypatch.setattr(cli.fcntl, "flock", block)
        rc = cli.main(["check", "--steam-home", str(steam_home)])
        assert rc == 1
        err = capsys.readouterr().err.lower()
        assert "another" in err or "in progress" in err


# --------------------------------------------------------------------------- #
# Routing                                                                     #
# --------------------------------------------------------------------------- #


class TestRouting:
    def test_unknown_subcommand_errors(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        with pytest.raises(SystemExit):
            cli.main(["doesnotexist"])

    def test_no_subcommand_prints_help(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        monorepo = _make_monorepo(tmp_path)
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        rc = cli.main([])
        # argparse exits 2 on missing required subcommand OR we return non-zero.
        assert rc != 0

    def test_global_steam_home_flag_propagates(
        self, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
    ) -> None:
        """--steam-home applied at top level reaches resolve_config."""
        monorepo = _make_monorepo(tmp_path)
        steam_home = tmp_path / "custom_steam"
        _write_appmanifest(steam_home, buildid="9999")
        monkeypatch.setattr(cli, "_resolve_monorepo", lambda *_a, **_kw: monorepo)
        _patch_flock_ok(monkeypatch)
        rc = cli.main(["check", "--steam-home", str(steam_home)])
        assert rc == 0
        out = capsys.readouterr().out
        assert "9999" in out
