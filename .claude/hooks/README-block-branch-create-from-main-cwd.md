# R7 hook proposal: `block-branch-create-from-main-cwd.py`

> Drafted 2026-05-21 (wave-47a/Workstream-B). Q1-lead-authored on branch `r7-hook-proposal-2026-05-21`.
> **Install deferred** per project-lead directive: gates on ≥2 more clean wave cycles with zero R7 recurrence.

## 1 — Motivation

The subagent main-landing recurrence pattern (`[[feedback-subagent-main-landing-recurrent]]`) has surfaced repeatedly across waves:

| Wave        | Streams violated | Recovery                                           |
| ----------- | ---------------- | -------------------------------------------------- |
| Wave-26     | 4/5              | 2 orchestrator-recovered, 2 self-recovered, 1 clean |
| Wave-40     | 1                | engineer-recovered                                  |
| Wave-45/A.1 | 1                | Q1-lead tag→reset→cherry-pick                       |
| Wave-46     | **0/4 cohorts**  | R7-mitigation language in dispatch briefs alone     |
| Wave-47a    | **0/1 cohort**   | R7-mitigation language unchanged                    |

Wave-46 + wave-47a demonstrate that dispatch-prompt language is currently effective. This hook is **belt-and-suspenders** — a structural fail-safe in case dispatch prompts drift or new subagent classes (different model, different cohort shape) don't inherit the convention.

The hook fills the gap left by existing worktree-discipline hooks (`block-merge-in-worktree`, `block-edit-outside-worktree`, `block-dirty-worktree-merge`): none catch the specific failure mode where a subagent's `git branch -m` invocation runs from a CWD that has somehow shifted to the main repo path (subagent intended to rename its OWN branch; ended up creating a branch label on main).

## 2 — Design

**Matcher:** PreToolUse on `Bash` tool.

**Predicate (warning fires when ALL true):**
1. `tool_name == "Bash"` AND `tool_input.command` is non-empty.
2. Command matches write-to-branch-state regex: `git checkout`, `git switch`, `git branch -m`, `git branch -c`, `git branch --move`, `git branch --copy`. Does NOT match read-only branch ops (`-d/-D/--list/--show-current/-v/-a`) or `checkout --` (file discard).
3. CWD == main repo path (parameterized via `STS2_MAIN_REPO_PATH`; default `/home/clydew372/development/projects/cpp/sts2-ai`). Does NOT fire from worktree subdirectories (`.claude/worktrees/...`).
4. Target branch != `main` (best-effort heuristic regex).
5. Env var `STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH` is NOT set to `1` (orchestrator carve-out).

**Behavior — Phase 3b (BLOCK; ratcheted 2026-05-22 wave-49/B):**
- Stderr message with: CWD, command, best-effort target branch, memory-rule reference, carve-out instructions.
- Exit 2 (BLOCKS the operation). Orchestrators bypass via `STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1` prefix for legitimate workflows.

**Phase 3a (warn-only, original install state — superseded 2026-05-22):**
- Original wave-47b/A install (2026-05-21) ran exit 0 + stderr warning only.
- Ratcheted to Phase 3b after 4 consecutive clean wave cycles (wave-46, wave-47a, wave-47b, wave-48) per project-lead approval at wave-48 close response.
- ADR-024 promotion-window convention precedent honored.

**R7 discharge gate:** 1 more clean wave cycle in Phase 3b (block-mode active). If block-mode causes legitimate orchestrator workflow friction (forgot env-var carve-out), surface to project lead — ratchet back to Phase 3a if needed.

## 3 — Self-test (3 scenarios)

Each scenario constructs a stub stdin JSON payload, pipes to the hook, observes exit code + stderr.

**Scenario 1 — Warning fires (main CWD; new branch target; no env var):**

