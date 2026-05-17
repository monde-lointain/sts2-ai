---
allowed-tools: Bash(make sync-check:*), Bash(cat .claude/state/upstream-drift-detected.json:*), Bash(test -f .claude/state/upstream-drift-detected.json:*)
description: "Run upstream sync-check and surface drift summary + recommended action if drift detected."
disable-model-invocation: false
---

Run `make sync-check` to check for upstream STS2 drift against the engine pin.

Steps:

1. Run sync-check:
   ```
   make sync-check
   ```
   Print the full output.

2. Check for sentinel:
   ```
   test -f .claude/state/upstream-drift-detected.json && cat .claude/state/upstream-drift-detected.json
   ```

3. Interpret results:
   - If sentinel **does not exist** and sync-check exited 0: no drift detected. Report "Upstream is pinned and current — no action required."
   - If sentinel **exists**: drift was detected (by this run or a prior cron run). Print the sentinel JSON and surface:
     - `current_version` vs pin in `engine/headless/upstream-pin.json`
     - Recommended next action: "Open a port-decision doc for vX.Y.Z and notify project-lead for ADR-027 (bridge Wave B.1)."
   - If sync-check exits non-zero without sentinel: likely Steam unavailable or lock contention. Report the error output and advise user to retry when Steam client is running.

4. If drift detected, print:
   ```
   Upstream drift detected: <pinned_version> -> <current_version>
   Bridge wave B.1 should be scheduled. See docs/plans/upstream-pipeline.md.
   ```
