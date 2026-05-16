## Workflow

This project codifies its Claude Code workflow under `.claude/`. Use the
artifacts here instead of re-deriving each session.

**Subagents** (`.claude/agents/`) — invoke via `Agent(subagent_type: ...)`:
- `project-lead` — cross-quantum orchestration (Q1–Q12). Opus.
- `quantum-lead` — single-quantum orchestrator dispatching engineer subagents to worktrees. Opus.
- `quantum-architect` — read-only structural design + ADR authoring. Opus.

**Skills** (`.claude/skills/<name>/SKILL.md`) — invoke via `Skill(skill: <name>)`:
- `dispatching-a-wave` — worktree-per-subagent, preflight-SHA, file partition.
- `merging-a-wave` — main-CWD invariant, sequential merge, rollback protocol.
- `creating-an-adr` — number sequencing, template, Consequences-leads-with-negatives.
- `bumping-a-schema-version` — proto → codegen → fixture sweep → ADR.
- `running-a-quantum-ci-gate` — gate-per-quantum table; mandatory backgrounding.
- `verifying-subagent-claims` — augments superpowers:verification-before-completion.
- `rescuing-ci-failures` — remote GHA fail → log pull → triage → fix subagent → push loop, bounded by iteration cap + same-error dedup.

**Slash commands** (`.claude/commands/`):
- `/wave-dispatch`, `/wave-merge`, `/wave-close` — wave lifecycle.
- `/q-ci <q>`, `/phase0-gate` — backgrounded gate runs (writes `.claude/state/last-gate.json`).
- `/adr-new <title>`, `/worktree-cleanup`, `/ground-as <persona>` — utility ops.
- `/ci-rescue [--auto-push] [--max-iterations N]` — fetch failed GHA logs and iterate fixes until green or escalation (writes `.claude/state/ci-rescue.json`).
- `/ci-logs [<run-id>] [--full]` — one-shot pull of failed-step logs to `/tmp/ci-logs-<run-id>.log` (no loop, no state).

**Hooks** (`.claude/hooks/`) — auto-fire on tool calls:
- `block-system-python` — refuse bare `python` outside `.venv/bin/`.
- `block-merge-in-worktree`, `block-dirty-worktree-merge` — enforce main-CWD merge invariant.
- `warn-foreground-longrun` — flag `make q2-ci|phase0-gate|sanitize*` without backgrounding.
- `proto-edit-tracker` — log `contracts/schemas/*.proto` edits to `.claude/state/proto-edits-pending-adr.json`.

**State contract** — `.claude/state/SCHEMA.md` documents JSON shapes for
runtime files. Writes go via `.claude/scripts/write-*.sh` (never trust
prompt-only persistence).

**Plan templates** — `.claude/plan-templates/wave-dispatch.md` covers
wave-dispatch / schema-bump / ADR-ratification variants.

## Quantum Map (Q1–Q12)

| Q | Name | Substrate | Module spec |
|---|---|---|---|
| Q1 | Game Simulator | `engine/headless/` (C#) | `docs/specs/modules/game-simulator.md` |
| Q2 | Oracle Verifier | `engine/cpp/` (expectimax) | `docs/specs/modules/oracle.md` |
| Q3 | Experience Store | `pipeline/experience-store/` | `docs/specs/modules/experience-store.md` |
| Q4 | Content Registry | `contracts/registry/` | `docs/specs/modules/content-registry.md` |
| Q5 | Model Registry | `pipeline/model-registry/` | `docs/specs/modules/model-registry.md` |
| Q6 | Evaluation Reports | output of Q12 | `docs/specs/modules/evaluation-reports.md` |
| Q7 | Observability TSDB | `pipeline/observability/` | `docs/specs/modules/observability.md` |
| Q8 | Rollout Workers | `pipeline/rollout-workers/` | `docs/specs/modules/rollout-workers.md` |
| Q9 | Inference Server | `pipeline/inference-server/` | `docs/specs/modules/inference-server.md` |
| Q10 | Trainer | `pipeline/trainer/` | `docs/specs/modules/trainer.md` |
| Q11 | Curriculum Generator | TBD (Phase 2+) | `docs/specs/modules/curriculum-generator.md` |
| Q12 | Evaluation Harness | `pipeline/evaluation-harness/` | `docs/specs/modules/evaluation-harness.md` |

Cross-quantum contracts in `contracts/schemas/` are versioned per ADR-001.
A schema edit is a cross-quantum coordination event — surface to project-lead.

## Wave Protocol

A wave = one PR. Sub-streams = commits within. File-disjoint per R8.

1. **Capture pre-wave SHA** into `.claude/state/current-wave.json`.
2. **Dispatch** engineer subagents into per-stream worktrees via
   `Agent(isolation: "worktree")`. Wave-N>0 prompts must include
   expected-SHA pre-flight (`git merge --ff-only main`) — auto-worktree
   base may be stale.
3. **Verify each subagent's branch** before merging:
   `git diff --name-only main..<branch>` must cover the claimed file list;
   `git log main ^<branch>` should be empty (or your work landed on main directly — see `[[feedback-subagent-commit-target]]`).
4. **Merge from main CWD only** (`[[feedback-worktree-dispatch-protocol]]`).
   FF if file-disjoint; otherwise resolve.
5. **Gate** the merged main (`/q-ci`, `/phase0-gate` — backgrounded).
6. **Close** the wave (`/wave-close <N>`) — snapshot to
   `.claude/state/waves/<N>.json`, tag, log.

## Model tiering

| Role | Model | Rationale |
|---|---|---|
| Orchestrators (project-lead, quantum-lead, quantum-architect) | Opus 4.7 | Strategy + design judgment |
| Engineer / general-purpose dispatch subagents | Sonnet 4.6 | Implementation; opt to Opus only for architecturally tricky sub-streams |
| Plan subagents (plan-mode Phase 2) | Sonnet 4.6 | Synthesis but not strategy |
| Explore subagents (plan-mode Phase 1, read-only sweeps inside skills) | Haiku 4.5 | Fast search/read |

In dispatch prompts, the orchestrator chooses model explicitly per stream.

## C++ Coding Requirements

- Follow the C++ Core Guidelines in all code: safety, clarity, RAII, value semantics, and avoidance of undefined behavior.
- Adhere to established C++ coding style conventions: consistent naming, concise comments only where needed, clear ownership, and minimal global state.
- Prefer modern C++ features: smart pointers over raw pointers, range-based loops, `auto` where it improves clarity, `constexpr` when appropriate.
- Enforce strong type safety; avoid implicit conversions and unsafe casts.
- Use exceptions for error handling unless performance constraints dictate otherwise; never use error codes for normal flow.
- Ensure thread safety, avoid data races, and use standard concurrency utilities.
- Optimize only when necessary and never at the expense of readability.
- Write code that compiles cleanly on modern C++ compilers with warnings enabled and treated as errors.
