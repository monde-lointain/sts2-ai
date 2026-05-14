"""Tests for upstream_sync.extract: GDRE staging + allowlist surveillance + rsync mirror."""

from __future__ import annotations

import shutil
from pathlib import Path
from unittest.mock import MagicMock

import pytest

from upstream_sync import extract


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_file(path: Path, content: bytes = b"x") -> None:
    """Create file, including parents."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


def _populate_minimal_staging(staging: Path) -> None:
    """Drop the GDRE sanity-key file so extract_to_staging passes its check."""
    _make_file(staging / "src" / "Core" / "Combat" / "CombatManager.cs", b"// sanity")


def _make_subprocess_result(returncode: int = 0, stdout: str = "", stderr: str = ""):
    """Mimic the attributes of a subprocess.CompletedProcess."""
    result = MagicMock()
    result.returncode = returncode
    result.stdout = stdout
    result.stderr = stderr
    return result


# ---------------------------------------------------------------------------
# surveil_allowlist
# ---------------------------------------------------------------------------


class TestSurveilAllowlist:
    def test_all_allowed_paths_returns_empty(self, tmp_path: Path) -> None:
        """Staging with only allowed top-level paths produces no unmatched."""
        _make_file(tmp_path / "src" / "foo.cs")
        _make_file(tmp_path / "scenes" / "main.tscn")
        _make_file(tmp_path / "project.godot")
        _make_file(tmp_path / "global.json")
        _make_file(tmp_path / ".gitignore")

        assert extract.surveil_allowlist(tmp_path) == []

    def test_unwanted_top_level_directory_detected(self, tmp_path: Path) -> None:
        """A top-level dir not in the allowlist is surfaced with summed size."""
        _make_file(tmp_path / "src" / "foo.cs", b"abc")
        _make_file(tmp_path / "scenes" / "x.tscn", b"yy")
        _make_file(
            tmp_path / "_mono_referenced_assemblies" / "System.dll",
            b"1234567890",
        )
        _make_file(
            tmp_path / "_mono_referenced_assemblies" / "nested" / "More.dll",
            b"abc",
        )

        unmatched = extract.surveil_allowlist(tmp_path)
        assert unmatched == [("_mono_referenced_assemblies", 13)]

    def test_sorted_deterministically(self, tmp_path: Path) -> None:
        """Multiple unmatched entries come back sorted by path."""
        _make_file(tmp_path / "zeta_dir" / "x", b"a")
        _make_file(tmp_path / "alpha_dir" / "y", b"bb")
        _make_file(tmp_path / "mid_dir" / "z", b"ccc")

        unmatched = extract.surveil_allowlist(tmp_path)
        assert [p for p, _ in unmatched] == ["alpha_dir", "mid_dir", "zeta_dir"]

    def test_top_level_only_does_not_recurse(self, tmp_path: Path) -> None:
        """Files nested inside an allowed dir do NOT appear as unmatched."""
        _make_file(tmp_path / "src" / "weird_subdir" / "deep.cs")
        _make_file(tmp_path / "scenes" / "_internal" / "hidden.tscn")

        assert extract.surveil_allowlist(tmp_path) == []

    def test_unmatched_top_level_file(self, tmp_path: Path) -> None:
        """A loose file at the top level that doesn't match any pattern."""
        _make_file(tmp_path / "src" / "foo.cs")
        _make_file(tmp_path / "random.txt", b"hello")

        unmatched = extract.surveil_allowlist(tmp_path)
        assert unmatched == [("random.txt", 5)]

    def test_csproj_top_level_is_allowed(self, tmp_path: Path) -> None:
        _make_file(tmp_path / "Game.csproj")
        _make_file(tmp_path / "Solution.sln")
        _make_file(tmp_path / "packages.lock.json")
        assert extract.surveil_allowlist(tmp_path) == []

    def test_yz_dunder_files_are_allowed(self, tmp_path: Path) -> None:
        _make_file(tmp_path / "--y__Helper.cs")
        _make_file(tmp_path / "--z__Other.cs")
        _make_file(tmp_path / "--y__Helper.cs.uid")
        assert extract.surveil_allowlist(tmp_path) == []


# ---------------------------------------------------------------------------
# extract_to_staging
# ---------------------------------------------------------------------------


