# Wave-50 Mock-Layer Feasibility Survey (A.0)

> **Purpose:** Pre-execution feasibility survey for wave-50 Phase-2 mock layer.
> Verifies Godot-singleton binding before A.1.* (mock implementation) dispatch.
> Per R17 prophylactic discipline; output drives A.1 scope and A.2 hook-chain impl.
>
> **Upstream ground truth:** `~/development/projects/godot/sts2/src/`
> **Survey scope (C1):** singleton-touching code paths in `CombatManager.StartTurn`,
> `EndPlayerTurnPhaseOneInternal`, and `ExecuteEnemyTurn` only.
>
> **SHA:** `3a6b33a` (wave-49 close)

---

## §1 LocalContext Usage

### Class structure

`LocalContext` (`Core/Context/LocalContext.cs`) is a **pure static class** — no
instance, no Godot inheritance, no constructor, no `Instance` getter. All methods
are `static`. It holds one mutable static property:

```csharp
public static class LocalContext
{
    public static ulong? NetId { get; set; }
    // ... static methods using NetId
```

### Methods invoked by the turn loop

| Call site | Method | Effect |
|---|---|---|
| `StartTurn` (player-branch, lines 345, 364) | `LocalContext.NetId.HasValue` | Guard check; constructs `HookPlayerChoiceContext` only if non-null |
| `EndPlayerTurnPhaseOneInternal` (lines 882, 907) | `LocalContext.NetId.HasValue` | Same guard pattern |
| `EndPlayerTurnPhaseOneInternal` (line 885) | `LocalContext.NetId.Value` | Passed to `HookPlayerChoiceContext` ctor |
| `AfterAllPlayersReadyToEndTurn` (line 835) | `LocalContext.GetMe(_state)` | Resolves local player for `ReadyToBeginEnemyTurnAction` |
| `Hook.BeforeSideTurnStart` (line 849) | `LocalContext.NetId` | Hook guard; returns early if `!netId.HasValue` |
| `Hook.BeforeTurnEnd` (line 917) | `LocalContext.NetId` | Hook guard; returns early if `!netId.HasValue` |
| `Hook.AfterAutoPostPlayPhaseEntered` (line 668) | `LocalContext.NetId.HasValue` | Hook guard; returns early if false |

**SceneTree dependencies: NONE.** `LocalContext` is a pure-static data holder with
no Godot base class, no node references, and no `GetTree()` calls.

**Mock strategy (C2 reflection-swap): NOT NEEDED — direct value injection suffices.**
`LocalContext.NetId` is a plain `public static` property with `get; set;`. Setting it
directly via reflection (or simply calling `LocalContext.NetId = someValue`) before
invoking the turn loop is all that is required. No class swap needed.

The critical behavior: when `LocalContext.NetId.HasValue == true`, hook chains run
per-player. When `false`, `Hook.BeforeSideTurnStart`, `Hook.BeforeTurnEnd`, and
`Hook.AfterAutoPostPlayPhaseEntered` all return early without iterating hook
listeners — **which means no enemy power effects fire during the turn loop**. For
headless capture, we need hooks to fire; therefore **A.1 must set `LocalContext.NetId`
to a valid `ulong` before invoking the turn loop**.

