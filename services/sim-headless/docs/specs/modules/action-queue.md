# Module: Action Queue & Hooks (M6d)

> The serial event-loop heart of Q1. Async action orchestration, hook-callback registry, action validation, deterministic effect ordering. Lifted from upstream `~/development/projects/godot/sts2/src/Core/{GameActions, Commands, Hooks}`.

## Responsibilities

- Maintain an action queue: enqueue actions from M6a (combat-driven), M6b (run-driven), M6c (content-driven). Drain in deterministic order.
- Resolve actions: each action mutates state via `ICombatContext` / `IRunContext` and may enqueue follow-up actions (e.g., Strike's damage action enqueues an on-take-damage hook firing).
- Maintain the hook registry: ~150 hook types covering combat lifecycle, card play, damage events, turn boundaries, run-level transitions. M6c content registers handlers; M6a / M6b / M6c trigger.
- Enforce deterministic effect ordering per Q1-ADR-006: explicit priority field first; tie-broken by registration order; registration order itself deterministic by `(owner-creature-id, owner-content-id, content-source-position)`.
- Provide legal-action enumeration: from current `CombatState` / `RunState`, compute the set of legal player actions (legal cards × legal targets, legal map-room choices, legal event branches, etc.). Used by M2 for hook-protocol-mask emission.
- Provide action validation: when M2 / M4 receives an action from outside, verify it is in the legal set before applying. Reject illegal actions with explicit error.
- Suspend execution at player-decision boundaries: when the queue is drained and the next thing required is a player action, M6d yields to M2 / M4 for the action.

`[Phase 1 scope]` — combat actions and combat-scope hooks (~50 of the ~150 hook types). Run-level actions stubbed.

`[Phase 2]` — full hook surface, run-level actions (card-pick, map, shop, event, rest, potion-use).

`[Phase 3+]` — counterfactual rollout: queue snapshot/restore, action replay from saved state.

Out of scope: state structures (M6a / M6b); per-content code (M6c); IPC framing (M2); RPC framing (M4); RNG primitives (M5).

## Data Ownership

In-memory only. M6d does not own any external schema. Owned C# types:

- **`ActionQueue`** — FIFO with priority overlay. Holds queued `IAction` instances.
- **`IAction`** — abstract action with `Resolve(IExecutionContext)` method. Concrete subclasses cover damage application, block gain, draw card, end turn, reward open, room enter, etc.
- **`HookRegistry`** — multimap of `HookType` → ordered list of `HookHandler`. Owner identity and source position recorded per handler for tie-breaking.
- **`HookHandler`** — `(HookType, Priority, Callback, OwnerCreatureId, OwnerContentId, SourcePosition)`.
- **`ExecutionContext`** — injected into action `Resolve`; bundles `ICombatContext` / `IRunContext` / `IRngSource` references.

`HookType` is an enum (~150 values). Stable IDs; new hook types appended; deprecation requires a state-schema bump (Q1-ADR-005) because replays reference hook firings.

No data is persistent. Action queue and hook registrations roundtrip through M1.

## Communication

### Synchronous (in-process calls)

- **Inbound:** action enqueueing from M6a / M6b / M6c.
- **Inbound:** hook registration / deregistration from M6c.
- **Inbound:** "drain to next decision" call from M2 (hot path) or M4 (cold path).
- **Inbound:** "validate this action" call from M2 / M4 before applying an external action.
- **Outbound:** invocations of registered hook handlers (M6c primarily; rarely M6a / M6b directly).
- **Outbound:** mutations applied via `ICombatContext` / `IRunContext` (defined by M6a / M6b, called by M6d during action resolution).
- **Outbound:** RNG calls via M5 for tie-breaking and for actions with stochastic resolution.

### Asynchronous

- None. M6d is the *only* place "async" appears in Q1's domain code, and it is async only in the cooperative sense: yielding back to the IPC/RPC loop at decision boundaries. Per Q1-ADR-008, no `Task.Run`, no thread-pool work.

### Events emitted

- M6d emits hook firings to all registered M6c handlers in deterministic order. From an external-quantum perspective, M6d is a hook *broker*, not an event source.

## Coupling

- **Afferent (in):** M6a Combat Domain (enqueues damage/block/draw/etc. actions); M6b Run Domain (enqueues room-enter / reward-open / map-traverse actions); M6c Content Behaviors (registers/deregisters hooks; enqueues content-driven follow-up actions); M2 Hook Protocol (drain-to-decision, validate-action calls); M4 Control Plane (drain-to-decision via control RPC).
- **Efferent (out):** M5 Determinism Kernel (RNG for tie-breaking, stochastic actions); M6c Content Behaviors (hook callbacks invoked); `ICombatContext` / `IRunContext` (state mutations).
- **Indirect:** M1 State Codec (serializes queue + registrations); M3 Replay Recorder (records action sequence).

Aim: M6d does not import M2 / M3 / M4 / M8. It is a domain module called by adapters.

## Testing Strategy

### Unit Tests

Mock M5, M6c handlers, `ICombatContext`, `IRunContext`. Focus on queue ordering and hook firing rules:

- **FIFO + priority queue:** enqueue 5 actions with mixed priorities; drain; verify drain order is priority-then-enqueue-order.
- **Nested action enqueueing:** an action's `Resolve` enqueues 2 follow-up actions; verify follow-ups drain after current action's resolution completes (not before).
- **Hook firing order:** register 3 handlers with priorities 10, 5, 5; trigger; verify firing order is priority-10, then handlers-priority-5 in registration order.
- **Tie-breaking on registration order:** register 3 handlers all with priority 5 from owners (creature-A, creature-B, creature-A); verify firing order matches `(owner-creature-id, owner-content-id, content-source-position)` rule.
- **Hook deregistration:** register handler; deregister; trigger; verify handler does not fire.
- **Action validation — legal set:** compute legal-action set from a mocked combat state; verify it includes only valid card-plays (cost ≤ energy, target valid) and turn-end.
- **Action validation — rejection:** submit an action with a card not in hand; verify validation rejects with explicit `IllegalAction.NotInHand`.
- **Drain-to-decision:** seed queue with non-decision actions; call drain; verify queue empties and yield occurs at first decision boundary.
- **Empty drain:** call drain on empty queue at decision boundary; verify zero work, immediate yield.
- **Hook-firing-during-drain:** an action's resolution triggers a hook that registers another hook; verify new registration takes effect for *future* triggers, not the in-progress one.

### Integration Tests

Verify M6d's quantum boundaries:

- **End-to-end combat through queue:** drive a full combat by feeding actions into M6d; verify final state matches a recorded golden trace.
- **Hook-ordering pin tests:** for ~10 known multi-hook scenarios (Strike with Pen Nib + Strength + Vulnerable + Curl-Up + Thorns), verify final state matches Q1-ADR-006-pinned expected state.
- **Persistence roundtrip:** after enqueueing actions and registering hooks, roundtrip through M1; resume; verify action drain produces identical post-state.
- **Replay roundtrip:** record action sequence via M3; replay against fresh state; verify identical post-state.
- **Differential vs Godot:** determinism probe (Q1-ADR-007) drives identical action sequences through M6d and unmodified Godot's `GameActions` queue; per-step state hashes match.
- **Latency budget:** drain-to-decision under realistic combat load completes in <100µs (leaving budget for M2 IPC framing). Tracked via Prometheus histogram.
