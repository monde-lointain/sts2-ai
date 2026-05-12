# Module: Content Behaviors (M6c)

> Polymorphic per-content code: card `OnPlay`, relic `OnHook`, power `OnAttack` / `OnTakeDamage`, monster intent state machines, event-branch resolution, card upgrade/transform logic. Inherited from upstream `~/development/projects/godot/sts2/src/Core/Models/{Cards, Relics, Powers, Monsters, Acts}` — ~1640+ class definitions.

## Responsibilities

- Implement card behaviors: `CardModel.OnPlay(ICombatContext, target)` for every card; upgrade rules; cost computation (snecko, Mummified Hand interactions); play-validation conditions.
- Implement relic behaviors: `RelicModel.OnHook(...)` for every hook the relic listens to; relic state transitions (e.g., counters, broken state).
- Implement power behaviors: `PowerModel.OnAttack` / `OnTakeDamage` / `OnEndOfTurn` etc. for every power; stack management rules; expiration rules.
- Implement monster intent state machines: `MonsterModel.NextIntent(ICombatContext)` for every monster; move-rotation rules; opener / phase-transition logic.
- Implement event behaviors: `EventModel.Resolve(IRunContext, branch)` for every event; outcome resolution; reward application.
- Implement act-specific logic: `ActModel` subclasses for per-act rules (boss selection, encounter weighting overrides).
- Register hook handlers with M6d for content that reacts to game events (relics, powers).
- Provide content metadata used by M7 (Content Catalog) at registry-build time: tags, rarity, character, keyword set, target-type.

`[Phase 1 scope]` — implement only the content needed for the Phase-1 reference encounter (Silent + Ring of the Snake vs CULTISTS_NORMAL): ~10-20 cards, ~5 relics, ~5 powers, 2 monsters, 0 events.

`[Phase 2]` — full content for at least one character × first-act run.

`[Phase 3+]` — full content for all characters × all acts × ascension variants.

Out of scope: combat / run state structure (M6a / M6b — M6c reads/writes through `ICombatContext` / `IRunContext`); action sequencing (M6d); content lookup (M7); RNG primitives (M5).

## Data Ownership

In-memory only. M6c does not own any external schema. Owned C# types are class hierarchies:

- **`CardModel`** abstract + ~1000+ concrete subclasses (`StrikeIronclad`, `Defend`, `Impervious`, etc.). Each subclass owns its `OnPlay` body, upgrade override, cost rules, target rules, keyword set.
- **`RelicModel`** abstract + ~200+ concrete subclasses. Each owns hook-registration list and hook-callback bodies.
- **`PowerModel`** abstract + ~200+ concrete subclasses. Each owns stack-type rules and per-trigger callback bodies.
- **`MonsterModel`** abstract + ~50+ concrete subclasses. Each owns intent state machine, HP table, drop table.
- **`EventModel`** abstract + concrete subclasses per event. Each owns branch list and per-branch resolution.
- **`ActModel`** abstract + 3 concrete (Act1, Act2, Act3). Each owns encounter pool, boss table, event pool.

Per-content state (counters, "broken" flags, internal state machines) is stored on instances of these classes, *not* in `RunState` / `CombatState` directly. M1 serializes the per-content state via a class-id-discriminated polymorphic codec (see Q1-ADR-003 pattern, applied here too).

## Communication

### Synchronous (in-process calls)

