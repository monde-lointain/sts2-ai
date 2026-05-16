---
allowed-tools: Bash(gh:*), Bash(git:*), Bash(jq:*)
description: "Pull failed-step logs from a GitHub Actions run. Usage: /ci-logs [<run-id>] [--full]"
disable-model-invocation: false
---

One-shot: download failed-step logs (or full logs) for inspection. No loop, no state writes. The 80% case before resorting to `/ci-rescue`.

Args (all optional):
- `<run-id>` — specific run database ID. Default: latest run on the current branch.
- `--full` — pull entire log (`--log`) instead of just failed steps (`--log-failed`).

Steps:

1. Resolve `run_id`:
   - If passed as `$1` and numeric → use it.
   - Else: `gh run list --branch "$(git rev-parse --abbrev-ref HEAD)" --limit 1 --json databaseId,conclusion,workflowName --jq '.[0]'`.

2. Echo: `run_id`, `workflow`, `conclusion`.

3. Pull logs:
   - Default: `gh run view <run_id> --log-failed > /tmp/ci-logs-<run_id>.log`
   - With `--full`: `gh run view <run_id> --log > /tmp/ci-logs-<run_id>.log`

4. Summarize the log:
   - Failed step headers (`grep -E '^##\[error\]'`).
   - First 20 lines of each failed step.
   - Total line count + file size.

5. Print the saved path: `/tmp/ci-logs-<run_id>.log`. User can `cat` or pass it to `/ci-rescue`.

Examples:

```
/ci-logs
/ci-logs 12345678901
/ci-logs --full
/ci-logs 12345678901 --full
```

Fails fast if `gh auth status` is not green.
