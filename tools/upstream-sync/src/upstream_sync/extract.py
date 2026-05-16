"""GDRE-driven atomic extraction pipeline.

Two-phase flow per plan §12.7:

    1. Staging   — invoke gdre_tools.x86_64 into a scratch directory and
                   sanity-check the result.
    2. Mirror    — rsync the allowlisted top-level paths over the tracked
                   upstream tree with --delete, so upstream-removed files
                   actually disappear.

Phase 1.5 (allowlist surveillance) runs between the two and surfaces any
top-level path GDRE produced that is NOT covered by the allowlist, so we
notice when STS2 introduces a new top-level folder we'd otherwise drop.

Public surface (kept stable; consumed by W6 CLI):
    ALLOWLIST_RSYNC_INCLUDES — top-level patterns mirrored into upstream
    StagingResult            — return value of extract_to_staging
    extract_to_staging       — phase-1 wrapper around GDRE
    surveil_allowlist        — phase-1.5 inventory
    rsync_with_delete        — phase-2 mirror
"""

from __future__ import annotations

import fnmatch
import logging
import subprocess
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Top-level rsync include patterns; mirrors the negated entries in the
# .gitignore allowlist (see git_ops.ALLOWLIST_GITIGNORE).
#
# IMPORTANT: do NOT include `/.gitignore` here. The .gitignore in the upstream
# tree is a tooling artifact written by git_ops.bootstrap(); it does NOT exist
# in the GDRE staging dir. If we listed it in includes, `rsync --delete` would
# treat the target's .gitignore as an "extra" file outside the source and
# delete it — and then `git add -A` would slurp every untracked file in the
# working tree because no ignore rules remain.
ALLOWLIST_RSYNC_INCLUDES: list[str] = [
    "/src/",
    "/src/***",
    "/scenes/",
    "/scenes/***",
    "/*.csproj",
    "/*.sln",
    "/global.json",
    "/packages.lock.json",
    "/project.godot",
    "/--y__*.cs",
    "/--y__*.cs.uid",
    "/--z__*.cs",
    "/--z__*.cs.uid",
]

# Path under the staging dir that any successful extraction must produce —
# used as a cheap sanity check.
_SANITY_KEY_FILE = Path("src/Core/Combat/CombatManager.cs")

# Harmless GDRE stderr noise; suppressed from logged output.
_DOTNET_NOISE = "Could not create child process: dotnet"


@dataclass(frozen=True)
class StagingResult:
    staging_dir: Path
    file_count: int
    unmatched_paths: list[tuple[str, int]] = field(default_factory=list)


# ---------------------------------------------------------------------------
# Phase 1 — GDRE staging
# ---------------------------------------------------------------------------


def extract_to_staging(
    pck_path: Path,
    staging_dir: Path,
    gdre_bin: Path,
    *,
    _subprocess_run: Callable[..., Any] | None = None,
) -> StagingResult:
    """Invoke GDRE to extract `pck_path` into `staging_dir`.

    Raises FileNotFoundError if `gdre_bin` or `pck_path` are missing;
    RuntimeError if GDRE exits non-zero or the sanity-key file is absent
    afterwards.
    """
    runner = _subprocess_run if _subprocess_run is not None else subprocess.run

    if not gdre_bin.exists():
        raise FileNotFoundError(f"GDRE binary not found: {gdre_bin}")
    if not pck_path.exists():
        raise FileNotFoundError(f"pck file not found: {pck_path}")

    staging_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        str(gdre_bin),
        "--headless",
        f"--recover={pck_path}",
        f"--output={staging_dir}",
        "--ignore-checksum-errors",
    ]
    logger.info("Running GDRE: %s", " ".join(cmd))
    result = runner(cmd, capture_output=True, text=True)

    _log_filtered_stderr(getattr(result, "stderr", "") or "")

    if result.returncode != 0:
        raise RuntimeError(
            f"GDRE extraction failed (exit {result.returncode}): {getattr(result, 'stderr', '')!r}"
        )

    sanity_path = staging_dir / _SANITY_KEY_FILE
    if not sanity_path.exists():
        raise RuntimeError(
            "GDRE extraction reported success but sanity-key file is missing: "
            f"CombatManager.cs not found at {sanity_path}"
        )

    file_count = sum(1 for p in staging_dir.rglob("*") if p.is_file())
    unmatched = surveil_allowlist(staging_dir)
    if unmatched:
        logger.warning(
            "Allowlist surveillance: %d top-level path(s) not in allowlist: %s",
            len(unmatched),
            unmatched,
        )

    return StagingResult(
        staging_dir=staging_dir,
        file_count=file_count,
        unmatched_paths=unmatched,
    )


