#!/usr/bin/env python3
"""PreToolUse hook: block `git merge` (any CWD) if any `.claude/worktrees/*`
has uncommitted changes.

Rationale: merging into main while another worktree has pending work
risks losing that work or merging a half-baked branch. The wave-merge
protocol assumes clean worktree state.

Skipped for `git merge --abort` (cleanup is always OK).
"""
import json
import os
import re
import subprocess
import sys

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

if data.get("tool_name") != "Bash":
    sys.exit(0)

command = data.get("tool_input", {}).get("command", "") or ""
GIT_MERGE = re.compile(r"(?:^|[\s;&|`(])git\s+merge\b")
if not GIT_MERGE.search(command):
    sys.exit(0)
if "--abort" in command or "--quit" in command:
    sys.exit(0)


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


# List worktrees; for each one under .claude/worktrees/, check status
try:
    wt_list = run(["git", "worktree", "list", "--porcelain"], check=True)
except Exception:
    sys.exit(0)  # if we can't enumerate, don't block

dirty = []
current = {}
for line in wt_list.stdout.splitlines() + [""]:
    if not line:
        if current.get("worktree") and "/.claude/worktrees/" in current["worktree"]:
            status = run(["git", "-C", current["worktree"], "status", "--porcelain"])
            if status.stdout.strip():
                dirty.append(current["worktree"])
        current = {}
    elif line.startswith("worktree "):
        current["worktree"] = line[len("worktree "):]

if dirty:
    sys.stderr.write(
        "BLOCKED by block-dirty-worktree-merge hook: git merge attempted "
        "while these worktrees have uncommitted changes:\n"
    )
    for w in dirty:
        sys.stderr.write(f"  - {w}\n")
    sys.stderr.write(
        "Commit or stash worktree changes before merging into main.\n"
        f"Command: {command}\n"
    )
    sys.exit(2)

sys.exit(0)
