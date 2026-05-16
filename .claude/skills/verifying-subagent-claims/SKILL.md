---
name: verifying-subagent-claims
description: "Use when verifying an engineer subagent's claimed completion before accepting it. Augments superpowers:verification-before-completion with project-specific checks: gtest count parsing, commit-SHA spot-check, worktree-cleanup audit."
---

# Verifying Subagent Claims

From `docs/quantum-lead-prompt.md` Step 7: "Subagents tend to over-claim. Trust but verify." Invoke `[[superpowers:verification-before-completion]]` for the generic discipline, then apply the project-specific checks below before accepting any stream as DONE.

## 1 — gtest count verification

When an agent claims "X tests pass":

```bash
# Re-run with gtest_filter to confirm count
.venv/bin/pytest <test-file> -v 2>&1 | tail -20   # Python
# or
make q3-ci 2>&1 | grep -E "passed|failed|error"

# For C++ gtest:
./build/tests --gtest_filter=<suite>* 2>&1 | grep -E "\[  PASSED  \]|\[  FAILED  \]"
```

Counts must match the agent's claim exactly. Off-by-one is a red flag — re-run the full gate, not just the filter.

## 2 — Commit-SHA spot-check

```bash
git show <claimed-sha> --stat   # confirm SHA exists and diff scope matches claim
git log --oneline -5             # confirm SHA is on expected branch
```

If the SHA doesn't exist or the diff touches files outside the agent's OWNED list, reject the claim.

## 3 — OWNED/FORBIDDEN file audit

```bash
git diff <pre-stream-sha>..<claimed-sha> --name-only
```

Every file in the diff must appear in the stream's OWNED list. Any file in the FORBIDDEN list is a hard reject — the stream must be reverted and re-dispatched.

## 4 — Worktree-cleanup audit

```bash
git worktree list                              # should show only main + active streams
git branch | grep worktree-agent              # orphan detection
```

After a stream reports DONE, its worktree must still exist (not yet removed — that happens in [[merging-a-wave]]). But if the agent cleaned up early, confirm the branch still exists:

```bash
git branch -a | grep <stream-branch>
```

## 5 — Memory consistency

If the agent claims to have modified any file in `.claude/state/` or `.claude/skills/`:

```bash
ls -la .claude/state/
ls -la .claude/skills/<name>/
```

Confirm the file exists and its mtime is recent. `head -5` the file to confirm non-empty and structurally valid.

## Pre-merge checklist

Run before accepting a stream and handing off to [[merging-a-wave]]:

- [ ] Gate green: ran exact command from stream's Verification section, not a proxy
- [ ] gtest/pytest counts match agent's reported numbers
- [ ] `git show <claimed-sha> --stat` confirms SHA real and diff in-scope
- [ ] Diff name-only — no FORBIDDEN files touched
- [ ] No orphan worktrees from this stream
- [ ] If memory/state files claimed modified: confirmed by `ls` + content spot-check

## Rejection protocol

If any check fails:
1. Do **not** merge the stream.
2. Re-dispatch the stream with the specific failure identified: "Your SHA shows X but you claimed Y. Re-run and re-report."
3. Do **not** accept a second claim without re-running the full checklist.

## Cross-references

- `[[superpowers:verification-before-completion]]` — underlying generic discipline
- [[merging-a-wave]] — merge only after checklist passes
- [[running-a-quantum-ci-gate]] — gate commands and wall-clock budgets
- `docs/quantum-lead-prompt.md` Step 7 — "trust but verify" doctrine
