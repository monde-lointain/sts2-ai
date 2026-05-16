---
allowed-tools: Bash(git worktree:*), Bash(git rev-parse:*), Bash(.claude/scripts/write-wave-state.sh:*), Read, Agent
description: "Dispatch a wave of engineer subagents for the named quantum. Usage: /wave-dispatch <quantum> <streams-file>"
disable-model-invocation: false
---

Dispatch a parallel wave of engineer subagents.

Args:
- `$1`: quantum name (e.g., q3)
- `$2`: path to streams JSON file — an array of objects with keys: `stream_id`, `owned_files`, `forbidden_files`, `goal`, `verification`

Steps:

1. **Read the streams file** at path `$2`. Validate it is a JSON array. If not, halt.

2. **Capture pre-wave state.** Get the current main tip SHA:
   ```
   pre_wave_sha=$(git rev-parse HEAD)
   now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
   ```
   Build the stream IDs list from the JSON, then write current-wave.json:
   ```
   .claude/scripts/write-wave-state.sh current-wave "$1" \
     "{\"wave_n\": \"$1\", \"started_at\": \"$now\", \"pre_wave_sha\": \"$pre_wave_sha\", \"expected_streams\": [...], \"rollback_target\": \"$pre_wave_sha\"}"
   ```

3. **Write initial active-worktrees.json** with status "pending" for each stream:
   ```
   .claude/scripts/write-wave-state.sh active-worktrees "$1" "{\"wave_n\": \"$1\", \"entries\": [...]}"
   ```

4. **Dispatch all subagents in parallel** — issue all Agent calls in a single message, one per stream.

   Each Agent call:
   - `subagent_type: general-purpose`
   - `model: sonnet`
   - `isolation: worktree`
   - Prompt must include the following pre-flight block (substituting actual values):

   ```
   ## Pre-flight (CRITICAL)
   Expected main SHA: <pre_wave_sha>

   git rev-parse HEAD
   git fetch origin
   git log --oneline main -1

   If main tip != <pre_wave_sha>, STOP and report.
   Otherwise: git merge --ff-only main
   If FF fails, STOP and report.
   ```

   The prompt must also specify:
   - `stream_id`, `owned_files`, `forbidden_files`, `goal`, `verification` from the streams JSON
   - File-ownership boundary: only touch `owned_files`; never touch `forbidden_files`
   - Venv discipline: all Python via `.venv/bin/python` (absolute path resolved via `git rev-parse --git-common-dir`)
   - Commit style: one commit per logical unit, concise messages

   Reference skill: `[[dispatching-a-wave]]` for full dispatch-prompt template.

5. **After all agents complete**, update active-worktrees.json with actual branch names and head SHAs for each stream.

6. Report: wave N, stream count, pre-wave SHA, each stream's branch name.
