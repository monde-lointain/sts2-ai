"""Git operations against the *upstream tree* (NOT the sts2-ai monorepo).

The upstream tree (default `~/development/projects/godot/sts2`, configurable
via `Config.upstream_tree`) is not under git when first encountered; this
module's `bootstrap()` initializes it with an allowlist `.gitignore`.
Subsequent `commit_and_tag()` calls add version tags on top.

All commits use a dedicated machine author (`upstream-sync@local`) injected
via `git -c` flags so the upstream tree's commits are clearly automated and
the global git config is not polluted.
"""

from __future__ import annotations

import subprocess
from pathlib import Path

# --------------------------------------------------------------------------- #
# Allowlist .gitignore                                                        #
# --------------------------------------------------------------------------- #
# Deny-all + re-enable only the source / scene / config files we want
# tracked. GDRE extracts dump engine caches and binary assets that would
# bloat the upstream tree without yielding diffable signal.
ALLOWLIST_GITIGNORE = """# Track only source + scenes + key config; ignore engine cache + assets
/*
!/.gitignore
!/src/
!/scenes/
!/*.csproj
!/*.sln
!/global.json
!/packages.lock.json
!/project.godot
!/--y__*.cs
!/--y__*.cs.uid
!/--z__*.cs
!/--z__*.cs.uid
"""

_AUTHOR_FLAGS = (
    "-c",
    "user.email=upstream-sync@local",
    "-c",
    "user.name=upstream-sync",
)


# --------------------------------------------------------------------------- #
# Internal helpers                                                            #
# --------------------------------------------------------------------------- #


def _run_git(tree: Path, *args: str, op: str) -> subprocess.CompletedProcess[str]:
    """Run `git <args>` in `tree`. Re-raise non-zero exits as RuntimeError.

    `op` is a short human-readable label used in error messages (e.g. "init",
    "commit"). We intentionally swallow CalledProcessError here so callers
    see a single RuntimeError-shaped failure mode.
    """
    try:
        return subprocess.run(
            ["git", *args],
            cwd=tree,
            check=True,
            capture_output=True,
            text=True,
        )
    except subprocess.CalledProcessError as exc:
        stderr = (exc.stderr or "").strip()
        raise RuntimeError(f"git {op} failed in {tree}: {stderr}") from exc
    except FileNotFoundError as exc:  # git binary missing
        raise RuntimeError(f"git binary not found while attempting {op}: {exc}") from exc


def _is_git_repo(tree: Path) -> bool:
    return (tree / ".git").exists()


def _tag_exists(tree: Path, tag: str) -> bool:
    """True iff `tag` already exists in `tree`. Uses git plumbing for correctness."""
    result = subprocess.run(
        ["git", "rev-parse", "--verify", f"refs/tags/{tag}"],
        cwd=tree,
        capture_output=True,
        text=True,
    )
    return result.returncode == 0


# --------------------------------------------------------------------------- #
# Public API                                                                  #
# --------------------------------------------------------------------------- #


def bootstrap(tree: Path, version: str, buildid: str, gdre_version: str) -> str:
    """Initialize the upstream tree under git with the allowlist `.gitignore`.

    Idempotent: if `.git/` already exists, returns the current HEAD SHA
    without modifying anything.

    On a fresh tree: writes ALLOWLIST_GITIGNORE, runs git init, stages
    everything, commits with a labelled message, and tags `version`.

    Returns the resulting HEAD SHA. Raises RuntimeError on any git failure.
    """
    if _is_git_repo(tree):
        return get_head_sha(tree)

    (tree / ".gitignore").write_text(ALLOWLIST_GITIGNORE)

    _run_git(tree, "init", "-q", op="init")
    _run_git(tree, "add", "-A", op="add")

    msg = (
        f"bootstrap: import {version} "
        f"(GDRE {gdre_version}, Steam buildid {buildid})"
    )
    _run_git(tree, *_AUTHOR_FLAGS, "commit", "-q", "-m", msg, op="commit")
    _run_git(tree, "tag", version, op="tag")

    return get_head_sha(tree)


def commit_and_tag(
    tree: Path,
    version: str,
    buildid: str,
    prior_buildid: str | None,
) -> str:
    """Stage all changes, commit them, and apply tag `version`.

    Refuses (RuntimeError) if:
      * tag `version` already exists, or
      * `prior_buildid` is not None AND int(buildid) < int(prior_buildid)
        (backward-time guard).

    The caller is responsible for invoking `assert_clean` before re-extracting;
    this function does not enforce that pre-condition.

    Returns the new HEAD SHA.
    """
    if _tag_exists(tree, version):
        raise RuntimeError(f"tag {version} already exists in {tree}")

    if prior_buildid is not None and int(buildid) < int(prior_buildid):
        raise RuntimeError(
            f"backward-time guard: buildid {buildid} < prior_buildid {prior_buildid}"
        )

    _run_git(tree, "add", "-A", op="add")

    # Sanity: after `add -A`, every porcelain line should start with [AM ]
    # (added / modified-staged / modified-in-worktree). Anything else (?? for
    # untracked, etc.) indicates the allowlist let something through it
    # shouldn't have.
    status = _run_git(tree, "status", "--porcelain", op="status").stdout
    offending = [
        line for line in status.splitlines() if line and line[:1] not in ("A", "M", " ")
    ]
    if offending:
        joined = "\n".join(offending)
        raise RuntimeError(f"unexpected porcelain lines after add -A:\n{joined}")

    msg = f"sync: {version} (Steam buildid {buildid})"
    _run_git(tree, *_AUTHOR_FLAGS, "commit", "-q", "-m", msg, op="commit")
    _run_git(tree, "tag", version, op="tag")

    return get_head_sha(tree)


def assert_clean(tree: Path) -> None:
    """Verify the upstream tree's working tree is clean.

    Raises RuntimeError listing the offending paths if anything is modified,
    staged, or untracked.
    """
    status = _run_git(tree, "status", "--porcelain", op="status").stdout
    lines = [line for line in status.splitlines() if line]
    if lines:
        # Strip the two-char status prefix + space so the user sees paths.
        paths = [line[3:] if len(line) > 3 else line for line in lines]
        joined = "\n".join(paths)
        raise RuntimeError(f"upstream tree {tree} is not clean:\n{joined}")


def get_head_sha(tree: Path) -> str:
    """Return current HEAD SHA. RuntimeError if `tree` is not a git repo."""
    return _run_git(tree, "rev-parse", "HEAD", op="rev-parse").stdout.strip()


def list_tags(tree: Path) -> list[str]:
    """Return all tags in the upstream tree (empty list if none)."""
    out = _run_git(tree, "tag", "--list", op="tag").stdout
    return [line for line in out.splitlines() if line]