**Recommendation for A.1.a:** Instead of a `MockLocalContext` class, set
`LocalContext.NetId = 1UL` (or the player's actual NetId from the `RunState`)
via reflection in the `SingletonSwapper` / `TurnLoopBootstrap` setup phase. No mock
class required. Restore `null` after capture in the `finally` block.

---

## §2 RunManager + ActionQueueSynchronizer

### RunManager.Instance

`RunManager` (`Core/Runs/RunManager.cs`) is a **plain C# class** (not a Godot Node):

```csharp
public class RunManager : IRunLobbyListener
{
    public static RunManager Instance { get; } = new RunManager();
    private RunManager() { }
```

The static `Instance` is a `{ get; }` (init-only) property initialized at class-load
time to `new RunManager()`. The private constructor has no parameters and no Godot
calls. `RunManager.Instance` is **always non-null** at reflection time (initialized
at class load, not via Godot autoload). The init-only backing field is
`<Instance>k__BackingField` — writable via reflection if needed.

### Methods invoked by the turn loop

| Call site | Singleton path | Effect headless |
|---|---|---|
| `StartTurn` line 372 | `RunManager.Instance.ChecksumTracker.GenerateChecksum(...)` | Safe: `ChecksumTracker.IsEnabled = false` when NetService is singleplayer; short-circuits |
| `StartTurn` line 409 | `RunManager.Instance.ActionExecutor.Unpause()` | Pure C# object; sets internal flag; safe |
| `StartTurn` line 410 | `RunManager.Instance.ActionQueueSynchronizer.SetCombatState(PlayPhase)` | **NULL CRASH** if AQS not initialized |
| `StartTurn` line 422 | `RunManager.Instance.ChecksumTracker.GenerateChecksum(...)` | Safe (IsEnabled=false) |
| `AfterAllPlayersReadyToEndTurn` line 830 | `RunManager.Instance.ActionQueueSynchronizer.SetCombatState(EndTurnPhaseOne)` | **NULL CRASH** if AQS not initialized |
| `AfterAllPlayersReadyToEndTurn` line 835 | `RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(...)` | Singleplayer: enqueues directly; needs AQS initialized |
| `EndPlayerTurnPhaseOneInternal` line 926 | `RunManager.Instance.ChecksumTracker.GenerateChecksum(...)` | Safe |
| `ExecuteEnemyTurn` line 814 | `RunManager.Instance.ChecksumTracker.GenerateChecksum(...)` | Safe |
| `WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` lines 843–854 | `RunManager.Instance.ActionExecutor.*` | Pure async; safe if initialized |

### ActionQueueSynchronizer: the critical initialization requirement

`RunManager.Instance.ActionQueueSynchronizer` is `null` until `RunManager
.InitializeShared()` is called. **Calling `SetCombatState` on null crashes headless.**

`ActionQueueSynchronizer.SetCombatState(PlayPhase)` calls `_actionQueueSet
.UnpauseAllPlayerQueues()` — pure C# in-memory queue state. No Godot/SceneTree
access inside `SetCombatState`.

`ActionQueueSynchronizer.RequestEnqueue(ReadyToBeginEnemyTurnAction)` branches on
`_netService.Type`. In singleplayer mode: `EnqueueAction` → `_actionQueueSet
.EnqueueWithoutSynchronizing(action)`. No SceneTree access.

### Thread-safety and queue semantics

Queue operations are single-threaded (C# async/await cooperative). No locks needed
for headless single-player capture. `ActionQueueSet.Reset()` and
`ActionQueueSet.CombatStarted()` are called during `SetCombatState` transitions.

### Hook-protocol queue expectations

Between hook fires, no explicit queue pumping is needed. Hooks are `await`-chained
directly. The `ActionExecutor` is only needed for the `ReadyToBeginEnemyTurnAction`
gate between `EndPlayerTurnPhaseOne` and `ExecuteEnemyTurn`. With
`NonInteractiveMode.IsActive == true`, `WaitForUnpause` is a no-op.

### Mock recommendation for RunManager + AQS

**Preferred path: use `RunManager.SetUpTest(runState, gameService, ...)` via
reflection.** The `runState` is already constructed in `CaptureMidCombat()`. The
`gameService` can be `new NetSingleplayerGameService()` (plain C# — no Godot). This
call populates `ActionQueueSynchronizer`, `ActionExecutor`, `ActionQueueSet`, and
`ChecksumTracker` (with `IsEnabled=false` for singleplayer). Call
`RunManager.Instance.CleanUp()` after capture to reset state to null.

`RunManager.SetUpTest` also calls `InitializeRunLobby` (singleplayer = no
`RunLobby`; `CombatStateSynchronizer` is created but `IsDisabled=true`) and
`InitializeNewRun` (populates relic grab bags — pure C# no Godot).

No mock class for `ActionQueueSynchronizer` is needed. No reflection-swap of
`RunManager.Instance` is needed — the existing singleton is initialized correctly,
just unpopulated until `SetUpTest`.

---

## §3 NRunMusicController

### Class structure

`NRunMusicController` (`Core/Nodes/Audio/NRunMusicController.cs`) is a **Godot
Node** (`partial class NRunMusicController : Node`). Its `Instance` getter is:

```csharp
public static NRunMusicController? Instance => NRun.Instance?.RunMusicController;
```

`NRun.Instance` is a Godot scene-tree node (`partial class NRun : Control`). It
returns `null` when running headless (no scene tree). Therefore
**`NRunMusicController.Instance` returns `null` headless — no crash**.

### Methods invoked by the turn loop

| Call site | Invocation | Effect headless |
|---|---|---|
| `StartCombatInternal` line 230 | `NRunMusicController.Instance?.PlayCustomMusic(...)` | No-op (null `?.`) |
| `StartCombatInternal` line 242 | `NRunMusicController.Instance?.UpdateTrack()` | No-op |
| Any `RunManager.EnterRoomInternal` calls | `NRunMusicController.Instance?.UpdateTrack()` / `?.UpdateAmbience()` | Out of C1 turn-loop scope |

All turn-loop `NRunMusicController` calls use `?.` null-safe operator and are no-ops.

Furthermore, every public method in `NRunMusicController` is guarded by
`if (!NonInteractiveMode.IsActive)`. Since `TestMode.IsOn = true` (set by
UpstreamDriver) makes `NonInteractiveMode.IsActive = true`, all method bodies
short-circuit immediately even if `Instance` were non-null.

### Mock requirement: NONE

`NRunMusicController` **does not require a mock class** for headless turn-loop
execution. The `?.` null-safe operator + `NonInteractiveMode.IsActive` guard
handle all call sites transparently. **A.1.c is eliminated.**

---

## §4 Hook Chain → Q1 Side Mapping

### Upstream hook signatures and positions in the turn loop

**`Hook.BeforeSideTurnStart(ICombatState combatState, CombatSide side)`**
- Fired at `StartTurn` line 295, after `creature.BeforeTurnStart(...)` but before
  card draw / energy reset.
- Iterates `combatState.IterateHookListeners()` via `HookPlayerChoiceContext`.
- **Guard:** returns early if `!LocalContext.NetId.HasValue`.
- Fired for **both** `CombatSide.Player` and `CombatSide.Enemy`.
- Enemy powers like `LouseProgenitor.CURL_AND_GROW` fire here on the enemy side.

**`Hook.AfterBlockCleared(ICombatState combatState, Creature creature)`**
- Fired at `StartTurn` lines 338–341, iterating each creature starting the turn.
- **No `LocalContext.NetId` guard** — fires unconditionally.
- Affects both player and enemy creatures.

**`Hook.AfterAutoPostPlayPhaseEntered(HookPlayerChoiceContext, ICombatState, Player)`**
- Fired inside `EndPlayerTurnPhaseOneInternal` lines 882–888, once per ending player.
- Player phase = `AutoPostPlay` immediately before call.
- Fires only for `CombatSide.Player`.
- **Guard:** returns early if `!LocalContext.NetId.HasValue`.
- For current encounter set (no relics), this is effectively a no-op.

**`Hook.BeforeTurnEnd(ICombatState combatState, CombatSide side)`**
- Fired in `EndPlayerTurnPhaseOneInternal` line 898 (player side) and
  `EndEnemyTurnInternal` line 964 (enemy side).
- **Guard:** returns early if `!LocalContext.NetId.HasValue`.

### Q1 emit-point mapping

Q1's `Q1MidCombatCaptureDriver.Capture()` emits at three per-turn checkpoints:

| Q1 emit point | Q1 method boundary | `Side` string |
|---|---|---|
| After `CombatEngine.StartPlayerTurn(ctx)` | post-StartPlayerTurn, pre-card-play | `"player-pre"` |
| After `CombatEngine.EndPlayerTurn(ctx)` | post-EndPlayerTurn | `"player-end"` |
| After `CombatEngine.EnemyTurn(ctx)` + `CheckCombatEnd(ctx)` | post-EnemyTurn | `"enemy-end"` |

### Explicit mapping table

| Upstream hook | Fires during | Q1 capture analog | `Side` string | Notes |
|---|---|---|---|---|
| `Hook.BeforeSideTurnStart` (`CombatSide.Player`) | `StartTurn`, before card draw + energy reset | After `StartPlayerTurn` completes | `"player-pre"` | **Timing gap:** upstream fires pre-draw; Q1 fires post-draw. Enemy `Block` may differ at "player-pre": Q1 clears blocks before snapshot; upstream has blocks still intact. Intent fields unaffected. |
| `Hook.AfterAutoPostPlayPhaseEntered` (player→enemy) | `EndPlayerTurnPhaseOneInternal`, AutoPostPlay phase | After `EndPlayerTurn(ctx)` | `"player-end"` | **Minor gap:** upstream fires pre-discard; Q1 fires post-discard. For no-relic starter deck: behaviorally identical. |
| `Hook.BeforeSideTurnStart` (`CombatSide.Enemy`) | `StartTurn` (enemy branch), before enemy action | **No Q1 equivalent** | (none) | Q1 `EnemyTurn()` is atomic; no "enemy-pre" snapshot. A.2 may add capture point here for future phases; not required for Phase-2 gate. |
| `Hook.BeforeTurnEnd` (enemy side, from `EndEnemyTurnInternal`) | After all enemy attacks complete | After `CombatEngine.EnemyTurn(ctx)` | `"enemy-end"` | Best upstream analog for Q1's post-EnemyTurn snapshot. |

### Hook-chain gap summary

**Gap 1 — "player-pre" timing:** Upstream `BeforeSideTurnStart(Player)` fires before
card draw; Q1 "player-pre" fires after `StartPlayerTurn` completes (post-draw,
post-energy-reset, post-block-clear). For intent/MoveId comparison: gap is
**irrelevant** (intents set by `PrepareForNextTurn` before `BeforeSideTurnStart`
fires). For enemy `Block` at "player-pre": 1-block difference expected (Q1 clears;
upstream hasn't cleared yet). **Risk: LOW** for Phase-2 gate criteria (LouseProgenitor
PounceDamage catches at "enemy-end"; Exoskeleton catches at "player-pre" via MoveId).

**Gap 2 — "player-end" phase:** Q1 "player-end" is post-full-EndPlayerTurn including
discard. Upstream `AfterAutoPostPlayPhaseEntered` is pre-discard. For current
encounter set (no AutoPostPlay effects in starter deck): **behaviorally invisible**.
**Risk: LOW.**

**Gap 3 — No "enemy-pre" capture:** Q1 has no snapshot between player-end and enemy
execution. Not needed for Phase-2 criteria. Can be added in wave-51+ if required.

**A.2 implementation guidance:** Capture at three points to match Q1's three sides:
1. `"player-pre"`: snapshot after `StartTurn` player-branch completes (at
   `ActionExecutor.Unpause` boundary, line 409 — after energy reset + draw + hooks).
2. `"player-end"`: snapshot after `EndPlayerTurnPhaseOneInternal` returns.
3. `"enemy-end"`: snapshot after `ExecuteEnemyTurn` + `EndEnemyTurn` return.

---

## §5 Mock Layer Scope Estimate

### Revised surface area

Based on §1–§3 findings:

| Component | A.1 plan (wave-50 plan doc) | Actual requirement | Delta |
|---|---|---|---|
| `MockLocalContext` class | NEW file ~30 LOC | **Not needed** — set `LocalContext.NetId` directly | −30 LOC |
| `MockActionQueueSynchronizer` class | NEW file ~80 LOC | **Not needed** — use `RunManager.SetUpTest(...)` via reflection | −80 LOC |
| `MockNRunMusicController` class | NEW file ~40 LOC | **Not needed** — null-safe `?.` + `NonInteractiveMode` guard | −40 LOC |
| `SingletonSwapper` helper | NEW file ~100 LOC | `TurnLoopBootstrap.Install/Restore` ~50 LOC (LocalContext.NetId + RunManager.SetUpTest + TestMode gate) | −50 LOC |
| A.2 UpstreamDriver extension | ~200–400 LOC | ~300–450 LOC; direct method invocation per §7 risk 6 | unchanged |

### Hour estimates

| Stream | Project-lead estimate | Revised estimate | Notes |
|---|---|---|---|
| A.1 mock layer | 8–12h | **~2–3h** | No mock classes; only `TurnLoopBootstrap` + `RunManager.SetUpTest` call |
| A.2 UpstreamDriver turn loop | 4–6h | ~4–6h | Unchanged |
| A.3 re-capture + comparer | 3–4h | ~3–4h | Unchanged |
| A.4 red-team | 2–3h | ~2–3h | Unchanged |
| **Total** | **16–24h** | **~11–16h** | Well below 30h surface threshold |

**A.1 drops from 8–12h to ~2–3h.** No re-surface trigger fires.

---

## §6 Mock Layer Design Recommendation

### Mechanism per singleton

**`LocalContext` (static class, no instance, no `Instance` getter):**
- Mechanism: **direct property write** — `LocalContext.NetId = 1UL` (or via
  reflection crossing assembly boundary: `typeof(LocalContext).GetProperty("NetId")
  .SetValue(null, (ulong?)1UL)`).
- Reflection-swap: NOT applicable (no instance to swap).
- **Verdict: TRIVIAL. No mock class.**

**`RunManager.Instance.ActionQueueSynchronizer` (field on plain-C# singleton):**
- `RunManager.Instance` is `{ get; } = new RunManager()` at class load — always
  non-null; no swap needed. Backing field `<Instance>k__BackingField` is writable
  but not required.
- Mechanism: call `RunManager.Instance.SetUpTest(runState, new
  NetSingleplayerGameService(), disableCombatStateSync: true)` via reflection.
  This populates `ActionQueueSynchronizer` (real object, no Godot), `ActionExecutor`,
  `ActionQueueSet`, `ChecksumTracker` (IsEnabled=false).
- Restore: call `RunManager.Instance.CleanUp()` in `finally` block.
- **Verdict: `SetUpTest`/`CleanUp` pattern. No mock class.**

**`NRunMusicController.Instance` (Godot Node, nullable via `NRun.Instance?.`):**
- Returns `null` headless. All call sites use `?.`. `TestMode.IsOn=true` makes
  `NonInteractiveMode.IsActive=true`, guarding every method body.
- Mechanism: **no action required**.
- **Verdict: NONE NEEDED. No mock class.**

### C2 (reflection-swap) viability per original plan

| Singleton | Static `Instance`? | Private backing field? | Reflection-swap viable? | Required? |
|---|---|---|---|---|
| `LocalContext` | N/A (static class) | N/A | N/A | No — direct `NetId` set |
| `RunManager` | `{ get; } = new RunManager()` (init-only) | `<Instance>k__BackingField` writable | Yes, but not needed | No — `SetUpTest` suffices |
| `ActionQueueSynchronizer` | Not a singleton; field on RunManager | N/A | N/A | No — populated by SetUpTest |
| `NRunMusicController` | Via `NRun.Instance?.RunMusicController` (Godot Node) | `RunMusicController` field | Not applicable headless (NRun.Instance==null) | No — null-safe, no-op |

### Concrete implementation: `TurnLoopBootstrap`

A.1.a's deliverable: a single class `TurnLoopBootstrap` inside `UpstreamDriver.cs`
or `src/MockLayer/TurnLoopBootstrap.cs` (~50 LOC):

```csharp
// MockLayer/TurnLoopBootstrap.cs  (~50 LOC; replaces 3-file A.1 decomposition)
internal sealed class TurnLoopBootstrap : IDisposable
{
    private readonly Assembly _sts2;
    private readonly ulong? _prevNetId;

    internal TurnLoopBootstrap(Assembly sts2, object runState, ulong netId = 1UL)
    {
        _sts2 = sts2;

        // (1) LocalContext.NetId = netId
        var localContextType = sts2.GetType("MegaCrit.Sts2.Core.Context.LocalContext")!;
        _prevNetId = (ulong?)localContextType.GetProperty("NetId")!.GetValue(null);
        localContextType.GetProperty("NetId")!.SetValue(null, (ulong?)netId);

        // (2) RunManager.SetUpTest(runState, new NetSingleplayerGameService(), ...)
        var runManagerType = sts2.GetType("MegaCrit.Sts2.Core.Runs.RunManager")!;
        var instance = runManagerType.GetProperty("Instance")!.GetValue(null)!;
        var singleplayerSvc = CreateSingleplayerService(sts2);
        runManagerType.GetMethod("SetUpTest", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(instance, new[] { runState, singleplayerSvc, true, false });
    }

    public void Dispose()
    {
        // Restore LocalContext.NetId
        var localContextType = _sts2.GetType("MegaCrit.Sts2.Core.Context.LocalContext")!;
        localContextType.GetProperty("NetId")!.SetValue(null, _prevNetId);

        // CleanUp RunManager (graceful=true)
        var runManagerType = _sts2.GetType("MegaCrit.Sts2.Core.Runs.RunManager")!;
        var instance = runManagerType.GetProperty("Instance")!.GetValue(null)!;
        runManagerType.GetMethod("CleanUp")!.Invoke(instance, new object[] { true });
    }

    private static object CreateSingleplayerService(Assembly sts2) { /* ... */ }
}
```

Used in A.2 `CaptureMidCombat` extension:
```csharp
using var bootstrap = new TurnLoopBootstrap(_sts2, runState);
try
{
    // ... invoke StartTurn / EndPlayerTurnPhaseOneInternal / ExecuteEnemyTurn via reflection
}
// Dispose() restores LocalContext.NetId + calls CleanUp()
```

**A.1.b and A.1.c are eliminated.** A.1 is a single serialized substream (~2–3h).

---

## §7 Risk Surface

### Risk 1 — `RunManager.SetUpTest` purity (LOW)

`RunManager.SetUpTest` → `InitializeShared` creates: `ChecksumTracker`,
`RunLocationTargetedBuffer`, `FlavorSynchronizer`, `ActionQueueSet`,
`ActionExecutor`, `ActionQueueSynchronizer`, `PlayerChoiceSynchronizer`,
`MapSelectionSynchronizer`, `ActChangeSynchronizer`, `EventSynchronizer`,
`RewardSynchronizer`, etc. All are plain C# objects; no Godot APIs in their
constructors. `InitializeRunLobby` with singleplayer creates `CombatStateSynchronizer`
(disabled). `InitializeNewRun` populates relic bags from `ModelDb` (pure C# — already
initialized in `EnsureModelDbInitialized`).

**Bail-out:** if any constructor throws on a hidden Godot dependency, fall back to
manually constructing `ActionQueueSynchronizer` + assigning via reflection to
`RunManager.Instance`'s backing field, and manually setting `ActionExecutor`.

### Risk 2 — `Cmd.CustomScaledWait` (NONE)

`StartTurn` line 324 calls `await Cmd.CustomScaledWait(0.5f, 0.8f)`. With
`NonInteractiveMode.IsActive == true`, the entire method body is a no-op. Already
handled by `TestMode.IsOn=true` set in `CaptureMidCombat`.

### Risk 3 — `NCombatRoom.Instance?.GetCreatureNode(enemy)` (NONE)

Returns `null` headless. `enemy.TakeTurn()` → `Monster.PerformMove()` does not
require `NCreature` to be non-null. `Creature.GetCreatureNode()` checks
`TestMode.IsOn` and returns `null` immediately.

### Risk 4 — `Hook.BeforeSideTurnStart` requires `LocalContext.NetId` non-null (MEDIUM if not set)

If `LocalContext.NetId == null`, `BeforeSideTurnStart`, `BeforeTurnEnd`, and
`AfterAutoPostPlayPhaseEntered` all skip hook iteration. For encounters where
monster powers fire during `BeforeSideTurnStart` on the enemy side (e.g.,
`LouseProgenitor.CURL_AND_GROW` at turn 2+ fires Strength gain here), skipping
hooks would produce incorrect enemy state. **Mitigation:** `TurnLoopBootstrap.Install`
sets `LocalContext.NetId = 1UL` before turn loop entry. Verified pattern.

### Risk 5 — `WaitForUnpause` / `ActionExecutor` blocking (LOW)

`WaitForUnpause` with `NonInteractiveMode.IsActive=true` is an immediate no-op.
`WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` checks
`ActionExecutor.CurrentlyRunningAction` — null if no action executing (headless at
turn-loop entry). The `AfterActionExecuted` subscription path is skipped. `ActionExecutor
.Pause()` is called in `InitializeShared`; must be followed by `ActionExecutor.Unpause()`
before the turn loop. **Mitigation:** call `ActionExecutor.Unpause()` after
`TurnLoopBootstrap.Install`; verify via `ActionExecutor.IsPaused == false` before
invoking `StartTurn`.

### Risk 6 — `ReadyToBeginEnemyTurnAction` async gate (MEDIUM)

`AfterAllPlayersReadyToEndTurn` (line 835) enqueues `ReadyToBeginEnemyTurnAction`
and awaits its `CompletionTask`. The `ActionExecutor` must run the action to
completion for the continuation (`AfterAllPlayersReadyToEndTurn` → `ExecuteEnemyTurn`)
to fire. Without an active action-pump loop, this awaits indefinitely.

**Recommended mitigation (A.2 engineer guidance):** Bypass the gate entirely by
calling turn-loop phases directly in sequence:
```
StartTurn(playerSide)  → snapshot "player-pre"
EndPlayerTurnPhaseOneInternal()  → snapshot "player-end"
ExecuteEnemyTurn()  → snapshot "enemy-end"
```
instead of following the full `StartCombatInternal` → `StartTurn` → `AfterAllPlayers
ReadyToEndTurn` → `ReadyToBeginEnemyTurnAction.CompletionTask` → `ExecuteEnemyTurn`
async chain. This is the same effect; the gate exists for multiplayer synchronization
which is irrelevant headless.

### Risk 7 — SceneTree transitive dependencies (NONE detected)

Exhaustive audit of `StartTurn`, `EndPlayerTurnPhaseOneInternal`, and
`ExecuteEnemyTurn` found no `GetTree()`, `SceneTree.CreateTimer()`, or
`Engine.GetMainLoop()` calls that execute when `NonInteractiveMode.IsActive=true`.
`Cmd.Wait` and `Cmd.CustomScaledWait` are the only SceneTree touchpoints and both
are fully gated by `NonInteractiveMode.IsActive`. **No SceneTree transitive
dependency blocks the mock layer.** No re-surface trigger fires.

### Re-surface trigger evaluation

| Trigger | Status |
|---|---|
| Mock layer scope >30h | NOT FIRED (~11–16h total) |
| SceneTree transitive dependency (unblockable) | NOT FIRED (all guarded by NonInteractiveMode) |
| Hook chain calls Godot.Audio/Animation beyond music | NOT FIRED (music is no-op; no animation in turn loop) |
| A.0 INCONCLUSIVE | NOT FIRED (feasibility confirmed) |

---

## Verdict

**Wave-50/A.0 FEASIBLE. No re-surface triggers. A.1 dispatch approved.**

Mock layer reduces to:
- A.1.a only (no A.1.b, A.1.c): `TurnLoopBootstrap` ~50 LOC; eliminates 3 mock classes
- `LocalContext.NetId` direct-set (not a singleton swap)
- `RunManager.SetUpTest` via reflection (not a mock class)
- `NRunMusicController` requires no action

Total wave-50 estimate: ~11–16h (vs. project-lead's 16–24h baseline; vs. 30h trigger).