- **Inbound:** hook firings from M6d (the action queue triggers registered hook handlers in priority order).
- **Inbound:** direct `OnPlay` invocations from M6d when a play-card action resolves.
- **Inbound:** intent-decision calls from M6a (monster turn) — `NextIntent(ICombatContext)`.
- **Outbound:** mutations to combat / run state via `ICombatContext` / `IRunContext` interfaces (defined by M6a / M6b).
- **Outbound:** action enqueueing via M6d (e.g., a card's `OnPlay` enqueues damage / block / draw actions rather than mutating directly).
- **Outbound:** RNG calls via M5's per-subsystem RNG references (e.g., card-reward RNG, monster-move RNG).

### Asynchronous

- None.

### Events emitted

- M6c is the *primary registrant* of M6d's hook system. It does not emit events itself; it reacts to events emitted by M6d on behalf of M6a / M6b.

## Coupling

- **Afferent (in):** M6a Combat Domain (calls intent decisions, queries content during play); M6b Run Domain (calls relic / event resolution); M6d Action Queue (fires registered hooks, resolves card OnPlay); M7 Content Catalog (reads class metadata at registry-build time).
- **Efferent (out):** M6d Action Queue (enqueue actions, register hooks); M5 Determinism Kernel (RNG); `ICombatContext` / `IRunContext` interfaces (defined by M6a / M6b but used opaquely here).
- **Indirect:** M1 State Codec (serializes per-content state via polymorphic codec).

Aim: M6c does not import M6a or M6b directly — only their context interfaces. This keeps M6c testable in isolation and prevents content code from reaching into combat/run state structures.

## Testing Strategy

### Unit Tests

Mock M6d (action queue), M5 (RNG), `ICombatContext`, `IRunContext`. Focus on per-content business rules. Each subclass gets its own test class.

- **Per-card OnPlay:** mock combat context; invoke `OnPlay`; verify expected actions enqueued (e.g., Strike → enqueue `DealDamage(6, target)`); verify upgrade variant differs as expected (Strike+ → 9 damage); verify illegal target rejection.
- **Per-relic OnHook:** mock combat/run context; fire the hook the relic listens to; verify state mutations and counter advancement; verify "broken" state transitions (e.g., Snecko Eye uses-up flag).
- **Per-power OnTrigger:** mock context; fire trigger; verify stack-decrement / refresh / additive rules; verify expiration on threshold.
- **Per-monster NextIntent:** mock combat context with rigged RNG; invoke `NextIntent`; verify intent matches move-rotation rule; verify opener fires on round 1 only; verify phase-transition triggers on HP threshold.
- **Per-event branch resolution:** mock run context; invoke `Resolve` with each branch index; verify state mutations (gold, HP, deck changes, relic gain).
- **Snecko-cost interactions:** card cost depends on snecko-applied state; verify cost computed correctly for all combinations.
- **Mummified-Hand-style hooks:** play-card hook fires for the *next* card-play; verify it consumes the proc, does not double-fire.
- **Hook registration:** `OnEnter` registers correct set of hooks; `OnExit` unregisters; no leaked registrations across combats.

These tests are *generated* from the `CardModel` / `RelicModel` / etc. class hierarchy where possible — a fixture-discovery framework that asserts every concrete subclass has at least one OnPlay/OnHook test.

### Integration Tests

Verify M6c's quantum boundaries — its interaction with the rest of Q1:

- **Card × monster × power interaction:** play a card with a damaging effect against a monster with Vulnerable; verify damage = base × 1.5; verify the order of triggered effects (Strength → on-attack-relic-bonus → Vulnerable-multiplier → on-take-damage → Curl-Up-on-take-damage) matches Q1-ADR-006's pinned ordering.
- **Multiple hook registrants:** register 3 powers and 2 relics that all listen to `OnTakeDamage`; trigger; verify all five fire in deterministic priority order.
- **Hook deregistration on combat end:** register hooks; end combat; start new combat; verify zero leaked registrations from the previous combat.
- **Content-state roundtrip:** apply a card's OnPlay that mutates per-content state (counter); roundtrip `CombatState` through M1; verify counter survives.
- **Differential vs Godot:** determinism probe (Q1-ADR-007) plays a fixed card sequence in both Q1 and unmodified Godot; per-action state hashes match.
- **Content-coverage gate:** CI assert that every concrete subclass under `Models/Cards/`, `Models/Relics/`, etc. has a registered behavior in M7's Content Catalog. Missing implementations fail the build.