class TestExtractToStaging:
    def test_happy_path_returns_staging_result(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre_tools.x86_64"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.write_bytes(b"fake-pck")
        staging = tmp_path / "staging"

        def fake_run(cmd, **kwargs):
            staging.mkdir(parents=True, exist_ok=True)
            _populate_minimal_staging(staging)
            _make_file(staging / "scenes" / "main.tscn")
            return _make_subprocess_result(returncode=0)

        result = extract.extract_to_staging(
            pck, staging, gdre_bin, _subprocess_run=fake_run
        )

        assert isinstance(result, extract.StagingResult)
        assert result.staging_dir == staging
        assert result.file_count == 2
        assert result.unmatched_paths == []

    def test_missing_gdre_bin_raises_filenotfound(self, tmp_path: Path) -> None:
        missing = tmp_path / "does_not_exist"
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"

        with pytest.raises(FileNotFoundError, match="GDRE"):
            extract.extract_to_staging(
                pck, staging, missing, _subprocess_run=lambda *a, **kw: None
            )

    def test_missing_pck_raises_filenotfound(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre_tools.x86_64"
        gdre_bin.touch()
        missing_pck = tmp_path / "missing.pck"
        staging = tmp_path / "staging"

        with pytest.raises(FileNotFoundError, match="pck"):
            extract.extract_to_staging(
                missing_pck, staging, gdre_bin, _subprocess_run=lambda *a, **kw: None
            )

    def test_subprocess_nonzero_exit_raises_runtime(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"

        def fake_run(cmd, **kwargs):
            staging.mkdir(parents=True, exist_ok=True)
            return _make_subprocess_result(returncode=1, stderr="boom")

        with pytest.raises(RuntimeError, match="GDRE"):
            extract.extract_to_staging(
                pck, staging, gdre_bin, _subprocess_run=fake_run
            )

    def test_exit_zero_but_sanity_key_missing_raises(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"

        def fake_run(cmd, **kwargs):
            staging.mkdir(parents=True, exist_ok=True)
            _make_file(staging / "src" / "Some" / "Other.cs")
            return _make_subprocess_result(returncode=0)

        with pytest.raises(RuntimeError, match="CombatManager"):
            extract.extract_to_staging(
                pck, staging, gdre_bin, _subprocess_run=fake_run
            )

    def test_dotnet_stderr_line_suppressed_but_not_failure(
        self, tmp_path: Path, caplog
    ) -> None:
        """Harmless 'Could not create child process: dotnet' must not appear in log output."""
        gdre_bin = tmp_path / "gdre"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"

        def fake_run(cmd, **kwargs):
            staging.mkdir(parents=True, exist_ok=True)
            _populate_minimal_staging(staging)
            return _make_subprocess_result(
                returncode=0,
                stderr=(
                    "Could not create child process: dotnet\n"
                    "Some other useful warning\n"
                ),
            )

        import logging

        with caplog.at_level(logging.INFO, logger="upstream_sync.extract"):
            result = extract.extract_to_staging(
                pck, staging, gdre_bin, _subprocess_run=fake_run
            )

        assert isinstance(result, extract.StagingResult)
        joined = "\n".join(r.getMessage() for r in caplog.records)
        assert "Could not create child process: dotnet" not in joined
        # Sanity: the unrelated stderr line is still logged
        assert "Some other useful warning" in joined

    def test_command_uses_expected_flags(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"
        captured = {}

        def fake_run(cmd, **kwargs):
            captured["cmd"] = cmd
            staging.mkdir(parents=True, exist_ok=True)
            _populate_minimal_staging(staging)
            return _make_subprocess_result(returncode=0)

        extract.extract_to_staging(
            pck, staging, gdre_bin, _subprocess_run=fake_run
        )

        cmd = captured["cmd"]
        assert cmd[0] == str(gdre_bin)
        assert "--headless" in cmd
        assert f"--recover={pck}" in cmd
        assert f"--output={staging}" in cmd
        assert "--ignore-checksum-errors" in cmd
        # MUST NOT pass --force-bytecode-version (auto-detect).
        assert not any("--force-bytecode-version" in c for c in cmd)

    def test_unmatched_paths_propagate_to_result(self, tmp_path: Path) -> None:
        gdre_bin = tmp_path / "gdre"
        gdre_bin.touch()
        pck = tmp_path / "foo.pck"
        pck.touch()
        staging = tmp_path / "staging"

        def fake_run(cmd, **kwargs):
            staging.mkdir(parents=True, exist_ok=True)
            _populate_minimal_staging(staging)
            _make_file(staging / "_mono_referenced_assemblies" / "x.dll", b"abcdef")
            return _make_subprocess_result(returncode=0)

        result = extract.extract_to_staging(
            pck, staging, gdre_bin, _subprocess_run=fake_run
        )

        assert result.unmatched_paths == [("_mono_referenced_assemblies", 6)]


# ---------------------------------------------------------------------------
# rsync_with_delete
# ---------------------------------------------------------------------------


class TestRsyncWithDelete:
    def test_invocation_contains_delete_and_includes(self, tmp_path: Path) -> None:
        staging = tmp_path / "staging"
        upstream = tmp_path / "upstream"
        staging.mkdir()
        upstream.mkdir()
        captured = {}

        def fake_run(cmd, **kwargs):
            captured["cmd"] = cmd
            return _make_subprocess_result(returncode=0)

        extract.rsync_with_delete(staging, upstream, _subprocess_run=fake_run)

        cmd = captured["cmd"]
        assert cmd[0] == "rsync"
        assert "-a" in cmd
        assert "--delete" in cmd
        # Verify every spec'd include is present as an --include=PATTERN arg.
        for pattern in extract.ALLOWLIST_RSYNC_INCLUDES:
            assert f"--include={pattern}" in cmd, f"missing include {pattern}"
        assert "--exclude=*" in cmd
        # Source must end in trailing slash, target need not.
        assert cmd[-2].endswith("/")
        assert cmd[-2] == f"{staging}/"
        assert cmd[-1] == f"{upstream}/"

    def test_exit_zero_no_error(self, tmp_path: Path) -> None:
        staging = tmp_path / "staging"
        upstream = tmp_path / "upstream"
        staging.mkdir()
        upstream.mkdir()

        extract.rsync_with_delete(
            staging,
            upstream,
            _subprocess_run=lambda *a, **kw: _make_subprocess_result(returncode=0),
        )

    def test_exit_nonzero_raises_runtime(self, tmp_path: Path) -> None:
        staging = tmp_path / "staging"
        upstream = tmp_path / "upstream"
        staging.mkdir()
        upstream.mkdir()

        with pytest.raises(RuntimeError, match="rsync"):
            extract.rsync_with_delete(
                staging,
                upstream,
                _subprocess_run=lambda *a, **kw: _make_subprocess_result(
                    returncode=1, stderr="some rsync error"
                ),
            )


# ---------------------------------------------------------------------------
# Integration test against real rsync (skipped if rsync absent)
# ---------------------------------------------------------------------------


@pytest.mark.skipif(shutil.which("rsync") is None, reason="rsync not installed")
class TestRsyncIntegration:
    def test_rsync_mirrors_only_allowed_paths(self, tmp_path: Path) -> None:
        staging = tmp_path / "staging"
        upstream = tmp_path / "upstream"
        staging.mkdir()
        upstream.mkdir()

        # Staging contents.
        _make_file(staging / "src" / "foo.cs", b"// allowed\n")
        _make_file(staging / "src" / "sub" / "bar.cs", b"// allowed\n")
        _make_file(staging / "scenes" / "main.tscn", b"[scene]\n")
        _make_file(staging / "project.godot", b"; godot\n")
        _make_file(staging / "Game.csproj", b"<Project/>\n")
        _make_file(staging / "_mono_referenced_assemblies" / "junk.dll", b"BIN")
        _make_file(staging / "random.txt", b"nope")

        # Pre-existing upstream content inside an allowlisted dir that should be
        # deleted by --delete (rsync's --delete only acts on files inside the
        # filter scope; a stale file in /src/ is therefore visible to --delete).
        _make_file(upstream / "src" / "stale.cs", b"// stale\n")

        extract.rsync_with_delete(staging, upstream)

        # Allowed content present and matches staging.
        assert (upstream / "src" / "foo.cs").read_bytes() == b"// allowed\n"
        assert (upstream / "src" / "sub" / "bar.cs").read_bytes() == b"// allowed\n"
        assert (upstream / "scenes" / "main.tscn").read_bytes() == b"[scene]\n"
        assert (upstream / "project.godot").read_bytes() == b"; godot\n"
        assert (upstream / "Game.csproj").read_bytes() == b"<Project/>\n"

        # Disallowed top-level content not mirrored.
        assert not (upstream / "_mono_referenced_assemblies").exists()
        assert not (upstream / "random.txt").exists()

        # --delete must have removed pre-existing upstream files absent from
        # staging *within the filter scope*.
        assert not (upstream / "src" / "stale.cs").exists()
