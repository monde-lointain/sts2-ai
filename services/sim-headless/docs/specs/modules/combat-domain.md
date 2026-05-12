# Module: Combat Domain (M6a)

> The combat state machine. The runtime "what's happening in this fight" — turns, creatures, card piles, intents, in-combat modifiers. Lifted from upstream `~/development/projects/godot/sts2/src/Core/{Combat, Entities/Creatures, Entities/Cards, Entities/Powers, Entities/Potions, Entities/Intents}`.

## Responsibilities

- Manage combat lifecycle: start-of-combat setup, round/turn cycle, end-of-combat teardown.
- Track creatures (player + monsters): HP, block, max-HP modifiers, side, in-combat power stacks.
- Track card piles: draw, hand, discard, exhaust, retain. Hold per-instance card state (upgraded copy, ethereal, snecko'd cost, conjured, temporary buffs).
- Track monster intent state: intent type, telegraphed value, intent state machine (move-rotation rules).
- Track in-combat modifiers: combat-scope flags, room-modifier effects (e.g., Wax-Bane).
- Expose a state surface (`ICombatContext`) consumed by M6c (Content Behaviors) for read/write.
- Surface legal-action enumeration to M6d for hook-protocol-mask emission.
- Surface combat-end conditions (player HP ≤ 0, all monsters dead, special-encounter-defined victory).

`[Phase 1 scope]` — full combat domain functional. This is the load-bearing module for Phase 1's combat-only entry point.

`[Phase 2]` — extensions for run-scope effects bleeding into combat (e.g., relics that activate on combat start).

`[Phase 3+]` — counterfactual rollout: clone combat state cheaply enough to support MCTS. Likely struct-of-arrays refactor for hot state.

Out of scope: card OnPlay code (lives in M6c); action sequencing and hook firing (M6d); RNG (M5); content table lookup (M7); serialization (M1).

## Data Ownership

In-memory only. M6a does not own any external schema. Owned C# types:

- **`CombatState`** — root combat aggregate. Contains `RoundNumber`, `CurrentSide`, `Allies`, `Enemies`, `Modifiers`, reference to `IRunContext`, reference to `Encounter`.
- **`Creature`** — `CurrentHp`, `MaxHp`, `Block`, `Side`, `Powers` (list), `IsPlayer`, optional `Player` / `Monster` ref.
- **`CardPile`** — typed collection (`PileType` enum: Draw, Hand, Discard, Exhaust, Retain) holding `CardInstance` objects.
- **`CardInstance`** — distinct from M7's `CardModel`. Per-instance state: upgrade level, snecko cost, ethereal flag, retained-this-turn flag, conjured-this-combat flag.
- **`PowerInstance`** — per-creature power stack: `PowerModelRef`, `Stacks`, `JustApplied` flag for end-of-turn rules.
- **`MonsterIntent`** — `IntentType`, `TelegraphValue`, `MoveHistory` (for move-rotation rules), next-move RNG-pin reference.
- **`InCombatModifiers`** — flag bag + counter bag for room-modifier and combat-scope effects.

These types are **structs where possible** for hot state (per Q1-ADR rank-2 throughput), classes where reference semantics are required (e.g., creatures referenced by multiple powers).

No data is persistent. Round-trip is via M1.

## Communication

### Synchronous (in-process calls)

- **Inbound:** `ICombatContext` queries (read-only) and mutations (apply damage, gain block, draw card, etc.) called by M6c content behaviors and M6d action handlers.
- **Inbound:** legal-action enumeration query from M6d.
- **Outbound:** none directly to other modules. State transitions are driven by M6d actions firing M6c hooks; M6a is the *substrate*, not the *driver*.

### Asynchronous

- None. Combat is purely synchronous within the action-queue serial loop.

### Events emitted

- M6a does not emit events directly. State changes generate hook firings via M6d (e.g., a `Creature.CurrentHp` mutation goes through `M6d.Trigger(Hook.OnTakeDamage, ...)`). The hook-firing is M6d's responsibility, not M6a's.

## Coupling

- **Afferent (in):** M6b Run Domain (initiates combat with an `Encounter`); M6c Content Behaviors (reads/writes via `ICombatContext`); M6d Action Queue (mutations via context); M9 Process Host (constructs `CombatState` at start).
- **Efferent (out):** M5 Determinism Kernel (RNG for shuffles, randomized state); M7 Content Catalog (resolve `CardModel` / `MonsterModel` lookups); M6d Action Queue (enqueue actions, trigger hooks).
- **Indirect:** M1 State Codec (serializes `CombatState`); M3 Replay Recorder (records action stream that mutates combat).

Aim: zero dependencies on M2/M3/M4/M8/M9. M6a is a pure domain module.

## Testing Strategy

### Unit Tests

Mock all I/O (M5, M6d, M6c, M7) using port interfaces. Focus on state transitions and business rules:

- **Damage application:** unblocked damage reduces HP; block absorbs first; overflow damage applies; HP cannot go negative; HP-0 sets dead flag; thorns reflects.
- **Block lifecycle:** block accrues during turn; block resets at start-of-turn for non-Barricade creatures; Barricade preserves block; negative-block is impossible.
- **Card pile transitions:** play card → hand → discard (default) / exhaust (if Exhaust keyword) / retain pile. Draw from empty draw pile reshuffles discard. Reshuffle preserves card-instance identity (upgrades, snecko cost).
- **Power stacking rules:** Strength stacks additively; Vulnerable refreshes-not-stacks; powers with `JustApplied` skip end-of-turn decrement on the turn applied. Per-power `PowerStackType` enforced.
- **Monster intent state machine:** intent telegraphed at start-of-turn; intent advances per move-rotation rules; unique intents (e.g., Cultist's first-turn ritual) fire once.
- **End-of-turn ordering:** discard hand, monster acts, player buff decrement, draw new hand. Ordering pinned per Q1-ADR-006.
- **Energy management:** start-of-turn energy reset; energy spent per card play; insufficient-energy rejects card play.
- **Combat-end detection:** all monsters dead → victory; player HP-0 → defeat; special-encounter victory conditions.

### Integration Tests

Verify M6a's quantum boundaries — the contract surfaces it offers to other modules:

- **Persistence boundary:** roundtrip `CombatState` through M1 and verify bit-equality of resulting state plus all behavioral equivalence (apply 100 random actions; serialize; deserialize; apply same actions; assert identical).
- **Content boundary:** instantiate a `CombatState` with a known encounter from M7; play a hand of cards from M7; verify HP/block/intent state matches a recorded golden trace.
- **Action-queue boundary:** drive M6a through a fixed action sequence via M6d; verify combat state evolution matches a pinned per-step state hash.
- **Differential vs Godot:** the determinism probe (Q1-ADR-007) runs identical seeds against M6a and unmodified Godot's `CombatManager`; per-step state hashes must match.

Phase 1 covers all of the above for the Phase-1 reference encounter (`Silent + Ring of the Snake vs CULTISTS_NORMAL`, per pipeline `00-system-overview.md` §1). Phase 2 extends test coverage to the full encounter pool.
