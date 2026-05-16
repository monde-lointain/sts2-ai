---
name: rescuing-ci-failures
description: Use when a GitHub Actions run is failing on the current branch and the goal is to pull failed-step logs, triage, fix, push, and iterate until green. Codifies stop conditions, dedup-by-error-signature, and fix-subagent dispatch.
---

# Rescuing CI Failures

When a remote GHA run is red on the current branch, this skill drives a bounded loop: pull failed-step logs → triage → dispatch a fix subagent → optional confirm → push → wait for the next run → repeat. The loop is hard-bounded by iteration cap, same-error dedup, and category gate so it cannot run away.

## When to invoke

- A push to a feature branch comes back red on GitHub.
- The user says "fix CI" / "make CI green" / "the build's broken".
- `/ci-rescue` slash command is the standard entry point.

This skill is **remote-only** — for local gate failures (`/q-ci`, `/phase0-gate`) read `.claude/state/last-gate.json` and fix directly.

## Preflight

```bash
gh auth status >/dev/null 2>&1 || { echo "gh not authed"; exit 12; }
git rev-parse --is-inside-work-tree >/dev/null || exit 12
branch="$(git rev-parse --abbrev-ref HEAD)"
[[ "$branch" == "main" ]] && { echo "refusing to rescue on main"; exit 12; }
```

Branch == main is a hard halt (no override). CI failures on main are an incident, not a rescue.

## Algorithm

1. **Read state**. If `.claude/state/ci-rescue.json.status == "running"` and `branch` matches current branch, resume; else `init`.
2. **Resolve latest run**:
   ```bash
   gh run list --branch "$branch" --limit 1 \
     --json databaseId,status,conclusion,headSha,workflowName
   ```
3. **Branch on `conclusion`**:
   - `success` → `write-ci-rescue.sh update-status green`; exit 0.
   - `failure` → step 4.
   - `cancelled` → `escalate cancelled-run`; AskUserQuestion.
   - `null` with `status` `in_progress` / `queued` / `pending` → **wait**:
     - If the in-flight workflow's longest job ETA is <10min (rule of thumb: `ci.yml` p50 ≈ 3-8min; `q2-ci` ≈ 18min; `phase0-gate` ≈ 20min) → foreground `gh run watch <id>`.
     - Otherwise → `ScheduleWakeup` 270s and re-enter `/ci-rescue`.
4. **Pull failed logs**:
   ```bash
   gh run view "$run_id" --log-failed > "/tmp/ci-rescue-$run_id.log"
   ```
5. **Triage** — classify into one category and compute the error signature. See "Triage rules" + "Error signature" below.
6. **Dedup check**: if `error_signature == last_error_signature` → `escalate same-error-twice`; surface to user via AskUserQuestion ("the prior fix didn't change the failure — review needed").
7. **Cap check**: if `iteration_count >= max_iterations` → `escalate iteration-cap`.
8. **Category check**: if category == `unactionable` → `escalate unactionable` with the matched pattern as context.
9. **Dispatch fix subagent** — see "Subagent dispatch" below.
10. **Verify subagent commit**:
    ```bash
    head_after="$(git rev-parse HEAD)"
    [[ "$head_after" != "$head_before" ]] || { echo "subagent did not commit"; exit 13; }
    git diff --quiet HEAD~1 HEAD && { echo "empty commit"; exit 13; }
    ```
    Invoke `[[verifying-subagent-claims]]` for the broader check.
