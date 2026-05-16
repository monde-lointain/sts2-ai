---
allowed-tools: Bash(make phase0-gate:*), Bash(.claude/scripts/write-gate-status.sh:*), Bash(git rev-parse:*), Bash(date:*)
description: Run the full Phase 0 gate backgrounded (~20 min). Writes outcome to last-gate.json.
disable-model-invocation: false
---

Run the full Phase 0 gate. This takes ~20 minutes — MUST run backgrounded.

Steps:

1. Record start state:
   ```
   start_ts=$(date +%s)
   head_sha=$(git rev-parse HEAD)
   ```

2. Write running status to state immediately:
   ```
   .claude/scripts/write-gate-status.sh "phase0" "make phase0-gate" "running" 0 0 "$head_sha"
   ```

3. Run the gate with `run_in_background: true`:
   ```
   make phase0-gate
   ```
   Capture exit code when it completes.

4. Compute duration:
   ```
   end_ts=$(date +%s)
   duration_s=$((end_ts - start_ts))
   ```

5. Map exit code → status: 0 → "pass", non-zero → "fail".

6. Write final outcome:
   ```
   .claude/scripts/write-gate-status.sh "phase0" "make phase0-gate" "$status" $exit_code $duration_s "$head_sha"
   ```

7. Report: status, duration, exit code, path to `last-gate.json`. If failed, surface the last 50 lines of output.
