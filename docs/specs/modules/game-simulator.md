---
quantum: Q1
substrate: engine/headless/
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Game Simulator (Q1)

> Headless C# Slay the Spire 2 Core. The deterministic game-state machine the rest of the pipeline talks to. Per ADR-002.

## Responsibilities [MIXED — see bullets]

- Run STS2 logic deterministically from a seed: **combat [SHIPPED]** (16-encounter corpus, 22/22 → 16 post-B.1-final, byte-exact self-consistency per `engine/headless/docs/phase1-gate-report.md`); **map traversal, shops, events, rewards, potion use, rest sites, run progression [PHASE-2]** (M6b Run Domain is stubbed in Phase-1; only minimal `IRunContext` exists, full run loop deferred).
- **`[PHASE-1A SHIPPED → PHASE-1.5 PARTIAL]`** Expose a hook protocol: at every player-decision boundary, return state + legal-action mask, await an action, validate, apply. **Combat-decision messages SHIPPED** (M2 Hook Protocol Adapter operational, IPC p99 = 14.24 µs vs. 500 µs budget — 35× margin). **Per-step probe coverage is 1/16 encounters (CultistsNormal smoke only) [PHASE-1.5]**; the other 15 encounters have stub `OnPlay` / `SubscribeHooks` / power-trigger bodies — behavior fill-in deferred. **Card-pick / map / shop / event / rest / potion message types defined in schema but unused until Phase-2 [PHASE-2]**.
- **`[PHASE-2]`** Hook protocol input extended to carry `macro_context` (HP / MaxHP / gold / per-potion-slot shadow prices, risk tolerance, pressure indicators per ADR-015 + ADR-019 v1.1 surface) — out of scope for Q1 Phase-1A; v1 hook-protocol bump at S15 (run-level message types) per Q1's existing manifest.
- **`[SHIPPED]`** Provide save/restore primitives: `(CombatState, RunState) ↔ binary blob`. Bit-identical round-trip is a hard requirement, enforced by CI per `scaling-strategy.md` §4.1 #4. 65 `BitIdenticalRoundtripTests` pass on every `make ci`. `RunState` payload is Phase-1 stubbed (placeholder fields per state-codec §SchemaVersion history); full `RunState` serialization is **[PHASE-2]**.
- **`[PHASE-3+]`** Support branchable rollouts: from any saved state, executing K alternative actions yields K independent continuations (the basis for MCTS). Per M2 `[Phase 3+]` scope; `clone_state_request` message type not in schema yet.
- **`[SHIPPED]`** Emit replay files: every rollout produces `(seed, action_sequence, manifest)` sufficient to reconstruct. Self-consistency replay PASSES on 292/292 corpus entries. **State checkpoints at room boundaries [PHASE-2]** (per M3 `[Phase 1 scope]` — minimal replay only, no checkpoints; combat-only).
- **`[SHIPPED]`** Run as out-of-tree mod via `Core/Modding` where possible; minimize patches to the upstream tree to keep rebases cheap (per ADR-002 positives). T3 Ledger has **0 entries** to date — all Godot-surface replacements achieved via T2 (DI / stub registry). NOTE: live-Godot per-step probe (Approach A) is blocked by 12 SceneTree-coupled singletons in upstream's `CombatManager.StartCombatInternal` — this is upstream coupling, not a Q1 in-tree patch — and is **[PHASE-1.5]** scope.
- **`[PHASE-2]`** Tag emitted state fields with observability regime (per ADR-016): each field in the M1 binary state schema classified `SOURCE_PERFECT` / `POLICY_VISIBLE` / `BELIEF_SAMPLED`. Q1 emits all fields; consumer-side (Q8/Q9/Q10) filtering enforces no hidden-state leak in deployed inference. Tag manifest co-located with the state schema; see `engine/headless/docs/specs/modules/state-codec.md § Field observability tagging`. Manifest not present in Phase-1A; authoring is a Phase-2 deliverable when Q9 boots (no deployed inference exists yet).

Out of scope: rendering, animation, audio, UI, networking, multiplayer, anti-cheat — all stripped from the headless build.

## Data Ownership [MIXED — see bullets]

Q1 alone owns these schemas. No other quantum reads or writes them directly:

- **`[SHIPPED]`** Versioned binary state schema for `CombatState` (M1 schema v3 operational); `RunState` placeholder fields shipped; **full `RunState` payload [PHASE-2]**. Schema version bumped on any breaking change. Old saves rejected with explicit version error — never silently coerced (Phase-3+ migrators deferred).
- **`[SHIPPED]`** Hook protocol message schema — request/response wire format between Q1 and Q8. Versioned. Backwards-compatible field additions allowed; removals require a major bump. **Combat-decision message types operational; run-level types defined-but-unused until [PHASE-2]**.
- **`[SHIPPED]`** Replay file format — `(game_version, seed, action_sequence, manifest)`. **Optional `checkpoints` field [PHASE-2]** (M3 Phase-1 ships seed + actions + manifest only).
- **`[SHIPPED]`** Game version manifest — STS2 version, mod version, schema version, build hash. Stamped onto every replay and every state blob (5-tuple invariants enforced).

