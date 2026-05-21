---
name: dispatching-a-wave
description: Use when planning a multi-stream wave dispatch in the sts2-ai initiative (Q1–Q12 quanta). Codifies worktree-per-subagent dispatch, file-ownership partition (R8), pre-flight SHA check for wave N>0, and the dispatch-prompt template.
---

# Dispatching a Wave

A wave = one PR. Sub-streams = per-stream commits merged sequentially into that PR. Sub-streams must be file-disjoint (R8 partition). Never let two sub-streams touch the same file — serialize those instead.

## Step 1 — Capture pre-wave SHA

Before any worktree creation, record the current main tip:

```bash
git rev-parse HEAD  # must match expected SHA from plan
```

Write to `.claude/state/current-wave.json` via `/wave-dispatch` (which calls `.claude/scripts/write-gate-status.sh` internals). If this file already exists for the same wave, **idempotency check**: read `.claude/state/active-worktrees.json` — re-use existing worktrees rather than creating duplicates.

## Step 2 — Create per-stream worktrees

Invoke `[[superpowers:using-git-worktrees]]` first to establish isolation pattern. Then, for each stream:

```bash
git worktree add .claude/worktrees/agent-<stream-id> -b worktree-agent-<stream-id>
```

Or use Agent tool with `isolation: "worktree"` — this auto-creates the worktree.

Check `.claude/state/active-worktrees.json` for existing entries before creating. Status field: `pending | running | done | merged`.

## Step 3 — Dispatch prompt template

Adapt from `docs/quantum-lead-prompt.md` Step 5 — never omit sections; mark unused as "N/A — none for this sub-stream":

```
# Sub-stream <ID>: <one-line goal>

## Quantum context
You are an engineer subagent on <Q? - Name>. The quantum lead has been
directed by the project lead to <one-sentence directive context>.

## Concrete goal
<2-4 sentences. What changes in the repo. What "done" looks like.>

## Files you OWN (may edit)
- <explicit path>

## Files you must NOT touch (owned by parallel sub-streams)
- <explicit path>  ← owned by sub-stream <ID>

## Constraints
- <constraint>

## Verification (must pass before you report DONE)
- `<exact command>`  → must be green

## Deliverables
- <list of commits / new files>

## Re-surface triggers (return to me instead of completing)
- <concrete condition>

## Reporting
On completion, return:
- Files changed (list)
- Test results (counts + commands)
- Any deferrals or caveats
- Risk-register implications (if any)
```

**Wave-N>0 preflight clause** (mandatory in every agent prompt for N>1):

```
## Pre-flight (CRITICAL)
Expected main SHA: `<sha>`.
git rev-parse HEAD
git fetch origin
git merge --ff-only main
If HEAD != expected SHA or FF fails → STOP and report back immediately.
```

## Step 3.5 — W7 4-file audit protocol (encounter-port pre-verification)

Baked wave-49/C 2026-05-22 in response to wave-47b/B's V1-DROPPED finding: Q1-lead's plan-time pre-verification missed upstream Monster class, leading to a wasted subagent dispatch on the HauntedShipSolo substrate stub. The W7 4-file protocol below is **mandatory** at both plan-time (Q1 lead) and dispatch-time (engineer subagent) for ANY sub-stream that ports / extends a Q1 encounter substrate.

**Read ALL FOUR files in full** (not just snippets / first N lines):

1. Q1 substrate `engine/headless/src/Sts2Headless.Domain/Content/Monsters/<MonsterName>.cs` (current state — Q1's port).
2. Q1 substrate `engine/headless/src/Sts2Headless.Domain/Content/Encounters/<EncounterName>.cs` (Q1's encounter wrapper).
3. Upstream `~/development/projects/godot/sts2/src/Core/Models/Monsters/<MonsterName>.cs` (target behavior — ground truth per project-lead 2026-05-21).
4. Upstream `~/development/projects/godot/sts2/src/Core/Models/Encounters/<EncounterName>.cs` (upstream encounter wrapper).

**Pre-verification deliverable** (Step 0b in dispatch prompts; bake into every encounter-port plan):
- Monster move pool: Q1 vs upstream (move IDs + damage values + branching state machine).
- Encounter shape: Q1 vs upstream (monster count + per-slot initial-move overrides + RNG-driven vs static).
- Per-slot logic: explicit listing of upstream's `ConditionalBranchState` / `RandomBranchState` keys + Q1's port-equivalent (e.g., `GenerateMonstersWithMoves` per-slot tuples).

**V1 scope-reduction protocol** (when audit reveals substrate divergence):
- Cohort STOPs without committing partial work for diverging encounter.
- Surfaces with full upstream-vs-Q1 diff.
- Q1 lead handles substrate-fix as separate substream OR defers to later wave.

**Common false-positive in plan-time read** (avoid):
- Reading only Q1 substrate + upstream encounter wrapper (2 of 4 files). The upstream MONSTER class often contains the behavioral complexity (move-table, branching, power application) that the encounter wrapper merely instantiates. Wave-47b/B's HauntedShipSolo finding: upstream wrapper is `1 monster, no flags`; upstream MONSTER has 4 moves + RandomBranchState + Weak/Dazed power application — but Q1's port was a 1-move ATTACK stub. 2-of-4 read missed the substrate stub.

**See also:** wave-48 Q1 substrate stub audit (`engine/headless/docs/specs/q1-substrate-stub-audit.md`) for catalog-wide inventory of 25 in-scope monsters classified FULL/PARTIAL/STUB per this protocol.

## Step 4 — Parallel dispatch

Invoke `[[superpowers:dispatching-parallel-agents]]` before issuing parallel Agent calls. Fire all independent sub-streams in a single message with multiple Agent tool calls. Serial dependencies (stream β consumes α output) must be explicit — β waits for α merge before dispatch.

## File-ownership rule (R8)

Per-stream prompt must list:
- **OWNED files** — the only files the agent may touch
- **FORBIDDEN files** — owned by parallel streams (name the stream)

Verify file-by-file before dispatch. If two streams need the same file, serialize them.

## Pre-dispatch checklist

- [ ] Sub-streams partition by file — verified file-by-file
- [ ] Each prompt has explicit verification commands (not "tests should pass")
- [ ] Each prompt lists owned + forbidden files
- [ ] Serial dependencies between streams stated
- [ ] Wave-N>0 preflight clause included in every prompt
- [ ] `.claude/state/active-worktrees.json` checked for idempotency
- [ ] **W7 4-file protocol** invoked at plan-time for encounter-port sub-streams (Step 3.5)
- [ ] V1 scope-reduction protocol referenced in dispatch prompt for encounter-port sub-streams

## Cross-references

- [[merging-a-wave]] — post-dispatch merge dance
- [[verifying-subagent-claims]] — before accepting any stream as DONE
- `docs/quantum-lead-prompt.md` — full dispatch protocol + status-report format
