# `.claude/state/` JSON contract

Runtime state for slash commands, hooks, and statusline. Files here are
machine-local (gitignored except this doc). Wrappers in `.claude/scripts/`
own writes — never write from prompt-only command bodies.

## `last-gate.json`

Written by `/q-ci` and `/phase0-gate` via `.claude/scripts/write-gate-status.sh`.
Read by statusline (`context-bar.sh`).

```json
{
  "quantum": "q3",
  "target": "make q3-ci",
  "status": "pass | fail | running",
  "exit_code": 0,
  "duration_s": 11.02,
  "started_at": "2026-05-16T08:05:14Z",
  "ended_at": "2026-05-16T08:05:25Z",
  "head_sha": "c552aba..."
}
```

## `active-worktrees.json`

Written by `/wave-dispatch`, `/worktree-cleanup`. Read by `/wave-dispatch` idempotency check and `/wave-merge`. Snapshot of `git worktree list` enriched with stream metadata.

```json
{
  "wave_n": 1,
  "entries": [
    {
      "stream_id": "1.1",
      "branch": "worktree-agent-aabc123...",
      "path": "/home/.../.claude/worktrees/agent-aabc123...",
      "base_sha": "c552aba...",
      "head_sha": "c552aba...",
      "status": "pending | running | done | merged"
    }
  ]
}
```

## `current-wave.json`

Written at `/wave-dispatch`; read by `/wave-merge`, `/wave-close`, and the
rollback path in the `merging-a-wave` skill.

```json
{
  "wave_n": 1,
  "started_at": "2026-05-16T08:30:00Z",
  "pre_wave_sha": "c552aba...",
  "expected_streams": ["1.1", "1.2", "1.3", "1.4"],
  "rollback_target": "c552aba..."
}
```

`rollback_target` lets `merging-a-wave` reset main if a stream merge fails post-merge of an earlier stream.

## `waves/<N>.json`

Written by `/wave-close`. Archive of the closed wave. Immutable once written.

```json
{
  "wave_n": 1,
  "started_at": "2026-05-16T08:30:00Z",
  "closed_at": "2026-05-16T11:14:32Z",
  "pre_wave_sha": "c552aba...",
  "merged_sha": "f7a9d12...",
  "streams": [
    {
      "stream_id": "1.1",
      "branch": "worktree-agent-aabc123",
      "head_sha": "...",
      "status": "merged",
      "merged_at": "2026-05-16T10:42:11Z"
    }
  ]
}
```

## `proto-edits-pending-adr.json`

Written by `.claude/hooks/proto-edit-tracker.py` (PreToolUse Edit/Write on
`contracts/schemas/*.proto`). Read by
`.claude/hooks/pre-push-proto-adr-gate.py`. Entries cleared once an ADR
referencing the proto edit lands.

```json
{
  "entries": [
    {
      "file": "contracts/schemas/trajectory.proto",
      "edited_at": "2026-05-16T09:12:04Z",
      "agent": "general-purpose | quantum-lead | ...",
      "head_sha_at_edit": "...",
      "adr_ref": null
    }
  ]
}
```

`adr_ref` is set when a corresponding ADR is created (manual or via
`/adr-new`). pre-push gate blocks main-bound push if any entry's
`adr_ref` is null AND no ADR with `Date: >= edited_at` references the
file path.

## `spec-edits-pending-resolution.json`

Written by `.claude/hooks/spec-edit-tracker.py` (PreToolUse Edit/Write on
`docs/specs/modules/*.md` or `docs/specs/00-system-overview.md`). Read by
`.pre-commit-hooks/pre-push-spec-resolution-gate.py`. Per ADR-024.

```json
{
  "entries": [
    {
      "file": "docs/specs/modules/trainer.md",
      "edited_at": "2026-05-16T23:36:03Z",
      "agent": "Edit | Write | MultiEdit",
      "head_sha_at_edit": "9d703c5...",
      "resolution": null
    }
  ]
}
```

`resolution` field reserved for future manual marking (e.g., explicit
substrate-paired commit SHA). Current gate logic ignores this field and
checks resolution dynamically against pushed commits: passes if the
pushed range touches the spec's frontmatter `substrate:` path OR any
commit message contains `doc-only:`. **Phase 3a is warn-only**; promotes
to block in ADR-N+2 after two silent wave cycles.

Entry cleanup: no automatic prune today — entries accumulate. Manual
cleanup at wave-close is acceptable; full lifecycle TBD per ADR-024
Consequences §1.

## `ci-rescue.json`

Written by `.claude/scripts/write-ci-rescue.sh` (invoked from the
`rescuing-ci-failures` skill / `/ci-rescue` command). Tracks the
iterative CI-fix loop: failed-run identity, accumulated fix attempts,
and the dedup signature that short-circuits same-error-twice loops.

```json
{
  "status": "running | green | escalated | idle",
  "branch": "wave-foo",
  "head_sha": "abc1234...",
  "current_run_id": 12345678901,
  "workflow": "ci.yml",
  "iteration_count": 2,
  "max_iterations": 5,
  "auto_push": false,
  "last_error_signature": "sha256:...",
  "attempted_fixes": [
    {"iteration": 1, "category": "lint", "summary": "ruff S603 in tools/upstream-sync", "commit_sha": "def456..."},
    {"iteration": 2, "category": "test-failure", "summary": "CombatEngineTests.MonsterDealsDamage", "commit_sha": "789abc..."}
  ],
  "started_ts": "2026-05-16T14:23:01Z",
  "last_update_ts": "2026-05-16T14:47:22Z",
  "escalation_reason": null
}
```

`status` transitions: `idle → running → (green | escalated) → idle`.
`escalation_reason` enum: `same-error-twice | iteration-cap |
unactionable | divr | cancelled-run | gh-auth-failure`. The minimal
idle file is `{"status":"idle"}`; full shape only after a rescue
initializes.

`attempted_fixes[*].category` enum: `compiler-error | test-failure |
lint | unactionable`.

## Schema discipline

- Keys: snake_case
- Timestamps: ISO 8601 UTC (`Z` suffix)
- SHAs: full 40-char or truncated to 7 — be explicit per file
- Optional fields: include with `null`, don't omit
- Files at `.claude/state/*.json` are machine-local; only SCHEMA.md tracks
- Wrappers in `.claude/scripts/` must `mkdir -p .claude/state/waves/` and use atomic write (`jq` to temp + `mv`)