def _log_filtered_stderr(stderr_text: str) -> None:
    """Log stderr lines except the well-known harmless dotnet line."""
    for line in stderr_text.splitlines():
        if _DOTNET_NOISE in line:
            continue
        if line.strip():
            logger.info("gdre stderr: %s", line)


# ---------------------------------------------------------------------------
# Phase 1.5 — Allowlist surveillance
# ---------------------------------------------------------------------------


def surveil_allowlist(staging_dir: Path) -> list[tuple[str, int]]:
    """Return top-level paths in `staging_dir` NOT matched by the allowlist.

    Each tuple is `(name_relative_to_staging, size_bytes)`. Directories
    contribute the sum of all files within them.
    """
    unmatched: list[tuple[str, int]] = []
    for entry in sorted(staging_dir.iterdir(), key=lambda p: p.name):
        if _matches_allowlist(entry):
            continue
        size = _entry_size_bytes(entry)
        unmatched.append((entry.name, size))
    return unmatched


def _matches_allowlist(entry: Path) -> bool:
    """True if `entry` (a top-level child) matches any allowlist pattern."""
    name = entry.name
    is_dir = entry.is_dir()
    for raw_pattern in ALLOWLIST_RSYNC_INCLUDES:
        # Strip the leading '/' (anchor at staging root) and any trailing
        # '/***' (rsync's recurse marker — for matching purposes we care only
        # about the directory name itself).
        pattern = raw_pattern.lstrip("/")
        is_dir_pattern = pattern.endswith("/")
        pattern = pattern.rstrip("/")
        if pattern.endswith("***"):
            pattern = pattern[: -len("***")].rstrip("/")
            is_dir_pattern = True
        if not pattern:
            continue
        if is_dir_pattern and not is_dir:
            continue
        if not is_dir_pattern and is_dir:
            continue
        if fnmatch.fnmatchcase(name, pattern):
            return True
    return False


def _entry_size_bytes(entry: Path) -> int:
    if entry.is_file():
        return entry.stat().st_size
    if entry.is_dir():
        return sum(p.stat().st_size for p in entry.rglob("*") if p.is_file())
    return 0


# ---------------------------------------------------------------------------
# Phase 2 — rsync mirror
# ---------------------------------------------------------------------------


def rsync_with_delete(
    staging_dir: Path,
    upstream_tree: Path,
    *,
    _subprocess_run: Callable[..., Any] | None = None,
) -> None:
    """Mirror allowlisted content from `staging_dir` into `upstream_tree`.

    Invokes `rsync -a --delete` with the allowlist as `--include` patterns
    and `--exclude='*'` to drop everything else. Raises RuntimeError on
    non-zero exit.
    """
    runner = _subprocess_run if _subprocess_run is not None else subprocess.run

    cmd: list[str] = ["rsync", "-a", "--delete"]
    # Protect the tooling-owned .gitignore in the target before any include
    # rules consider it — rsync evaluates filters top-down, first-match wins.
    cmd.append("--exclude=/.gitignore")
    for pattern in ALLOWLIST_RSYNC_INCLUDES:
        cmd.append(f"--include={pattern}")
    cmd.append("--exclude=*")
    cmd.append(f"{staging_dir}/")
    cmd.append(f"{upstream_tree}/")

    logger.info("Running rsync: %s", " ".join(cmd))
    result = runner(cmd, capture_output=True, text=True)

    if result.returncode != 0:
        raise RuntimeError(
            f"rsync failed (exit {result.returncode}): {getattr(result, 'stderr', '')!r}"
        )
