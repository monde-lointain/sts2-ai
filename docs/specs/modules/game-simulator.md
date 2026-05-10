# Module: Game Simulator (Q1)

> Headless C# Slay the Spire 2 Core. The deterministic game-state machine the rest of the pipeline talks to. Per ADR-002.

## Responsibilities

- Run STS2 logic deterministically from a seed: combat, map traversal, shops, events, rewards, potion use, rest sites, run progression.
- Expose a hook protocol: at every player-decision boundary, return state + legal-action mask, await an action, validate, apply.
- Provide save/restore primitives: `(CombatState, RunState) ↔ binary blob`. Bit-identical round-trip is a hard requirement, enforced by CI per `scaling-strategy.md` §4.1 #4.
- Support branchable rollouts: from any saved state, executing K alternative actions yields K independent continuations (the basis for MCTS).
- Emit replay files: every rollout produces `(seed, action_sequence, optional state checkpoints)` sufficient to reconstruct.
- Run as out-of-tree mod via `Core/Modding` where possible; minimize patches to the upstream tree to keep rebases cheap (per ADR-002 positives).

Out of scope: rendering, animation, audio, UI, networking, multiplayer, anti-cheat — all stripped from the headless build.

## Data Ownership

Q1 alone owns these schemas. No other quantum reads or writes them directly:

- **Versioned binary state schema** for `CombatState` and `RunState`. Schema version bumped on any breaking change. Old saves rejected with explicit version error — never silently coerced.
- **Hook protocol message schema** — request/response wire format between Q1 and Q8. Versioned. Backwards-compatible field additions allowed; removals require a major bump.
- **Replay file format** — `(game_version, seed, action_sequence, checkpoints?, schema_version)`.
- **Game version manifest** — STS2 version, mod version, schema version, build hash. Stamped onto every replay and every state blob.

Token IDs from Q4 are *referenced* in these formats but not owned by Q1. Q1 emits internal IDs that map to Q4 entries; consumers translate at the boundary.

## Communication

- **Sync — IPC (hot path, ADR-005):** hook protocol over shared-memory ring buffer + two semaphores. Per-decision target <500µs. Caller is Q8. Q12 and Q11 use the same protocol at much lower rates.
- **Sync — control RPC (cold path):** `{load_state, save_state, set_seed, step_until_decision, terminate}`. Used by orchestration (Q11, Q12, debugging tools). Plain JSON-over-Unix-socket is fine; not latency-critical.
- **Async — filesystem:** replay files written to a configured directory. Consumed by Q3 (ingest), Q12 (replay against pinned seeds), debugging tools.
- **Async — metrics:** Q1 exposes a Prometheus pull endpoint for Q7. No outbound runtime dependency.

## Coupling

- **Afferent (in):** Q8 (rollout workers — main hot-path consumer); Q11 (curriculum generator — uses save/restore); Q12 (evaluation harness — runs pinned seeds); Q2 (oracle — consumes serialized state).
- **Efferent (out):** none operational. Q4 is referenced for token mapping but not called at runtime.
- **Indirect:** Q7 (pull-based metrics); filesystem (replay sink).

## Phase Expectations

- **Phase 1.** Combat-only entry point: `(seed, character, deck, relics, encounter_id, ascension) → final state`. Map / run-level code paths can be stubbed.
- **Phase 2.** Full run entry point: `(seed, character, ascension) → full run`. Hook at every decision type (card pick, map, shop, event, rest, potion).
- **Phase 3+.** Counterfactual rollout primitive (replay from a saved state under alternative actions). Faithful event simulation with seeded RNG fully under our control.

## Open Risks

- **C# GC pauses on the hot path** (`scaling-strategy.md` §5.2 #4). Mitigation: struct types for state, object pooling, profile early.
- **Latent nondeterminism** via `DateTime.Now`, threading, async/await, multi-threaded shuffles. Mitigation: deterministic clock injected via DI; differential test against unmodified Godot build on every commit.
- **Godot ABI changes** breaking the headless build on STS2 patch. Mitigation: out-of-tree mod; if we are pinned, fall back to running real Godot in headless mode.
