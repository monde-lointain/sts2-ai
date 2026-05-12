# Module: Run Domain (M6b)

> The run state machine. Wraps combat (M6a): a combat is a Room. Manages acts, the map DAG, room sequencing, encounter selection, rewards, run-scope state (gold, potions, persistent relics, deck composition, floor). Lifted from upstream `~/development/projects/godot/sts2/src/Core/{Runs, Rooms, Map, Rewards, Events, CardSelection, Timeline}`.

## Responsibilities

- Manage run lifecycle: run start (character selection, ascension, starting deck/relics), act progression, run end (death, ascension complete).
- Manage map DAG: act-1/2/3 maps with rooms and connections; track visited coordinates and current position; legal next-room enumeration.
- Sequence rooms by type: combat, elite, boss, event, shop, rest, treasure. Each room's enter / resolve / exit lifecycle.
- Select encounters: per-act monster pools, normal/elite/boss tables, weighting and de-duplication rules.
- Generate rewards: card rewards (3-pick + skip), gold, relics, potions. Drop tables, rarity weights, character-specific filters.
- Track run-scope state: gold balance, potion slots, persistent relics, deck composition (between combats), HP/max-HP across rooms, current floor, defeated-bosses-this-run.
- Resolve events and shops: event-branch selection (delegated to M6c content behaviors); shop inventory generation; purchase flows.
- Resolve rest sites: rest/smith/dig/lift/toke; cost/benefit application.
- Surface legal-action enumeration for run-level decisions to M6d.
- Surface run-end conditions (HP ≤ 0 mid-room, ascension-final-boss-defeated).

`[Phase 1 scope]` — **stubbed**. M6b exposes a minimal `IRunContext` for M6a but does not run the full run loop. Combat-only entry point bypasses M6b's lifecycle.

`[Phase 2]` — full lifecycle, full hook surface for run-level decisions (card pick, map fork, shop, event, rest, potion).

`[Phase 3+]` — counterfactual rollout: clone run state cheaply for MCTS at run-level.

Out of scope: combat mechanics (M6a); per-content behaviors (M6c); action sequencing (M6d); map-tile rendering (never — Q1 is headless).

## Data Ownership

In-memory only. M6b does not own any external schema. Owned C# types:

- **`RunState`** — root run aggregate. Contains `Players` (list, single-element for single-player Q1 per Q1-ADR-009), `CurrentActIndex`, `Acts`, `VisitedMapCoords`, `CurrentMapPoint`, `CurrentRoom`, `NextRoomId`, `Floor`, `Gold`, `PotionSlots`, `Relics`, `Deck`, `Ascension`, `CharacterId`.
- **`Act`** — per-act state: `ActMap`, `Boss`, `Elites` (encountered list), `EncounteredEvents`.
- **`ActMap`** — DAG of `MapPoint` with connections. Generated deterministically from run seed + act index.
- **`MapPoint`** — `MapCoord`, `RoomType`, generated `EncounterId` / `EventId` / `ShopId`.
- **`AbstractRoom`** subtypes: `CombatRoom`, `EliteRoom`, `BossRoom`, `EventRoom`, `ShopRoom`, `RestSiteRoom`, `TreasureRoom`. Each owns its own resolve flow.
- **`Reward`** — typed reward bag (cards, gold, relic, potion); generated when a combat completes.
- **`PlayerRunState`** — per-player run-scope state (HP, max-HP, deck, relics, potions, gold). Single-element list in Q1.

No data is persistent. Round-trip via M1.

## Communication

### Synchronous (in-process calls)

- **Inbound:** `IRunContext` queries from M6c (relics, events) for run-scope state read/write.
- **Inbound:** combat-result reporting from M6a (HP delta, monsters defeated, combat-end reason).
- **Outbound:** initiates combat — constructs `CombatState` and hands off to M6a; awaits result.
- **Outbound:** reads card/relic/encounter pools from M7 Content Catalog.

### Asynchronous

- None. Run progression is synchronous within the action-queue serial loop.

### Events emitted

- M6b emits run-level hook firings via M6d (e.g., `Hook.OnRoomEnter`, `Hook.OnRewardOpen`, `Hook.OnRelicGained`). These trigger M6c content behaviors registered as hook handlers (relic effects, event branches).

## Coupling

- **Afferent (in):** M9 Process Host (constructs `RunState` at run start); M2 Hook Protocol (run-level decisions on `[Phase 2]+`); M4 Control Plane (load/save run state).
- **Efferent (out):** M6a Combat Domain (initiates combats); M6c Content Behaviors (relic/event/reward callbacks); M6d Action Queue (enqueue actions, trigger hooks); M5 Determinism Kernel (RNG for map gen, encounter selection, reward gen, event randomness); M7 Content Catalog (encounter / event / relic / card pool lookup).
- **Indirect:** M1 State Codec (serializes `RunState`); M3 Replay Recorder (records run-level actions).

Aim: zero dependencies on M2/M3/M8. M6b is a domain module called *by* the adapters.

## Testing Strategy

### Unit Tests

Mock M6a, M6c, M6d, M5, M7. Focus on run-level state transitions and business rules:

- **Map generation determinism:** same `(seed, act_index, character)` produces bit-identical `ActMap`. Verify edge connectivity rules (no orphan nodes; act-end reachability).
- **Encounter pool selection:** weighted sampling from per-act tables; first-elite-rules (no elite repeat in act); boss assignment per ascension table.
- **Reward generation:** card-reward 3-pick respects character/rarity weights; skip-card-reward is legal; gold rewards within ranges; relic-reward drop tables per ascension.
- **Gold accumulation:** combat → gold reward → run-state gold; shop purchase → gold deduction; gain-on-event → gold; cannot go negative.
- **Potion slots:** acquire potion → fill empty slot; full slots reject potion; use potion → slot empties; potion-use triggers M6c behavior callback.
- **Relic activation rules:** relic added → registered with M6d hook system; relic removed → unregistered; per-relic activation conditions (combat-only, run-only, on-take-damage, etc.).
- **Deck composition:** add card → deck list; remove card → deck list; transform card → deck list; upgrade card → in-place mutation; deck pile is single source of truth (no copy in combat).
- **Run-end detection:** HP-0 mid-combat-room → run end (defeat); ascension-final-boss-defeated → run end (victory); abandon-run RPC → run end (abandoned).
- **Map traversal rules:** legal-next-room enumeration respects DAG connectivity; cannot revisit; cannot teleport.

`[Phase 1 scope]` — only `RunState` initialization tests; full lifecycle tests deferred to `[Phase 2]`.

### Integration Tests

Verify M6b's quantum boundaries:

- **Persistence boundary:** roundtrip `RunState` through M1 and verify bit-equality plus behavioral equivalence (resume run from saved state, complete to act-1-boss, compare to non-resumed run on same seed).
- **Combat handoff boundary:** initiate combat through M6b; M6b receives result; run-state HP/relic/deck mutations match expected.
- **Content-pool boundary:** for a fixed seed, the encounter / event / shop / reward pools selected match a recorded golden manifest.
- **Action-queue boundary:** drive M6b through a fixed run-level action sequence; per-step run-state hash matches pin.
- **Differential vs Godot:** determinism probe (Q1-ADR-007) runs full runs against unmodified Godot `RunManager`; per-room state hashes match.

`[Phase 2]` — first end-to-end run integration test (Silent character, ascension 0, full act-1).
`[Phase 3+]` — counterfactual rollout integration: from a saved run state, K alternative actions yield K independent run continuations with no shared mutable state (validate via cross-clone state hash divergence after first divergence point).
