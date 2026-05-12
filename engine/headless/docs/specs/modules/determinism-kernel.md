# Module: Determinism Kernel (M5)

> Seeded RNG pool, deterministic clock, ordering primitives. The cross-cutting source of every deterministic value in Q1. Lifted from upstream `~/development/projects/godot/sts2/src/Core/Random/{Rng.cs, RunRngSet.cs, PlayerRngSet.cs}`.

## Responsibilities

- Provide seeded RNG instances per subsystem: monster-move RNG, card-shuffle RNG, card-reward RNG, treasure RNG, event-resolution RNG, map-generation RNG, plus per-player decision RNG. Each RNG is independent (separate counter), preserving the upstream `RunRngSet` / `PlayerRngSet` pattern.
- Provide RNG primitives: `NextInt(min, max)`, `NextBool()`, `NextFloat()`, `Shuffle<T>(IList<T>)`, `Choose<T>(IReadOnlyList<T>, weights)`. Counter-monotonic; counter advances with every call.
- Provide a deterministic clock (`IClock`): for any in-game elapsed-time computation, return a value derived from action-count, not wall clock. Production wall clock is **not** accessible to domain code.
- Provide ordering primitives used by M6d for tie-breaking — a stable, reproducible "shuffle" of equal-priority items.
- Expose RNG state for serialization to M1 via `IRngStateSerializer` (per Q1-ADR-003): each RNG returns a versioned byte blob; M1 treats it as opaque.
- Validate determinism contracts: counter cannot rewind, seed cannot be re-set without explicit reseeding RPC (M4), no two RNGs share a counter address.

`[Phase 1 scope]` — full M5 functional. Combat-only run requires monster-move RNG, card-shuffle RNG, player-action RNG. Other RNG subsystems instantiated but unused.

`[Phase 2]` — all RNG subsystems active.

`[Phase 3+]` — RNG state branching for counterfactual rollout: from a saved state, multiple RNG-state clones permit independent rollouts without aliasing.

Out of scope: nondeterministic system facilities (`DateTime.Now`, network jitter, file-modification timestamps). Domain code is forbidden from calling these by namespace-banned-API analyzer (per Q1-ADR-001).

## Data Ownership

M5 owns the RNG state schema — a versioned byte format, opaque to M1 per Q1-ADR-003.

- **`RngState`** — per-subsystem state: `Seed` (uint64), `Counter` (uint64), `SubsystemId` (`RunRngType` enum value).
- **`RngBundle`** — collection of all `RngState` instances for a process: run-scope RNGs + per-player RNGs. Roundtripped as one section of M1's binary state blob.
- **RNG schema version** — independent of state schema version; bumped when adding a new `RunRngType` value or changing a primitive's behavior.

`IClock` does not own state — it is a pure function of the action-count carried in `CombatState` / `RunState`.

## Communication

### Synchronous (in-process calls)

- **Inbound:** RNG calls from any module (M6a / M6b / M6c / M6d / M7 init); each caller specifies which subsystem RNG to use.
- **Inbound:** clock-read calls from any module that needs in-game-time computation.
- **Inbound:** `Serialize() / Deserialize(bytes)` from M1.
- **Inbound:** `Reseed(seed)` from M4 (control plane) and M9 (process init).
- **Outbound:** none. M5 is a leaf module; it depends on nothing.

### Asynchronous

- None.

### Events emitted

- None.

## Coupling

- **Afferent (in):** M6a, M6b, M6c, M6d (RNG and clock consumers); M7 (RNG for randomized content init); M1 (serialization); M4 (reseed RPC); M9 (initial seed at process boot).
- **Efferent (out):** **none**. M5 is an infrastructure leaf.

Aim: zero outbound dependencies. M5 is the foundation; everything else stands on it.

## Testing Strategy

### Unit Tests

Mock nothing — M5 has no dependencies. Focus on determinism guarantees and primitive correctness.

- **Seed reproducibility:** same seed + same call sequence → same value sequence; verify across `NextInt`, `NextBool`, `NextFloat`, `Shuffle`, `Choose`.
- **Counter monotonicity:** every primitive call advances the counter exactly as documented (one increment per scalar primitive; N increments per `Shuffle` of N items).
- **Counter cannot rewind:** attempting to set a counter lower than its current value throws.
- **Per-subsystem isolation:** advancing the monster-move RNG counter does not affect the card-shuffle RNG counter.
- **Reseed semantics:** after `Reseed(seed)`, the RNG behaves as if freshly constructed with `seed`. Counter resets to 0.
- **Shuffle determinism:** `Shuffle(list)` produces the same permutation for the same `(seed, counter, list-content)`. Tested across list sizes 0, 1, 2, 10, 100.
- **Choose with weights:** weighted sampling matches expected distribution over a high N (statistical, with deterministic seed).
- **Clock determinism:** for the same `CombatState.ActionCount`, `IClock.Now()` returns the same value. No reference to wall clock.
- **Serialization roundtrip:** serialize an `RngBundle`; deserialize; resume RNG calls; verify produced sequence matches a non-roundtripped RNG.
- **Schema version evolution:** old `RngBundle` with version-N missing a new subsystem deserializes successfully; the new subsystem initializes from a documented derived seed.

### Integration Tests

Verify M5's contract surfaces with the rest of Q1:

- **State-codec boundary:** roundtrip a full `RngBundle` through M1; assert byte-identical reserialization on the second pass.
- **Differential RNG vs Godot:** for a fixed seed, M5's `NextInt(0, 100)` sequence matches upstream `Rng.cs`'s `NextInt(0, 100)` sequence for the same seed and counter — exact byte equality.
- **Determinism probe (Q1-ADR-007) integration:** the probe uses M5's canonical state hash to compare Q1 and Godot states. M5 ships `CanonicalHash(CombatState, RunState, RngBundle) → uint64` used by the probe.
- **Reseed RPC roundtrip:** invoke M4's reseed; subsequent rollouts produce a different deterministic sequence keyed off the new seed.
- **No-wall-clock-leak gate:** static-analysis CI rule asserts no `DateTime.Now` / `Stopwatch.GetTimestamp` / `Environment.TickCount` reference exists in domain-namespace assemblies. M5's `IClock` is the only legal time source.
