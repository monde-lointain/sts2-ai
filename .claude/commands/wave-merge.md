---
allowed-tools: Bash(git merge:*), Bash(git checkout main:*), Bash(git rev-parse:*), Bash(pwd:*), Bash(make q3-ci:*), Bash(make test:*), Bash(.claude/scripts/write-wave-state.sh:*), Read
description: Sequentially merge wave sub-stream branches into main. Asserts main-CWD invariant; runs smoke gate after each merge.
disable-model-invocation: false
---

Sequentially merge all sub-stream branches from the current wave into main.

Steps:

1. **Assert CWD invariant.** Run `pwd`. The result must NOT contain `.claude/worktrees/`. If it does, halt immediately:
   ```
   STOP: /wave-merge must run from main repo root, not a worktree.
   Current CWD: <pwd output>
   ```

2. **Read stream list** from `.claude/state/active-worktrees.json`. Collect entries with status != "merged". If file missing or empty, halt.

3. **Read pre-wave SHA** from `.claude/state/current-wave.json` field `rollback_target`. This is the safety reset point.

4. **For each stream (in order by stream_id)**:

   a. Attempt fast-forward merge (MUST be FF-only — no octopus merges):
      ```
      git merge --ff-only <branch>
      ```
      If FF fails: STOP. Report which stream failed, the conflict, and the rollback target SHA. Do NOT proceed to next stream.

   b. Run smoke gate (inline, not backgrounded):
      ```
      make q3-ci
      ```
      If gate fails: STOP. Surface the failure. Advise manual rollback to `rollback_target` if needed.

   c. If gate passes, update that stream's entry in active-worktrees.json to `status: "merged"` and record `merged_at` timestamp:
      ```
      .claude/scripts/write-wave-state.sh active-worktrees <wave_n> "<updated JSON>"
      ```

5. **After all streams merged**, report: streams merged, final main SHA, any that were skipped.

Reference skill: `[[merging-a-wave]]`
