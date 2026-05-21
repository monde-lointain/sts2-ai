#!/usr/bin/env python3
"""PreToolUse hook: warn on `git checkout|switch|branch -m/-c/--move/--copy`
invocations from the main repo CWD that target a branch other than `main`.

Closes the gap left by existing block-merge-in-worktree / block-edit-outside-worktree
hooks: subagent dispatch protocol-violations (wave-26 4/5, wave-40, wave-45/A.1)
where a subagent in its own worktree CWD inadvertently created a branch label or
switched the MAIN CWD onto a worktree-shaped branch, landing the subagent's commit
content on `worktree-agent-<id>` (auto-named) instead of the intended
`wave-N/X-<descriptor>` rename target, OR worse, polluting main CWD with the
new branch label.

Empirical context:
- Wave-26 Q1 dispatch: 4/5 streams violated.
- Wave-40: 1 stream violated.
- Wave-45/A.1: 1 stream violated; standard tag→reset→cherry-pick recovery.
- Wave-46: 0/4 cohorts violated (R7-mitigation language in dispatch briefs alone).
- Wave-47a: 0/1 cohort violated (R7-mitigation language unchanged).

Hook behavior — Phase 3a (warn-only):
  - Fires stderr warning when:
      (a) tool is Bash
      (b) command matches write-to-branch-state regex (NOT `branch -d/--list/...`)
      (c) CWD is the main repo path (NOT a worktree subdirectory)
      (d) target branch != `main`
      (e) STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH env var is NOT set to "1"
  - Exit 0 (warn-only); does NOT block the operation.

Phase 3b (block; ratchet after ≥2 wave cycles green with zero recurrence):
  - Same predicate, exit 2 (block) instead of 0.
  - Promotion gated on project-lead approval + ADR-024 promotion-window precedent.

Carve-out env var: `STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1`
  Orchestrators (Q1 lead, project-lead) set this when doing legitimate
  wave-merge recovery or branch-switch work. Subagents never set it →
  hook fires on them.

Failure mode (fail-OPEN):
  Any internal error (regex compile, JSON parse, KeyError) → log to stderr +
  exit 0. NEVER blocks normal git operations on hook bugs.

Reads stdin JSON `{tool_name, tool_input: {command}}` per Claude Code hook spec.

Cross-refs:
- [[feedback-subagent-main-landing-recurrent]] — recurrence pattern
- [[feedback-worktree-dispatch-protocol]] — invariant this hook supplements
- ADR-024 — warn → block promotion-window convention
"""

import json
import os
import re
import sys

# Write-to-branch-state regex. Matches `git checkout`, `git switch`,
# `git branch -m`/`-c`/`--move`/`--copy`. Does NOT match read-only ops
# (`branch -d/-D/--list/--show-current/-v/-a/...`).
WRITE_BRANCH_REGEX = re.compile(
    r"(?:^|[\s;&|`(])git\s+("
    r"checkout(?!\s+--)\b"  # git checkout (excludes `checkout -- file` discard)
    r"|switch\b"  # git switch
    r"|branch\s+-m\b"  # git branch -m
    r"|branch\s+-c\b"  # git branch -c
    r"|branch\s+--move\b"  # git branch --move
    r"|branch\s+--copy\b"  # git branch --copy
    r")"
)

# Heuristic to extract the target branch from a matched command.
# Looks for the first non-flag, non-dash argument AFTER the matched verb.
# Imperfect (won't handle `git -C <path> checkout`, shell aliases, chained
# `&&` operators) — those are documented as known gaps.
TARGET_BRANCH_REGEX = re.compile(
    r"git\s+(?:checkout|switch|branch\s+(?:-m|-c|--move|--copy))\s+"
    r"(?:--[\w-]+\s+)*"  # skip leading flags
    r"(?:-[\w]+\s+)*"  # skip short flags
    r"([\w\-./]+)"  # branch name
)

# Main repo path — parameterized via env var for portability.
DEFAULT_MAIN_REPO_PATH = "/home/clydew372/development/projects/cpp/sts2-ai"
MAIN_REPO_PATH = os.environ.get("STS2_MAIN_REPO_PATH", DEFAULT_MAIN_REPO_PATH)

# Carve-out env var: orchestrators set to bypass.
ALLOW_ENV_VAR = "STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH"


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception as exc:  # fail-OPEN per V5
        sys.stderr.write(
            f"block-branch-create-from-main-cwd: stdin parse failed ({exc!r}); failing open.\n"
        )
        return 0

    try:
        if data.get("tool_name") != "Bash":
            return 0

        command = data.get("tool_input", {}).get("command", "") or ""
        if not command:
            return 0

        # Check regex match.
        if not WRITE_BRANCH_REGEX.search(command):
            return 0

        # Check CWD is main repo path (not a worktree subdir).
        cwd = os.path.realpath(os.getcwd())
        if "/.claude/worktrees/" in cwd:
            return 0  # Inside a worktree; this hook only fires from main CWD.
        if os.path.realpath(MAIN_REPO_PATH) != cwd:
            return 0  # Not the main repo path; out-of-scope.

        # Check carve-out env var.
        if os.environ.get(ALLOW_ENV_VAR, "") == "1":
            return 0  # Orchestrator carve-out.

        # Extract target branch (best-effort heuristic).
        m = TARGET_BRANCH_REGEX.search(command)
        target = m.group(1) if m else "<unknown>"

        # If target is literally `main`, hook does not fire (safe target).
        if target == "main":
            return 0

        # Warn-mode (Phase 3a) — stderr + exit 0.
        sys.stderr.write(
            "WARN by block-branch-create-from-main-cwd hook: "
            "git branch-switch invoked from main repo CWD targeting non-main branch.\n"
            f"  CWD: {cwd}\n"
            f"  command: {command}\n"
            f"  target branch (best-effort): {target}\n"
            "Memory rule [[feedback-subagent-main-landing-recurrent]]: subagents in "
            "their own worktrees should rename branches via `git branch -m` from the "
            "worktree CWD, not by checkout/switch/branch-create from main CWD. "
            f"Set {ALLOW_ENV_VAR}=1 to bypass for orchestrator-intentional switches.\n"
            "Phase 3a: warn-only; does not block. "
            "Phase 3b ratchet (block) after ≥2 wave cycles of no R7 recurrence.\n"
        )
        return 0

    except Exception as exc:  # fail-OPEN per V5
        sys.stderr.write(
            f"block-branch-create-from-main-cwd: hook error ({type(exc).__name__}: {exc!r}); failing open.\n"
        )
        return 0


if __name__ == "__main__":
    sys.exit(main())
