---
name: merging-a-wave
description: Use when merging completed wave sub-stream branches back into main. Codifies main-CWD-only invariant, absolute-path git, sequential merges with mid-merge gating, worktree cleanup, and rollback protocol.
---

# Merging a Wave

## HARD RULE — Main-CWD invariant

**Always `cd` to main repo root before any `git merge`.** Use absolute paths.

```bash
cd /home/clydew372/development/projects/cpp/sts2-ai   # full absolute path
git merge --ff-only <stream-branch>
```

Never merge from inside a worktree. The `.claude/hooks/block-merge-in-worktree.py` PreToolUse hook enforces this — but the skill is the doctrine. The 2026-05-14 incident: orchestrator ran `git merge feature-x` from a residual worktree CWD, landing the merge on the wrong branch. Recovery required reflog archaeology. The invariant eliminates this class of error.

## Sequential merge protocol

Merge one stream at a time. Never all-at-once.

```
Stream A → smoke gate → Stream B → smoke gate → … → final gate
```

For each stream:

1. **Verify stream DONE** via [[verifying-subagent-claims]] first.
2. From main repo root:
   ```bash
   git merge --ff-only <stream-branch>   # file-disjoint → always FF
   ```
   If FF fails: streams share a file — resolve conflict or abort and serialize.
3. **Run smoke gate** immediately after merge:
   ```bash
   make q3-ci    # or quantum-appropriate fast gate; see [[running-a-quantum-ci-gate]]
   ```
   Gate red → **stop**. Do not merge the next stream. Investigate the failing stream, fix, then continue.
4. Proceed to next stream only after gate green.

## Worktree teardown (per stream, after merge)

```bash
# From main repo root:
git worktree unlock .claude/worktrees/agent-<id>   # if Claude-locked
git worktree remove .claude/worktrees/agent-<id>
git branch -D worktree-agent-<id>
```

Update `.claude/state/active-worktrees.json` — set stream `status: "merged"`.

## Rollback protocol

If a mid-wave merge fails (gate red after merge of stream X):

1. Read `pre_wave_sha` from `.claude/state/current-wave.json`.
2. From main repo root:
   ```bash
   git reset --hard <pre_wave_sha>
   ```
3. Re-investigate the failing stream. Fix and re-dispatch only the affected stream.
4. Do **not** re-dispatch already-merged streams — their commits are gone with the reset; confirm with `git log` before re-dispatch.

## State writes

After all streams merged, call `/wave-close <N>` to write `.claude/state/waves/<N>.json` and tag the merge commit.

## Pre-merge checklist (per stream)

- [ ] `cd` to main repo root confirmed (absolute path, not worktree)
- [ ] Stream verified via [[verifying-subagent-claims]]
- [ ] `git merge --ff-only` (not plain `git merge`)
- [ ] Smoke gate green before proceeding to next stream
- [ ] Worktree removed + branch deleted after merge
- [ ] `.claude/state/active-worktrees.json` updated

## Cross-references

- [[verifying-subagent-claims]] — gate before each merge
- [[running-a-quantum-ci-gate]] — smoke gate commands + wall-clock budgets
- `.claude/state/SCHEMA.md` — `current-wave.json` rollback_target field