```bash
cd /home/clydew372/development/projects/cpp/sts2-ai
echo '{"tool_name":"Bash","tool_input":{"command":"git checkout new-branch"}}' | \
  python3 .claude/hooks/block-branch-create-from-main-cwd.py
echo "Exit: $?"
```

Expected:
```
WARN by block-branch-create-from-main-cwd hook: git branch-switch invoked from main repo CWD targeting non-main branch.
  CWD: /home/clydew372/development/projects/cpp/sts2-ai
  command: git checkout new-branch
  target branch (best-effort): new-branch
Memory rule [[feedback-subagent-main-landing-recurrent]]: subagents in their own worktrees should rename branches via `git branch -m` from the worktree CWD, not by checkout/switch/branch-create from main CWD. Set STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1 to bypass for orchestrator-intentional switches.
Phase 3a: warn-only; does not block. Phase 3b ratchet (block) after ≥2 wave cycles of no R7 recurrence.
Exit: 0
```

**Scenario 2 — Env var bypass (quiet):**

```bash
cd /home/clydew372/development/projects/cpp/sts2-ai
echo '{"tool_name":"Bash","tool_input":{"command":"git checkout new-branch"}}' | \
  STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1 \
  python3 .claude/hooks/block-branch-create-from-main-cwd.py
echo "Exit: $?"
```

Expected: no stderr; `Exit: 0`.

**Scenario 3 — Target is `main` (quiet — safe target):**

```bash
cd /home/clydew372/development/projects/cpp/sts2-ai
echo '{"tool_name":"Bash","tool_input":{"command":"git checkout main"}}' | \
  python3 .claude/hooks/block-branch-create-from-main-cwd.py
echo "Exit: $?"
```

Expected: no stderr; `Exit: 0`.

**Self-test results (Q1-lead-verified 2026-05-21):** All 3 scenarios pass; 2 bonus scenarios (fail-OPEN garbage JSON; read-only `branch -d` no-fire) also pass.

```
=== Self-test 1: warn fires ===
WARN by block-branch-create-from-main-cwd hook: git branch-switch invoked from main repo CWD targeting non-main branch.
  CWD: /home/clydew372/development/projects/cpp/sts2-ai
  command: git checkout new-branch
  target branch (best-effort): new-branch
Memory rule [[feedback-subagent-main-landing-recurrent]]: subagents in their own worktrees should rename branches via `git branch -m` from the worktree CWD, not by checkout/switch/branch-create from main CWD. Set STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1 to bypass for orchestrator-intentional switches.
Phase 3a: warn-only; does not block. Phase 3b ratchet (block) after ≥2 wave cycles of no R7 recurrence.
Exit: 0

=== Self-test 2: env-var bypass (quiet) ===
Exit: 0  (no stderr)

=== Self-test 3: target is main (quiet) ===
Exit: 0  (no stderr)

=== Bonus: Fail-OPEN garbage JSON ===
block-branch-create-from-main-cwd: stdin parse failed (JSONDecodeError('Expecting value: line 1 column 1 (char 0)')); failing open.
Exit: 0

=== Bonus: Read-only `git branch -d` does NOT fire ===
Exit: 0  (no stderr; correctly distinguishes write-to-branch-state from read-only ops)
```

## 4 — Coverage + known gaps

**Covered:**
- `git checkout <branch>`
- `git switch <branch>`
- `git branch -m [<old>] <new>` (rename)
- `git branch -c [<old>] <new>` (copy)
- `git branch --move`
- `git branch --copy`

**Read-only branch ops — NOT covered (intentional):**
- `git branch -d` / `git branch -D` (delete; orchestrator workflow; not part of the failure mode)
- `git branch --list` / `-v` / `-a` (inspection)
- `git branch --show-current`

**Out of scope (different failure modes — documented for clarity):**
- `git reset --hard <branch>` — moves HEAD; typically intentional orchestrator op; different recovery story.
- `git stash branch <name>` — rare; not part of recurrence pattern.