Token IDs from Q4 are *referenced* in these formats but not owned by Q1. Q1 emits internal IDs that map to Q4 entries; consumers translate at the boundary.

## Communication [MIXED — see bullets]

- **`[SHIPPED]`** **Sync — IPC (hot path, ADR-005):** hook protocol over shared-memory ring buffer + two semaphores. Per-decision target <500µs (measured p99 = 14.24 µs, ~35× margin). Caller is Q8. **Q12 and Q11 consumption [PHASE-2]** (Q12 evaluation harness and Q11 curriculum-generator integration follows when those quanta boot).
- **`[SHIPPED]`** **Sync — control RPC (cold path):** `{load_state, save_state, set_seed, step_until_decision, terminate}` (M4 Control Plane operational). Used by orchestration (Q11, Q12, debugging tools). Plain JSON-over-Unix-socket; not latency-critical.
- **`[SHIPPED]`** **Async — filesystem:** replay files written to a configured directory (M3 disk flush operational). **Q3 ingest and Q12 pinned-seed replay consumption [PHASE-2]** — consumers come online when those quanta boot; Q1 emit side is shipped.
- **`[SHIPPED]`** **Async — metrics:** Q1 exposes a Prometheus pull endpoint for Q7 (M9 Process Host `/metrics` endpoint operational). **Q7 scrape integration [PHASE-2]** (Q7 TSDB not yet booted; endpoint is live and self-tested).

## Coupling [MIXED — see bullets]

- **Afferent (in):** **Q8 (rollout workers — main hot-path consumer) [PHASE-2]** (Q8 not yet booted; M2 server side is shipped and self-tested); **Q11 (curriculum generator — uses save/restore) [PHASE-2+]** (Q11 substrate TBD); **Q12 (evaluation harness — runs pinned seeds) [PHASE-2]**; **Q2 (oracle — consumes serialized state) [ASPIRATION (parked per gate report)]** (Q2 oracle adapter parked per gate report; not on Phase-1 critical path).
- **Efferent (out):** none operational. **`[SHIPPED]`** Q4 is referenced for token mapping but not called at runtime (Q4 Phase-1 manifest fixture is registered: 96 cards / 58 relics / 45 powers / 32 monsters / 21 potions / 22 encounters).
- **`[SHIPPED]`** **Indirect:** Q7 (pull-based metrics — endpoint live, scrape pending Q7 boot); filesystem (replay sink — operational).

## Phase Expectations [MIXED — see bullets]

- **`[SHIPPED — Phase-1A; Phase-1.5 OPEN]`** **Phase 1.** Combat-only entry point: `(seed, character, deck, relics, encounter_id, ascension) → final state`. Map / run-level code paths stubbed (M6b minimal `IRunContext` only). **Phase-1A ratified 2026-05-12** (16-encounter initial-state Godot parity + smoke per-step Godot parity + 16-encounter self-consistency). **Phase-1.5 OPEN**: 15-encounter per-step behavior fill-in (87 cards + 53 relics + 40 powers + 30 monsters need `OnPlay`/`SubscribeHooks`/intent-rotation wiring), live-Godot per-step across full corpus (Approach A blockers — 12 SceneTree singletons), B.1-ε encounter-RNG plumbing (SmallSlimes/MediumSlimes), X-cost / calculated-damage evaluator for Malaise/Skewer/Finisher/Murder/Mirage/KnifeTrap, Lagavulin sleep/idol + FungalBoss spore-cloud/regrow multi-state intent rotations.
- **`[PHASE-2]`** **Phase 2.** Full run entry point: `(seed, character, ascension) → full run`. Hook at every decision type (card pick, map, shop, event, rest, potion). Full M6b run-loop, run-level hook protocol message types active, observability tag manifest authored, room-boundary replay checkpoints, mechanics-notes backlog landing (8 systems: treasure/merchant/rest/events/forge/ancient/orbs/mp-vote).
- **`[PHASE-3+]`** **Phase 3+.** Counterfactual rollout primitive (replay from a saved state under alternative actions). Faithful event simulation with seeded RNG fully under our control. `clone_state_request` message type to be added to M2 schema. Schema migrators between versions become part of the patch-adaptation workflow.

## Open Risks

- **C# GC pauses on the hot path** (`scaling-strategy.md` §5.2 #4). Mitigation: struct types for state, object pooling, profile early.
- **Latent nondeterminism** via `DateTime.Now`, threading, async/await, multi-threaded shuffles. Mitigation: deterministic clock injected via DI; differential test against unmodified Godot build on every commit.
- **Godot ABI changes** breaking the headless build on STS2 patch. Mitigation: out-of-tree mod; if we are pinned, fall back to running real Godot in headless mode.
