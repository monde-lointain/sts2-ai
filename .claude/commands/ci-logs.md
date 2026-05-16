---
allowed-tools: Bash(gh:*), Bash(git:*), Bash(jq:*)
description: "Pull failed-step logs from a GitHub Actions run. Usage: /ci-logs [<run-id>] [--full]"
disable-model-invocation: false
---

One-shot: download failed-step logs (or full logs) for inspection. No loop, no state writes. The 80% case before resorting to `/ci-rescue`.

Args (all optional):
- `<run-id>` — specific run database ID. Default: latest completed run on the current branch (or latest failed one if the head run is still pending).
- `--full` — fetch logs for **all** jobs (passing + failing). Default: failed jobs only. The API returns one log per job, not per step.

## Why this command does not use `gh run view --log[-failed]`

`gh run view <id> --log` and `gh run view --job <id> --log-failed` are unreliable: they often exit 0 with **empty output** (no stderr, no warning). Reproduced on multi-job CI runs more than ~1 hour old, on matrix jobs, and on jobs with long step names. The CLI internally downloads a ZIP and extracts per-step text files; extraction silently produces nothing in those cases.

Use the REST API per failed job instead — `GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs` reliably returns the full timestamp-prefixed job log.

Steps:

1. Resolve `run_id`:
   - If passed as `$1` and numeric → use it.
   - Else: `gh run list --branch "$(git rev-parse --abbrev-ref HEAD)" --limit 1 --json databaseId,conclusion,workflowName,status --jq '.[0]'`.
   - If the latest run has `status != "completed"` (pending / in_progress / queued), fall back to the most recent completed/failure run:
     ```bash
     gh run list --branch "$(git rev-parse --abbrev-ref HEAD)" --limit 10 \
       --json databaseId,conclusion,status,workflowName,createdAt \
       --jq '[.[] | select(.status=="completed" and .conclusion=="failure")][0]'
     ```
     Tell the caller which run was picked and why.

2. Echo: `run_id`, `workflow`, `conclusion`.

3. Enumerate failed jobs and pull each via API:

   ```bash
   REPO="$(gh repo view --json nameWithOwner --jq .nameWithOwner)"
   OUT="/tmp/ci-logs-${run_id}.log"
   : > "$OUT"

   # Default: failed jobs only. With --full: all jobs (drop the select()).
   mapfile -t jobs < <(gh run view "$run_id" --json jobs \
     --jq '.jobs[] | select(.conclusion=="failure") | "\(.databaseId)\t\(.name)"')

   if [[ ${#jobs[@]} -eq 0 ]]; then
     echo "no failed jobs in run $run_id (run may have been cancelled or failed at workflow level)"
     exit 0
   fi

   for line in "${jobs[@]}"; do
     job_id="${line%%$'\t'*}"; name="${line#*$'\t'}"
     printf '\n========== JOB %s — %s ==========\n\n' "$job_id" "$name" >> "$OUT"
     gh api "repos/$REPO/actions/jobs/$job_id/logs" >> "$OUT" 2>&1
   done
   ```

   The API can also be parallelized — each job is an independent request. For 6+ failed jobs, fire them in parallel and concatenate after.

4. Summarize the log. Lines are prefixed with `YYYY-MM-DDThh:mm:ss.sssssssZ ` from the API, so anchored regexes (`^##\[error\]`) fail — use unanchored or strip the timestamp first:
   - All error-marker lines: `grep -E '##\[error\]' "$OUT" | head -20` (these are generic exit-code lines).
   - **Real failure causes**: `tail -n 30` of each per-job section, where the actual error message lives just before the `##[error]Process completed with exit code N` line.
   - Total line count + file size: `wc -l "$OUT"; ls -la "$OUT"`.

5. Print the saved path: `/tmp/ci-logs-<run_id>.log`. User can `cat` or pass it to `/ci-rescue`.

Examples:

```
/ci-logs
/ci-logs 12345678901
/ci-logs --full
/ci-logs 12345678901 --full
```

Fails fast if `gh auth status` is not green.