**Known false-negatives (best-effort heuristics):**
- `git -C <path> checkout ...` — the `-C` flag changes the effective CWD; hook checks `os.getcwd()` not the `-C` arg. Bypasses regex anchor.
- Shell aliases (`gco`, `gsw`, `gb`) — hook's regex matches `git` literally.
- Chained commands (`git checkout main && git checkout other-branch`) — only the first match in the string is analyzed (best-effort).

These gaps are ACCEPTABLE in Phase 3a (warn-only) — they're false-negatives, not false-positives. Engineers using `-C` flags / aliases are typically orchestrators with intent; hook would warn-spam them otherwise.

## 5 — Failure modes

**Fail-OPEN semantics (V5):** every code path wraps in try/except. On ANY internal error (regex compile fail, JSON parse fail, KeyError, environment-variable surprise), hook logs to stderr + exits 0. Hook bugs NEVER block normal git operations.

Test: pipe garbage JSON to the hook:
```bash
echo 'not-json' | python3 .claude/hooks/block-branch-create-from-main-cwd.py
echo "Exit: $?"
# Expected: stderr error message + Exit: 0
```

## 6 — Install criteria

**Gates for installation (per project-lead directive 2026-05-21):**

1. **≥2 wave cycles green with zero R7 recurrence** post-wave-46. Wave-46 was 1 clean cycle. Wave-47a was a second clean cycle (1 cohort, no violation). If a single additional wave runs cleanly (e.g., wave-47b or wave-48), this criterion is met.
2. **Project-lead review** of this proposal + hook script + self-test results.
3. **Install action:** Q1 lead (with project-lead approval) edits `.claude/settings.local.json` to add the registration snippet (§7 below).

**Install criterion explicitly NOT met as of 2026-05-21:** the proposal artifact exists but the hook is NOT registered. Branch `r7-hook-proposal-2026-05-21` persists until install OR explicit reject.

## 7 — Settings.local.json install snippet

Add to the existing `PreToolUse → matcher: "Bash" → hooks` array in `.claude/settings.local.json`:

```json
{
  "type": "command",
  "command": "python3 $CLAUDE_PROJECT_DIR/.claude/hooks/block-branch-create-from-main-cwd.py"
}
```

Existing hooks already use this `$CLAUDE_PROJECT_DIR/.claude/hooks/<name>.py` pattern (per `block-system-python.py`, etc.). Hook script is gitignored-environment-aware (CWD-based detection).

**Note:** `.claude/settings.local.json` is gitignored per ADR-024 precedent — install is per-machine. Future ADR-024 follow-up may migrate hook registrations to tracked `.claude/settings.json`; out of scope for this proposal.

## 8 — Rollback

**To temporarily disable** (one-off command bypass):
```bash
STS2_ALLOW_MAIN_CWD_BRANCH_SWITCH=1 git checkout some-branch
```

**To uninstall permanently:** remove the registration entry from `.claude/settings.local.json` `hooks → PreToolUse → matcher: "Bash" → hooks` array. Hook script file at `.claude/hooks/block-branch-create-from-main-cwd.py` can remain (no effect when not registered).

**To repath main repo:** set `STS2_MAIN_REPO_PATH` env var to the absolute path of the main repo (e.g., for portable installs on different developer machines).

## Cross-references

- `[[feedback-subagent-main-landing-recurrent]]` — memory rule + recurrence pattern
- `[[feedback-worktree-dispatch-protocol]]` — invariant this hook supplements
- ADR-024 — warn → block promotion-window precedent (proto-edit-tracker.py + spec-edit-tracker.py)
- Wave-26, wave-40, wave-45/A.1, wave-46, wave-47a — empirical recurrence + clean-cycle history

## Origin

Project-lead-authorized 2026-05-21 (wave-46 close response). Q1-lead-drafted 2026-05-21 (wave-47a/Workstream-B). Install gates on ≥2 clean wave cycles + project-lead review.
