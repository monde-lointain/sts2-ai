#!/usr/bin/env python3
"""Pre-push hook: warn on main-bound push if any pending spec edit lacks
either a substrate-paired commit or a `doc-only:` flag.

**Phase 3a: warn-only**. Promote to block in ADR-N+2 after two consecutive
silent wave cycles per ADR-024.

Pre-commit framework invokes this with stdin lines:
    <local_ref> <local_sha> <remote_ref> <remote_sha>

We gate only pushes targeting `main` (refs/heads/main). Feature-branch
pushes pass freely.

Pending entries live in `.claude/state/spec-edits-pending-resolution.json`.
An entry is "resolved" when, in the commits being pushed:
  (a) the edited spec's YAML-frontmatter `substrate:` path is touched, OR
  (b) any commit message contains the literal `doc-only:` flag.

The spec's `substrate:` is read directly from the spec's frontmatter at
the local SHA (per ADR-023 — frontmatter is the machine-readable lookup;
no quantum-map markdown parsing).
"""

import json
import os
import re
import subprocess
import sys

PUSHED_REFS = sys.stdin.read().strip().splitlines()


def project_root():
    common = subprocess.run(
        ["git", "rev-parse", "--git-common-dir"], capture_output=True, text=True, check=True
    ).stdout.strip()
    return os.path.dirname(os.path.abspath(common))


def parse_substrate(spec_path, root):
    """Read first ~10 lines, extract `substrate:` from YAML frontmatter."""
    abs_path = os.path.join(root, spec_path)
    if not os.path.exists(abs_path):
        return None
    with open(abs_path) as f:
        head = f.read(2048)
    if not head.startswith("---"):
        return None
    m = re.search(r"^substrate:\s*(.+?)\s*$", head, re.MULTILINE)
    if not m:
        return None
    val = m.group(1).strip()
    return val if not val.startswith("n/a") else None


def commits_touch_path(remote_sha, local_sha, path):
    """Did any commit in the push range modify files under <path>?"""
    try:
        out = subprocess.run(
            ["git", "diff", "--name-only", f"{remote_sha}..{local_sha}"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    except subprocess.CalledProcessError:
        return False
    norm = path.rstrip("/")
    return any(line == norm or line.startswith(norm + "/") for line in out.splitlines())


def commits_have_doc_only_flag(remote_sha, local_sha):
    """Any commit in the range with `doc-only:` in the message?"""
    try:
        out = subprocess.run(
            ["git", "log", "--format=%B", f"{remote_sha}..{local_sha}"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    except subprocess.CalledProcessError:
        return False
    return "doc-only:" in out


def main():
    main_pushes = [
        line.split()
        for line in PUSHED_REFS
        if line.strip() and line.split()[2] == "refs/heads/main"
    ]
    if not main_pushes:
        sys.exit(0)

    try:
        root = project_root()
    except Exception:
        sys.exit(0)

    state_path = os.path.join(root, ".claude/state/spec-edits-pending-resolution.json")
    if not os.path.exists(state_path):
        sys.exit(0)

    with open(state_path) as f:
        state = json.load(f)
    pending = [e for e in state.get("entries", []) if not e.get("resolution")]
    if not pending:
        sys.exit(0)

    # Use the first main-bound push's range for resolution checks.
    local_sha = main_pushes[0][1]
    remote_sha = main_pushes[0][3]

    if commits_have_doc_only_flag(remote_sha, local_sha):
        sys.exit(0)

    unresolved = []
    for entry in pending:
        substrate = parse_substrate(entry["file"], root)
        if substrate and commits_touch_path(remote_sha, local_sha, substrate):
            continue
        unresolved.append((entry, substrate))

    if unresolved:
        sys.stderr.write(
            "WARN by pre-push-spec-resolution-gate (Phase 3a warn-only, "
            "per ADR-024): push to main has pending spec edits without "
            "either a paired substrate commit or a `doc-only:` flag.\n\n"
        )
        for e, sub in unresolved:
            sub_disp = sub or "n/a (frontmatter missing/derived)"
            sys.stderr.write(f"  - {e['file']} (substrate: {sub_disp}, edited {e['edited_at']})\n")
        sys.stderr.write(
            "\nResolve by EITHER touching the substrate dir in the same PR "
            "OR adding `doc-only:` to a commit message. Warn-only today; "
            "promotes to block in ADR-N+2 after 2 silent wave cycles.\n"
        )
        # warn-only: exit 0, do NOT block
        sys.exit(0)

    sys.exit(0)


if __name__ == "__main__":
    main()
