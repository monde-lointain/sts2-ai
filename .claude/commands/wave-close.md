---
allowed-tools: Bash(git tag:*), Bash(git rev-parse:*), Bash(.claude/scripts/write-wave-state.sh:*), Read, Write
description: "Close wave N: snapshot state to .claude/state/waves/<N>.json, tag the merge commit, append to wave log."
disable-model-invocation: false
---

Close wave `$1` (the wave number, e.g., `1`).

Steps:

1. **Read current state files**:
   - `.claude/state/current-wave.json` — for `started_at`, `pre_wave_sha`
   - `.claude/state/active-worktrees.json` — for stream list and merge status
   - `.claude/state/last-gate.json` — for final gate outcome

2. **Get the current main HEAD SHA** (this is the merged SHA):
   ```
   merged_sha=$(git rev-parse HEAD)
   closed_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
   ```

3. **Compose the wave snapshot** per SCHEMA.md `waves/<N>.json` shape:
   ```json
   {
     "wave_n": <N>,
     "started_at": "<from current-wave.json>",
     "closed_at": "<now>",
     "pre_wave_sha": "<from current-wave.json>",
     "merged_sha": "<HEAD>",
     "streams": [
       {
         "stream_id": "...",
         "branch": "...",
         "head_sha": "...",
         "status": "merged",
         "merged_at": "..."
       }
     ]
   }
   ```

4. **Write wave snapshot** via wrapper:
   ```
   .claude/scripts/write-wave-state.sh wave-snapshot "$1" "<JSON>"
   ```
   The wrapper creates `.claude/state/waves/` if needed.

5. **Tag the merge commit**:
   ```
   git tag wave-$1
   ```
   If tag already exists, warn but do not overwrite — report and halt.

6. **Append to wave log** at `.claude/state/wave-log.md` (create if missing). Append:
   ```
   ## Wave $1 (closed <YYYY-MM-DD>)
   <one-line summary: N streams, gate status from last-gate.json, merged SHA short>
   ```

7. **Update per-row port-decision status** (soft-fail — do not block wave close on failure):

   For each stream branch in the wave snapshot, invoke the status-update script in batch mode:
   ```bash
   for branch in <stream-branches>; do
     .claude/scripts/update-port-decision-status.sh --batch "$branch" \
       || echo "WARN: update-port-decision-status soft-failed for $branch — continuing" >&2
   done
   ```
   This bumps matching JSON sidecar rows to `MERGED` for any upstream paths ported in the wave.
   If the script is not present or jq is unavailable, the warning is logged and wave close proceeds.

8. Report: wave number, snapshot path, tag, log entry written, status-update result.
