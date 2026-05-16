---
allowed-tools: Bash(make q*-ci:*), Bash(make test:*), Bash(.claude/scripts/write-gate-status.sh:*), Bash(git rev-parse:*), Bash(date:*)
description: "Run the named quantum's CI gate, write outcome to last-gate.json. Usage: /q-ci q3 -> make q3-ci"
disable-model-invocation: false
---

Run the CI gate for quantum `$1`.

Quantum → make target mapping:
- q1 → `make q1-ci`
- q2 → `make q2-ci` (~18 min — MUST use run_in_background: true)
- q3 → `make q3-ci`
- q10 → `make q10-ci`
- phase0 → use `/phase0-gate` instead

Steps:

1. Validate that `$1` is one of: q1, q2, q3, q10. If not, halt and explain.

2. Record the start time:
   ```
   start_ts=$(date +%s)
   head_sha=$(git rev-parse HEAD)
   ```

3. Run the gate. For q2, you MUST set `run_in_background: true` on the Bash call — it takes ~18 min and will time out otherwise. For q1, q3, q10 inline execution is fine.
   - q1: `make q1-ci`
   - q2: `make q2-ci` (background)
   - q3: `make q3-ci`
   - q10: `make q10-ci`

   Capture the exit code.

4. Compute duration:
   ```
   end_ts=$(date +%s)
   duration_s=$((end_ts - start_ts))
   ```

5. Map exit code → status string: 0 → "pass", non-zero → "fail".

6. Write outcome to state:
   ```
   .claude/scripts/write-gate-status.sh "$1" "make $1-ci" "$status" $exit_code $duration_s "$head_sha"
   ```

7. Report result: quantum, status (pass/fail), duration, exit code, and path to `last-gate.json`.
