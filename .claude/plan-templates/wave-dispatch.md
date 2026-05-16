# Wave-dispatch plan template

A consolidated template for the recurring sts2-ai plan shapes:
**wave-dispatch**, **schema-bump**, **ADR-ratification**. Fill the
variant-appropriate sections; delete the rest. Cross-link skills with
`[[name]]`.

---

## Context

<!-- Why this wave? What constraint, deadline, or stakeholder ask
prompted it? What is the intended outcome? Reference the project lead's
directive if applicable. -->

## Guardrails

- Reference any binding memory entries (e.g. `[[feedback-worktree-dispatch-protocol]]`).
- State invariants this plan must not break (cheap-clone, schema back-compat, etc.).
- One PR per wave (sub-streams = commits). R8 partition-by-file.

## Pre-wave snapshot

- Main SHA at plan time: `<sha>`
- Expected pre-flight SHA for dispatched subagents: `<same sha>`
- Active worktrees pre-wave: `<git worktree list excerpt>`

---

## VARIANT A — wave-dispatch (multi-stream)

### Sub-streams (file-disjoint per R8)

| ID | Goal | Files OWNED | Files FORBIDDEN | Verification |
|---|---|---|---|---|
| A.1 | … | … | … | `make q3-ci` clean |
| A.2 | … | … | … | … |
| A.3 | … | … | … | … |

### Dependency graph

- A.2 depends on A.1 landing on main first? (state explicitly)
- A.3 file-disjoint from A.1/A.2 → fully parallel

### Dispatch protocol

Invoke `[[dispatching-a-wave]]`. Per-agent prompt template lives there.
Verify before merging: `[[verifying-subagent-claims]]`. Merge sequence:
`[[merging-a-wave]]`.

---

## VARIANT B — schema-bump

Invoke `[[bumping-a-schema-version]]`. Steps:

1. Edit `.proto` in `contracts/schemas/<schema>/`
2. `make schema-codegen`
3. Sweep v(N-1)-pinned fixtures; retain a v(N-1) variant for the multi-version reader test
4. Update affected module specs under `docs/specs/modules/`
5. Cross-quantum coord (ADR-001): flag to project-lead
6. Author ADR via `[[creating-an-adr]]`
7. PreToolUse hook logs to `.claude/state/proto-edits-pending-adr.json`; pre-push gate enforces ADR pairing

---

## VARIANT C — ADR ratification

Invoke `[[creating-an-adr]]`. Sections:

1. Title + Status (PROPOSED → ACCEPTED on ratification)
2. Context (what made this a decision)
3. Decision (the chosen path)
4. Consequences (negatives FIRST, then positives)
5. Update affected module specs and any pinned fixtures (if schema-adjacent — see Variant B)

---

## Verification (end-to-end)

- Specific commands per gate (use `[[running-a-quantum-ci-gate]]`)
- Expected pass counts, timings
- Memory entries to add/retire (if any)
- State-file shape (if `.claude/state/*` touched — see SCHEMA.md)

## Resolutions

<!-- Bake in decisions instead of leaving open. See
[[feedback-bake-decisions-in-plans]]. Surface true blockers as a short
unresolved-questions list at the end only when reasoning is genuinely
impossible without the user. -->

---

## Unresolved questions

<!-- Only items that genuinely require the user's judgment. Empty is good. -->
