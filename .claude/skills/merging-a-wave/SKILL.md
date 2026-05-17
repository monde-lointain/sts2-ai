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

## ABORTED close-out

A wave is ABORTED when it cannot complete (gate permanently red, conflicting scope, blocked on external dependency, or explicitly recalled by project-lead). ABORTED close-out differs from the rollback protocol: rollback resets main; ABORTED close-out preserves the branch as a reference artifact.

### When to use ABORTED vs rollback

- **Rollback** (§ above): gate is red but the problem is fixable; you intend to re-dispatch the failing stream and re-merge.
- **ABORTED**: the wave is abandoned entirely — the branch is preserved for archaeology but will not be merged in this wave cycle. May be re-dispatched fresh in a later wave.

### ABORTED close-out steps

1. **Do not reset main.** If any streams already merged, they stay. The merged work is forward-looking; the ABORTED designation applies to the unmerged remainder.

2. **Preserve branch(es).** Do NOT delete worktree branches for aborted streams. Leave them as `worktree-agent-<id>` (or rename to `aborted/<wave-N>-<stream-id>` for clarity):
   ```bash
   # From main repo root:
   git branch -m worktree-agent-<id> aborted/wave-N-<stream-id>
   ```
   Do NOT run `git worktree remove` on the aborted stream's worktree until the rename is done.

3. **Tag the abort point** using the standard wave tag convention:
   ```bash
   git tag wave-N    # on main HEAD — tags whatever DID merge (may be 0 commits if nothing merged)
   ```
   If the wave tag already exists from a partial merge run, skip re-tagging (do not overwrite).

4. **Write wave snapshot** with `status: "aborted"`:
   ```bash
   .claude/scripts/write-wave-state.sh wave-snapshot N "$(cat <<'EOF'
   {
     "wave_n": N,
     "started_at": "<from current-wave.json>",
     "closed_at": "<now>",
     "pre_wave_sha": "<from current-wave.json>",
     "merged_sha": "<HEAD at abort>",
     "status": "aborted",
     "aborted_reason": "<brief reason>",
     "streams": [
       {
         "stream_id": "...",
         "branch": "aborted/wave-N-<stream-id>",
         "head_sha": "...",
         "status": "aborted"
       }
     ]
   }
   EOF
   )"
   ```

5. **Append to wave log** at `.claude/state/wave-log.md`:
   ```
   ## Wave N (ABORTED <YYYY-MM-DD>)
   <reason; reference preserved branch name(s)>
   ```

6. **Update active-worktrees.json** — set aborted stream `status: "aborted"`.

7. **Invoke status-update script** (soft-fail — do not block abort):
   ```bash
   .claude/scripts/update-port-decision-status.sh --batch aborted/wave-N-<stream-id> || \
     echo "WARN: status-update soft-failed on ABORTED close-out" >&2
   ```

8. **Remove worktrees** for the aborted streams (the branch is preserved; only the worktree directory is cleaned up):
   ```bash
   git worktree remove .claude/worktrees/agent-<id>
   ```

### ABORTED pre-checklist

- [ ] main NOT reset (unless explicit rollback intent)
- [ ] Aborted stream branches renamed to `aborted/wave-N-<stream-id>`
- [ ] `wave-N` tag present on main HEAD
- [ ] Wave snapshot written with `status: "aborted"` and `aborted_reason`
- [ ] Wave log entry appended
- [ ] `active-worktrees.json` updated
- [ ] Worktrees cleaned up (directories removed; branches preserved)

## Cross-references

- [[verifying-subagent-claims]] — gate before each merge
- [[running-a-quantum-ci-gate]] — smoke gate commands + wall-clock budgets
- `.claude/state/SCHEMA.md` — `current-wave.json` rollback_target field
- `.claude/scripts/update-port-decision-status.sh` — per-row JSON sidecar status update (invoked by /wave-close and ABORTED close-out)
