---
name: dispatching-a-wave
description: Use when planning a multi-stream wave dispatch in the sts2-ai initiative (Q1–Q12 quanta). Codifies worktree-per-subagent dispatch, file-ownership partition (R8), pre-flight SHA check for wave N>0, and the dispatch-prompt template.
---

# Dispatching a Wave

A wave = one PR. Sub-streams = per-stream commits merged sequentially into that PR. Sub-streams must be file-disjoint (R8 partition). Never let two sub-streams touch the same file — serialize those instead.

## Step 1 — Capture pre-wave SHA

Before any worktree creation, record the current main tip:

```bash
git rev-parse HEAD  # must match expected SHA from plan
```

Write to `.claude/state/current-wave.json` via `/wave-dispatch` (which calls `.claude/scripts/write-gate-status.sh` internals). If this file already exists for the same wave, **idempotency check**: read `.claude/state/active-worktrees.json` — re-use existing worktrees rather than creating duplicates.

## Step 2 — Create per-stream worktrees

Invoke `[[superpowers:using-git-worktrees]]` first to establish isolation pattern. Then, for each stream:

```bash
git worktree add .claude/worktrees/agent-<stream-id> -b worktree-agent-<stream-id>
```

Or use Agent tool with `isolation: "worktree"` — this auto-creates the worktree.

Check `.claude/state/active-worktrees.json` for existing entries before creating. Status field: `pending | running | done | merged`.

## Step 3 — Dispatch prompt template

Adapt from `docs/quantum-lead-prompt.md` Step 5 — never omit sections; mark unused as "N/A — none for this sub-stream":

```
# Sub-stream <ID>: <one-line goal>

## Quantum context
You are an engineer subagent on <Q? - Name>. The quantum lead has been
directed by the project lead to <one-sentence directive context>.

## Concrete goal
<2-4 sentences. What changes in the repo. What "done" looks like.>

## Files you OWN (may edit)
- <explicit path>

## Files you must NOT touch (owned by parallel sub-streams)
- <explicit path>  ← owned by sub-stream <ID>

## Constraints
- <constraint>

## Verification (must pass before you report DONE)
- `<exact command>`  → must be green

## Deliverables
- <list of commits / new files>

## Re-surface triggers (return to me instead of completing)
- <concrete condition>

## Reporting
On completion, return:
- Files changed (list)
- Test results (counts + commands)
- Any deferrals or caveats
- Risk-register implications (if any)
```

**Wave-N>0 preflight clause** (mandatory in every agent prompt for N>1):

```
## Pre-flight (CRITICAL)
Expected main SHA: `<sha>`.
git rev-parse HEAD
git fetch origin
git merge --ff-only main
If HEAD != expected SHA or FF fails → STOP and report back immediately.
```

## Step 4 — Parallel dispatch

Invoke `[[superpowers:dispatching-parallel-agents]]` before issuing parallel Agent calls. Fire all independent sub-streams in a single message with multiple Agent tool calls. Serial dependencies (stream β consumes α output) must be explicit — β waits for α merge before dispatch.

## File-ownership rule (R8)

Per-stream prompt must list:
- **OWNED files** — the only files the agent may touch
- **FORBIDDEN files** — owned by parallel streams (name the stream)

Verify file-by-file before dispatch. If two streams need the same file, serialize them.

## Pre-dispatch checklist

- [ ] Sub-streams partition by file — verified file-by-file
- [ ] Each prompt has explicit verification commands (not "tests should pass")
- [ ] Each prompt lists owned + forbidden files
- [ ] Serial dependencies between streams stated
- [ ] Wave-N>0 preflight clause included in every prompt
- [ ] `.claude/state/active-worktrees.json` checked for idempotency

## Cross-references

- [[merging-a-wave]] — post-dispatch merge dance
- [[verifying-subagent-claims]] — before accepting any stream as DONE
- `docs/quantum-lead-prompt.md` — full dispatch protocol + status-report format