11. **Confirm push**:
    - If `auto_push == true` → push.
    - Else → AskUserQuestion with the diff summary; on rejection, `escalate divr` (subagent's fix was rejected by the user).
12. **Push** `git push`; capture new `head_sha`.
13. **Record iteration**:
    ```bash
    write-ci-rescue.sh record-iteration "$category" "$summary" "$head_sha" "$error_signature"
    ```
14. **Wait for the new run to appear**:
    ```bash
    # poll until gh sees a run for the new head_sha (≤60s timeout)
    for i in $(seq 1 6); do
      new_run="$(gh run list --branch "$branch" --limit 5 --json databaseId,headSha \
        | jq -r --arg s "$head_sha" '.[] | select(.headSha==$s) | .databaseId' | head -1)"
      [[ -n "$new_run" ]] && break
      sleep 10
    done
    ```
15. **Loop to step 2.**

## Triage rules

Apply patterns in order. First match wins.

| Pattern (regex / substring) | Category |
|---|---|
| `^.*: error: ` / `error CS\d+` / `ld: error` / `fatal error:` | `compiler-error` |
| `\[  FAILED  \]` / `FAIL:` / `AssertionError` / `pytest:.*failed` / `Test Run Failed` | `test-failure` |
| `error SA\d+` / `error CA\d+` / `^ruff` / `clang-tidy: error` / `cppcheck: error` / `ESLint` / `error TS\d+` | `lint` |
| `timed out` / `cancelled` / `no space left` / `quota exceeded` / `429 Too Many Requests` / `secret \w+ not found` / `connection refused` / `dial tcp` | `unactionable` |

Default if nothing matches → `unactionable` (we don't auto-fix what we can't classify).

## Error signature

Deterministic SHA256 over a normalized failure fingerprint. Algorithm:

```bash
# extract failed step names (lines like "##[error]…" or "FAILED test_x")
failed_steps="$(grep -E '^##\[error\]|^FAILED|\[  FAILED  \]' "/tmp/ci-rescue-$run_id.log" \
  | sort -u)"
# first 3 lines of each failure cluster, normalized: strip timestamps, line numbers
fingerprint="$(grep -B0 -A3 -E '^##\[error\]|FAIL:|error CS|error SA|error CA' "/tmp/ci-rescue-$run_id.log" \
  | sed -E 's/[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.Z]+//g; s/:[0-9]+:[0-9]+:/:LINE:COL:/g; s/0x[0-9a-fA-F]+/0xHEX/g' \
  | sort -u)"
signature="sha256:$(printf '%s\n%s' "$failed_steps" "$fingerprint" | sha256sum | cut -d' ' -f1)"
```

Stored verbatim in `last_error_signature`. The `LINE:COL` / `0xHEX` / timestamp normalizations prevent trivial off-by-one diffs from defeating dedup.

## Stop conditions and exit codes

| Code | Meaning | State action |
|---|---|---|
| 0 | Green — CI passes | `update-status green` |
| 10 | Iteration cap reached | `escalate iteration-cap` |
| 11 | Same error signature as prior iteration | `escalate same-error-twice` |
| 12 | Unactionable category, preflight failure, or branch==main | `escalate unactionable` / `escalate gh-auth-failure` |
| 13 | Fix subagent returned `DIVR` or user rejected diff | `escalate divr` |

On any escalation, AskUserQuestion to surface the state file contents (path + last 2 attempted fixes + escalation_reason) so the user can decide whether to extend iterations, switch strategy, or stop.

## Subagent dispatch

Dispatch one general-purpose subagent per iteration. Model: Sonnet 4.6 (project default — see `model tiering` in `.claude/CLAUDE.md`). Do NOT use `isolation: "worktree"` — the fix must land on the user's branch directly.

Prompt template (fill the angle-bracketed slots):

```
CI failure on branch <branch> at <head_sha>.

Failed log: /tmp/ci-rescue-<run_id>.log
Workflow: <workflow_name>
Triaged category: <category>
Iteration: <n> of <max>

Prior attempted fixes (do NOT repeat — the failure signature must change):
- <iteration 1 summary>
- <iteration 2 summary>

Your job:
1. Read the failed log.
2. Diagnose root cause. Apply `[[superpowers:systematic-debugging]]` discipline — confirm hypothesis with a code read before editing.
3. Apply a minimal fix. Do NOT broaden scope, refactor unrelated code, or add unrequested features.
4. Commit on the current branch with message "fix(ci): <one-line summary>".
5. DO NOT PUSH.

Return:
- The commit SHA (output of `git rev-parse HEAD`).
- A one-line summary of the change.
- If you cannot fix this mechanically, return the literal token "DIVR" followed by a rationale.

Constraints:
- Do not edit `.github/workflows/*` unless the log explicitly indicates a workflow YAML bug.
- Do not bypass pre-commit hooks (`--no-verify`).
- Do not amend prior commits.
- Stay on the current branch; do not switch or rebase.
```

## State writes — every transition

| Trigger | Wrapper invocation |
|---|---|
| Loop start, fresh | `write-ci-rescue.sh init <branch> <head_sha> <run_id> <workflow> <max_iter> <auto_push>` |
| New run is in-progress / waiting | `write-ci-rescue.sh update-status running` |
| CI green | `write-ci-rescue.sh update-status green` |
| Iteration commit pushed | `write-ci-rescue.sh record-iteration <category> <summary> <commit_sha> <error_signature>` |
| Any escalation | `write-ci-rescue.sh escalate <reason>` |
| User aborts | `write-ci-rescue.sh reset` (clears to `{"status":"idle"}`) |

## Cross-references

- [[verifying-subagent-claims]] — invoke before push.
- [[superpowers:systematic-debugging]] — fix subagent must use.
- `.claude/state/SCHEMA.md` — full `ci-rescue.json` shape.
- `.claude/commands/ci-rescue.md` — slash-command entry.
- `.claude/commands/ci-logs.md` — one-shot log pull, no loop.
