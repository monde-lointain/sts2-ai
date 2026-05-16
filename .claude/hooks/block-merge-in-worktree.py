#!/usr/bin/env python3
"""PreToolUse hook: block `git merge` invocations when CWD is inside a
worktree under `.claude/worktrees/`.

Memory rule [[feedback-worktree-dispatch-protocol]]: merges happen from
main repo CWD only. The 2026-05-14 incident landed a merge on the wrong
branch from residual CWD; reflog recovery was needed.

Reads stdin JSON `{tool_name, tool_input: {command}}`. Exit 2 blocks
with stderr to model; exit 0 allows.
"""

import json
import os
import re
import sys

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

if data.get("tool_name") != "Bash":
    sys.exit(0)

command = data.get("tool_input", {}).get("command", "") or ""

# Match `git merge` (with or without flags) at command boundary
GIT_MERGE = re.compile(r"(?:^|[\s;&|`(])git\s+merge\b")
if not GIT_MERGE.search(command):
    sys.exit(0)

cwd = os.getcwd()
if "/.claude/worktrees/" in cwd:
    sys.stderr.write(
        "BLOCKED by block-merge-in-worktree hook: git merge invoked from "
        "inside a worktree.\n"
        f"CWD: {cwd}\n"
        "Memory rule: merges happen from main repo CWD only "
        "([[feedback-worktree-dispatch-protocol]]).\n"
        f"Command: {command}\n"
    )
    sys.exit(2)

sys.exit(0)
