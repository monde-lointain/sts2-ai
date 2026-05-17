"""Decompiler-artifact filters for upstream-sync.

GDRE decompile artifacts appear in the upstream tree as synthetic files whose
names follow the pattern ``^--[yz]__.*[.]cs$``.  These are NOT game-logic files;
they must be excluded from diff analysis, content indexing, and port-decision
rows.

Public surface:
    ARTIFACT_REGEX — compiled regex for the canonical artifact pattern.
    ARTIFACT_ALLOWLIST — known artifact stems; updated as new GDRE builds ship.
    is_artifact(path) — fast combined test (regex + allowlist).
"""

from __future__ import annotations

import re

__all__ = [
    "ARTIFACT_ALLOWLIST",
    "ARTIFACT_REGEX",
    "is_artifact",
]

# ---------------------------------------------------------------------------
# Canonical decompiler-artifact regex
# ---------------------------------------------------------------------------

# Pattern: starts with '--y__' or '--z__', ends with '.cs'.
# GDRE emits these for internal bookkeeping files it cannot attribute to a
# real source class.
ARTIFACT_REGEX: re.Pattern[str] = re.compile(r"^--[yz]__.*\.cs$")

# ---------------------------------------------------------------------------
# Known-artifact allowlist
# ---------------------------------------------------------------------------

# Stem names (no directory prefix, no .cs suffix) of known GDRE artifacts.
# Populated from the spike in decompile-determinism-report.md (v2.5.0-beta.5).
# Update when a new GDRE version ships new bookkeeping files.
ARTIFACT_ALLOWLIST: frozenset[str] = frozenset(
    {
        "--y__GlobalClass",
        "--z__GlobalClass",
        "--y__GlobalEnums",
        "--z__GlobalEnums",
        "--y__GlobalSignals",
        "--z__GlobalSignals",
    }
)


def is_artifact(path: str) -> bool:
    """Return True if *path* is a decompiler artifact.

    A path is an artifact if:
    - Its **filename** (last path component) matches ARTIFACT_REGEX, OR
    - Its **filename stem** (no .cs suffix) is in ARTIFACT_ALLOWLIST.

    Both checks use the filename only, not the full path, so directory-prefixed
    paths (e.g. ``src/Core/--y__GlobalClass.cs``) are handled correctly.
    """
    filename = path.rsplit("/", 1)[-1]
    if ARTIFACT_REGEX.match(filename):
        return True
    stem = filename[:-3] if filename.endswith(".cs") else filename
    return stem in ARTIFACT_ALLOWLIST
