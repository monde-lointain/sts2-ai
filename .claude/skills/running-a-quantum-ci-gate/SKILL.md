---
name: running-a-quantum-ci-gate
description: Use when running a quantum CI gate (Q1–Q12) for verification. Codifies the gate-per-quantum mapping, the run_in_background mandate, and expected wall-clock budgets so timeouts don't surprise.
---

# Running a Quantum CI Gate

## Gate table

| Quantum / Gate | Command | Wall-clock | Background required? |
|---|---|---|---|
| Q1 (Game Simulator) | `make q1-ci` | ~3 min | Recommended |
| Q2 (Oracle) — fast | `make test` | ~30 s | Inline OK |
| Q2 (Oracle) — full regression | `make q2-ci` | ~18 min | **MANDATORY** |
| Q3 (Experience Store) | `make q3-ci` | ~10 s | Inline OK |
| Q10 (Trainer) | `make q10-ci` | ~30 s | Inline OK |
| Phase 0 full gate | `make phase0-gate` | ~20 min | **MANDATORY** |
| Sanitizer (ASan + UBSan) | `make sanitize-test` | ~10 min | **MANDATORY** |

`make phase0-gate` runs: `test q1-ci schema-test services-smoke content-test q3-ci q10-ci` in sequence.

## The 10-min Bash timeout rule

The Bash tool times out at 10 minutes. Any gate marked **MANDATORY** above exceeds or risks this limit. Use `run_in_background: true` for those — no exceptions.

```
# Correct: background for long gates
Bash(command="make q2-ci", run_in_background=true)

# Wrong: will timeout and leave build in undefined state
Bash(command="make q2-ci")   # ← times out at 10 min
```

Do not chain `sleep` loops to work around the timeout. Background + Monitor is the correct pattern.

## State write after gate

After any gate completes, write `.claude/state/last-gate.json` via:

```bash
.claude/scripts/write-gate-status.sh <quantum> <command> <exit_code> <duration_s>
```

The `/q-ci` slash command wraps this automatically. If running a gate manually, call the script explicitly — do not skip the state write.

Schema for `last-gate.json`:
```json
{
  "quantum": "q3",
  "target": "make q3-ci",
  "status": "pass | fail | running",
  "exit_code": 0,
  "duration_s": 11.02,
  "started_at": "2026-05-16T08:05:14Z",
  "ended_at": "2026-05-16T08:05:25Z",
  "head_sha": "..."
}
```

## Venv discipline in worktrees

Makefile uses `$(VENV)` resolved via `$(abspath ...)` relative to `--git-common-dir`. In a worktree, this resolves to the main repo's `.venv` — correct. Do NOT invoke `make` from a subdirectory within a worktree; the `$(abspath ...)` chain breaks for sub-CWDs.

Always run `make <target>` from the worktree root (or main repo root for merges).

## Slash command shortcut

`/q-ci <q>` — runs `make q<q>-ci` with `run_in_background: true` and writes `last-gate.json`. For `phase0-gate` and `sanitize-test`, use `/phase0-gate` (which also backgrounds).

## Which gate to run?

| Situation | Gate |
|---|---|
| Touched Q3 files only | `make q3-ci` |
| Touched Q10 files only | `make q10-ci` |
| Touched `contracts/schemas/` | `make schema-test` + affected quanta |
| Touched `engine/cpp/` | `make test` (fast) then `make q2-ci` (full, background) |
| Pre-merge smoke (any wave) | `make q3-ci` or `make q10-ci` (fast) |
| Pre-PR full verification | `make phase0-gate` (background, ~20 min) |
| Suspected memory corruption | `make sanitize-test` (background, ~10 min) |

## Cross-references

- [[merging-a-wave]] — smoke gate is mandatory between stream merges
- [[verifying-subagent-claims]] — gate results are part of claim verification
- `.claude/state/SCHEMA.md` — `last-gate.json` schema
