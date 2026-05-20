#!/usr/bin/env python3
"""PreToolUse hook: block Edit/Write/MultiEdit calls that target paths
outside the current worktree when CWD is inside `.claude/worktrees/`.

Closes the gap left by block-merge-in-worktree: engineer subagents in
worktrees were using absolute paths to the MAIN repo tree, causing edits
to leak onto main's working tree. Subsequent `git commit` from main CWD
then landed work on main directly (recurrent per
[[feedback_subagent_main_landing_recurrent]]; observed 6/7 streams in
Waves 33-35).

Trigger: PreToolUse on Edit, Write, MultiEdit.
Reads stdin JSON `{tool_name, tool_input: {file_path}}`.
Exit 2 blocks; exit 0 allows.
"""

import json
import os
import sys


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0

    if data.get("tool_name") not in ("Edit", "Write", "MultiEdit"):
        return 0

    cwd = os.path.realpath(os.getcwd())
    marker = "/.claude/worktrees/"
    idx = cwd.find(marker)
    if idx < 0:
        return 0  # CWD not in a worktree — main CWD; allow.

    # Worktree root = repo + "/.claude/worktrees/agent-<id>"
    tail = cwd[idx + len(marker) :]
    slash = tail.find("/")
    worktree_root = cwd[:idx] + marker + (tail[:slash] if slash >= 0 else tail)
    worktree_root = os.path.realpath(worktree_root)

    file_path = data.get("tool_input", {}).get("file_path") or ""
    if not file_path:
        return 0
    if not os.path.isabs(file_path):
        return 0  # relative paths resolve to CWD = worktree; safe.

    abspath = os.path.realpath(file_path)
    if abspath == worktree_root or abspath.startswith(worktree_root + os.sep):
        return 0

    sys.stderr.write(
        "BLOCKED by block-edit-outside-worktree hook: "
        f"{data.get('tool_name')} targets a path outside the current worktree.\n"
        f"  CWD worktree root: {worktree_root}\n"
        f"  file_path:         {file_path}\n"
        f"  resolves to:       {abspath}\n"
        "Memory rules [[feedback_subagent_commit_target]] + "
        "[[feedback_subagent_main_landing_recurrent]]: subagents in worktrees "
        "MUST edit only files inside their own worktree. Replace the leading "
        f"{cwd[:idx]} with {worktree_root} to construct the worktree-rooted "
        "path.\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
