---
allowed-tools: Bash(gh:*), Bash(git:*), Bash(.claude/scripts/write-ci-rescue.sh:*), Bash(jq:*), Bash(grep:*), Bash(sha256sum:*), Bash(sed:*), Bash(date:*), Bash(sort:*), Bash(cut:*), Bash(printf:*), Bash(echo:*), Bash(sleep:*), Bash(seq:*)
description: "Fetch failed GHA logs and iterate fixes until green or escalation. Usage: /ci-rescue [--auto-push] [--max-iterations N] [--workflow <name>]"
disable-model-invocation: false
---

Run the CI-rescue loop for the current branch.

Args (all optional):
- `--auto-push` — push each subagent fix without asking. Default: ask.
- `--max-iterations N` — cap iteration count. Default: 5.
- `--workflow <name>` — restrict to a specific workflow file (e.g. `ci.yml`, `pr-quality.yml`). Default: latest run on the branch regardless of workflow.

Steps:

1. Invoke `Skill(skill: "rescuing-ci-failures")` to load the algorithm, triage rules, stop conditions, and subagent dispatch template.

2. Parse args. Defaults: `auto_push=false`, `max_iterations=5`, `workflow=""` (any).

3. Run the algorithm verbatim. Every state transition goes through `.claude/scripts/write-ci-rescue.sh`.

4. On exit:
   - **Green** (code 0): report `attempted_fixes` summary + final `head_sha`. Run `write-ci-rescue.sh update-status green`.
   - **Escalation** (codes 10-13): AskUserQuestion with `escalation_reason`, the last 2 `attempted_fixes`, and the relevant log path (`/tmp/ci-rescue-<run_id>.log`). The state file is NOT reset — preserve it for postmortem. Tell the user how to clear it (`.claude/scripts/write-ci-rescue.sh reset`).

Examples:

```
/ci-rescue
/ci-rescue --auto-push
/ci-rescue --max-iterations 3 --workflow ci.yml
/ci-rescue --auto-push --max-iterations 10
```

Refuses to run when the current branch is `main` — that's an incident, not a rescue.
