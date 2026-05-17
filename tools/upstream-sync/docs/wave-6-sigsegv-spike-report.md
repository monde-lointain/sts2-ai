# Wave 6 SIGSEGV Spike Report

**Date:** 2026-05-17
**Engineer:** Wave 6.5 / B.2-α.SIGSEGV (Opus 4.7)
**Methodology:** superpowers:systematic-debugging (root cause before fix)
**Scope:** Read-only forensic spike. No engine code edits. Single deliverable: this report.
**Pre-flight SHA:** `b679d0e7b940c9ccc27f81febc6560248f63008a` (Wave 6 merge) — verified.

---

## Executive summary

Wave 6's reflection refactor is correct and complete: 41/41 reflection targets resolve, the `CombatState` ctor predicate handles v0.103.2/v0.105.1 drift, and the call chain successfully reaches `CombatManager.SetUpCombat`. The SIGSEGV that emerged is **not a regression introduced by Wave 6** — it is a **latent upstream regression in v0.105.1** that was previously hidden behind the now-removed ctor blocker.

**Root cause:** Upstream v0.105.1 added `Log.LogMessage(...)` and `Log.Error(...)` calls to `NetCombatCardDb.IdCardIfNecessary` (file `src/Core/GameActions/Multiplayer/NetCombatCardDb.cs`, lines 117-122). These are the FIRST logging calls executed in our capture process. Their invocation triggers eager static-class-construction (`Log..cctor` → `Logger..cctor` → `Godot.OS..cctor`) which dispatches to native Godot interop functions (`godotsharp_string_new_with_utf16_chars`) — function-pointer table is uninitialized because the Godot runtime is not loaded in our standalone .NET console process. Hard SIGSEGV inside libc when the JIT-emitted P/Invoke trampoline dereferences a null/garbage native function pointer.

**Classification:** **H1 variant** — null-deref on uninitialized "singleton", but the singleton is `GodotSharp.dll`'s GDExtension native function table, NOT a managed singleton fix-able via reflection. The cctor that crashes cannot be skipped from managed code (`RuntimeHelpers.GetUninitializedObject` still triggers cctors for precise-init types per `coreclr/vm/reflectioninvocation.cpp` lines 1957-1960).

**Wave 6.5.β scope recommendation:** **COMPRESSED scope (~40-80 LOC)** — primary fix is surgical (replace `InvokeMethod(... "SetUpCombat" ...)` with manually-replicated SetUpCombat body that omits the offending `NetCombatCardDb.Instance.StartCombat` call); child-process isolation should be added as optional defense-in-depth against future upstream regressions in other code paths. Full sidecar-machinery scope (80-150 LOC) is unnecessary given precise root cause is identified.

---

## Reproduction

- **Command:** `make probe-upstream-capture` (equivalently: `dotnet run --project test/determinism-probe-upstream-capture -- --batch-out <dir>`)
- **Deterministic across 3 independent runs:** YES — identical managed stack frame sequence captured by `dotnet-dump` for 3 separate core dumps (PIDs 53289, 53812, 54211).
- **First (and every) crashing tuple:** the very first encounter that the batch iterates. By `EncounterCatalog.AllKnownIds()` traversal order (ProcessOrder), this is the first upstream-comparable encounter. Crash happens INSIDE `CombatManager.SetUpCombat`, not at any encounter-specific code path — all encounters/seeds trigger identical crash.
- **Time to crash:** ~7s wall-clock (ModelDb.Inject takes the bulk: ~544 total injections across all model categories). The actual segfault is microseconds after Logger..cctor first executes.
- **CLR mini-dumps captured:** `/tmp/wave6-5-dumps/coredump.{53289,53812,54211}` (3× ~257 MB full memory dumps).

---

## Crash evidence

- **Exit code:** 139 (signal 11 / SIGSEGV) — confirmed by `dotnet run` rc and CLR `createdump` output `Crashing thread d029 signal 11 (000b)`.
- **createdump output (verbatim, run 3):**
  ```
  inject Encounter: 88 ok, 0 skipped (of 88)
  inject Monster: 120 ok, 0 skipped (of 120)
  inject Event: 68 ok, 0 skipped (of 68)
  inject Ancient: 9 ok, 0 skipped (of 9)
  inject Power: 257 ok, 0 skipped (of 257)
  inject Badge: 2 ok, 0 skipped (of 2)
  [createdump] Gathering state for process 53289 Sts2Headless.Up
  [createdump] Crashing thread d029 signal 11 (000b)
  [createdump] Writing full dump to file /tmp/wave6-5-dumps/coredump.53289
  [createdump] Written 257044480 bytes (62755 pages) to core file
  ```
  ModelDb init completes cleanly (all `Inject` calls succeed); the crash occurs inside the first `Capture()` body, BEFORE any `ok` line for an encounter is emitted.

- **Crash IP (rip):** `0x000078C2B2D10813`
- **Address mapping:** `libc.so.6` (range `0x000078C2B2C00000–0x000078C2B2E05000`, offset `+0x110813`). The native frame is libc-internal (likely `__strlen` or similar memory walk), invoked from JIT-compiled `Godot.NativeInterop.NativeFuncs.godotsharp_string_new_with_utf16_chars` (in `GodotSharp.dll`) — a managed-to-unmanaged P/Invoke trampoline whose target function pointer is uninitialized.
- **Registers at crash:**
  - `rax = 0xFFFFFFFFFFFFFE00` (Linux syscall errno-style return, -512)
  - `rdi = 0x000000000000D03A` (small integer — looks like a corrupted/uninitialized pointer arg)
  - `rsi = 0x000078C2B345A6D4` (stack-adjacent)
  - `rdx = 0x0` (null)
  - `rip = 0x000078C2B2D10813` (libc internal)
- **Crash instruction:** Inside libc internals; not relevant to root cause beyond confirming the native function pointer in `GodotSharp.dll` is dispatching to garbage memory.

---

## Stack trace (managed, from `dotnet-dump analyze ... clrstack -i -f`)

```
Frame 0: Godot.NativeInterop.NativeFuncs.godotsharp_string_new_with_utf16_chars(godot_string&, char*)   [GodotSharp.dll, native target]
Frame 1: Godot.NativeInterop.Marshaling.ConvertStringToNative(string)                                    [GodotSharp.dll]
Frame 2: Godot.NativeInterop.NativeFuncs.godotsharp_string_name_new_from_string(string)                  [GodotSharp.dll]
Frame 3: Godot.StringName..ctor(string)                                                                  [GodotSharp.dll]
Frame 4: Godot.StringName.op_Implicit(string)                                                            [GodotSharp.dll]
Frame 5: Godot.OS..cctor()                                                                               [GodotSharp.dll]
Frame 6: Godot.OS.GetCmdlineArgs()                                                                       [GodotSharp.dll]
Frame 7: MegaCrit.Sts2.Core.Logging.Logger.GetIsRunningFromGodotEditor()                                 [sts2.dll]
Frame 8: MegaCrit.Sts2.Core.Logging.Logger..cctor()                                                      [sts2.dll]
Frame 9: MegaCrit.Sts2.Core.Logging.Logger..ctor(string, LogType)                                        [sts2.dll]
Frame 10: MegaCrit.Sts2.Core.Logging.Log..cctor()                                                        [sts2.dll]
Frame 11: MegaCrit.Sts2.Core.Logging.Log.LogMessage(LogLevel, LogType, string, int)                      [sts2.dll]
Frame 12: MegaCrit.Sts2.Core.GameActions.Multiplayer.NetCombatCardDb.IdCardIfNecessary(CardModel)        [sts2.dll]
Frame 13: MegaCrit.Sts2.Core.GameActions.Multiplayer.NetCombatCardDb.StartCombat(IReadOnlyList<Player>)  [sts2.dll]
Frame 14: MegaCrit.Sts2.Core.Combat.CombatManager.SetUpCombat(CombatState)                               [sts2.dll]
Frame 15: System.Reflection.RuntimeMethodInfo.Invoke(...)                                                [System.Private.CoreLib]
Frame 16: Sts2Headless.UpstreamCapture.UpstreamDriver.InvokeMethod(...) [UpstreamDriver.cs:930]          [UpstreamCapture.dll]
Frame 17: Sts2Headless.UpstreamCapture.UpstreamDriver.Capture(...)      [UpstreamDriver.cs:511]
Frame 18: Sts2Headless.UpstreamCapture.Program.RunBatch(...)             [Program.cs:119]
Frame 19: Sts2Headless.UpstreamCapture.Program.Main(...)                 [Program.cs:53]
```

**Critical observation:** the stack walks cleanly through 14 managed frames before transitioning to native. The crash is in JIT-compiled application code (`GodotSharp.dll`), not CLR internals. Stack integrity confirms NO memory corruption from the driver.

---

## Trigger chain (annotated)

```
SetUpCombat(state)                          // line 189 of upstream CombatManager.cs
  ├ _state = state                          //  L195 — safe
  ├ MultiplayerScalingModel?.OnCombatEntered// L196 — null-conditional, safe
  ├ StateTracker.SetState(state)            // L197 — safe (just _state field set)
  ├ _playersTakingExtraTurn.Clear()         // L200 — safe
  ├ foreach player: ResetCombatState        // L202-205 — creates PlayerCombatState; safe
  ├ foreach player: PopulateCombatState     // L206-209 — populates DrawPile; safe (no logging)
  ├ NetCombatCardDb.Instance.StartCombat(players)  // L210  ← CRASH PATH STARTS HERE
  │   └ IdCardIfNecessary(card)             //  L113 of NetCombatCardDb.cs (v0.105.1)
  │     └ Log.LogMessage(Debug, Network, "ID card ...")  // L122 ← NEW in v0.105.1 (DIFF vs v0.103.2)
  │       └ Log..cctor() FIRES              // type-init: `_logger = new Logger(null, LogType.Generic)`
  │         └ new Logger(...) → Logger..cctor() FIRES
  │           └ static readonly _isRunningFromGodotEditor = GetIsRunningFromGodotEditor()
  │             └ string[] cmdlineArgs = OS.GetCmdlineArgs()   // calls into Godot
  │               └ Godot.OS..cctor() FIRES
  │                 └ Godot.StringName.op_Implicit("OS")
  │                   └ Godot.StringName..ctor("OS")
  │                     └ godotsharp_string_name_new_from_string("OS")
  │                       └ Marshaling.ConvertStringToNative("OS")
  │                         └ godotsharp_string_new_with_utf16_chars(...) → SIGSEGV in libc
  ├ foreach creature: AddCreature           // L211-214 — never reached
  └ CombatSetUp?.Invoke(state)              // L215 — never reached
```

---

## Version diff confirming the regression (v0.103.2 → v0.105.1)

`/home/clydew372/development/projects/godot/sts2/` is a clean clone of upstream with tags `v0.103.2` (pin baseline) and `v0.105.1` (current Steam build). Diff of `NetCombatCardDb.cs`:

```diff
+ using MegaCrit.Sts2.Core.Logging;
...
  private void IdCardIfNecessary(CardModel card)
  {
      if (!_cardToId.ContainsKey(card))
      {
+         if (card.Owner == null)
+         {
+             Log.Error($"Tried to ID combat card {card} without an owner! This is not allowed");
+             return;
+         }
+         Log.LogMessage(LogLevel.Debug, LogType.Network, $"ID card {card} owned by {card.Owner.NetId} ...");
          _cardToId[card] = _nextId;
          _idToCard[_nextId] = card;
          _nextId++;
      }
  }
```

In `v0.103.2` (pinned content baseline), `IdCardIfNecessary` had NO `Log.*` calls. The driver could execute the full `SetUpCombat` body without ever firing the `Log/Logger/Godot.OS` cctor chain. In `v0.105.1`, the upstream developers added Debug + Error logging — which would be a no-op in their Godot-runtime environment (Logger initialization works fine when Godot is loaded), but a hard SIGSEGV in our standalone .NET console process.

---

## Hypotheses considered

### H1a: SetUpCombat dereferences `NCombatRoom.Instance` or `RunManager.Instance.ActionExecutor` (per dispatch-prompt seed)

- **Reasoning:** these are documented in `Program.cs` Stream-C invariant as scene-tree-coupled singletons SetUpCombat must not reach.
- **Test:** read SetUpCombat body (`CombatManager.cs:189-216`); look for null deref on any of those types.
- **Outcome:** REFUTED — `SetUpCombat` body itself does NOT reference NCombatRoom, RunManager.ActionExecutor, etc. Those are only reached from `StartCombatInternal` (line 225+) which is NOT called by our path. The Stream-C invariant correctly excluded scene-tree singletons; SetUpCombat's body is scene-tree-free, EXCEPT for the new (v0.105.1) `Log.LogMessage` call inside the `NetCombatCardDb.StartCombat` subroutine.

### H1b: `NetCombatCardDb.Instance` itself is null or partially initialized

- **Reasoning:** the Stream-C invariant lists `NetCombatCardDb.Instance` as a forbidden singleton. SetUpCombat does call `NetCombatCardDb.Instance.StartCombat`.
- **Test:** read `NetCombatCardDb.cs`; check its singleton init pattern.
- **Outcome:** REFUTED at face — `NetCombatCardDb` is a pure-managed singleton with `public static NetCombatCardDb Instance { get; } = new NetCombatCardDb();`. The ctor only initializes empty dictionaries; no scene-tree coupling. **Confirmed by stack trace:** Frame 13 successfully enters `NetCombatCardDb.StartCombat`. The Stream-C invariant was over-cautious in flagging NetCombatCardDb (correctly cautious for downstream contents, but the singleton itself is harmless).

### H1c (TRUE H1 VARIANT): `Godot.OS`/`GodotSharp.dll` native function table is uninitialized

- **Reasoning:** `Godot.OS..cctor` invokes native interop into the embedded Godot runtime via P/Invoke; in a standalone .NET console process (no Godot host), the function-pointer table is null.
- **Test:** inspect Godot.OS..cctor IL via `dumpil`; verify it calls `Godot.StringName::op_Implicit("OS")` immediately at IL_0000, which goes through `godotsharp_string_*` natives.
- **Outcome:** **VERIFIED.** IL dump confirms `Godot.OS..cctor` first instructions are `ldstr "OS"; call Godot.StringName::op_Implicit(string)`. This is the eager static field initializer for `Godot.OS.NativeName = (StringName)"OS";` — fires unconditionally during type init, dispatching to a native function pointer that doesn't exist in our process.

### H2: CLR-internal crash (GC, JIT, marshaling bug)

- **Reasoning:** crash address could resolve to libcoreclr.so.
- **Test:** check `modules` and address ranges; verify crash IP location.
- **Outcome:** REFUTED. Crash IP `0x000078C2B2D10813` resolves to libc.so.6 (`0x78C2B2C00000–0x78C2B2E05000`), inside JIT-compiled application code's call into libc. Not a CLR bug. Marshaling code (`Godot.NativeInterop.Marshaling.ConvertStringToNative`) is application-level GodotSharp code, not CLR.

### H3: Memory corruption from reflection-driven `ToMutable` / partially-initialized objects

- **Reasoning:** the driver reflectively constructs Player, RunState, CombatState, monsters — corruption could propagate to SetUpCombat.
- **Test:** verify stack walk is clean; verify managed objects in stack frames are well-formed (`clrstack -a` LOCAL/PARAMETER values).
- **Outcome:** REFUTED. The 14-frame managed stack walks cleanly via ICorDebug. `clrstack -a` shows well-formed parameter and local values throughout `SetUpCombat`, `StartCombat`, `IdCardIfNecessary`. The `this` for NetCombatCardDb is non-null (`0x000078821e9c5f30`); the `card` parameter is non-null (`0x000078821e9c2a18`). The crash is deterministic and reproducible at the exact same call site — symptomatic of an env/state issue, not corruption.

---

## Root cause classification

- **Final category:** **H1 variant** — null-deref triggered by upstream's static cctor chain dispatching into uninitialized Godot native function table.
- **Specific cause:** Upstream v0.105.1 introduced `Log.LogMessage` + `Log.Error` calls inside `NetCombatCardDb.IdCardIfNecessary` (file `src/Core/GameActions/Multiplayer/NetCombatCardDb.cs` lines 117-122). These calls trigger first-time initialization of `Log/Logger` static state, which lazily fires `Godot.OS..cctor` → `Godot.StringName.op_Implicit("OS")` → native P/Invoke into `godotsharp_string_new_with_utf16_chars`. The native function pointer is bound to `GodotSharp.dll`'s GDExtension table, which is only populated when the Godot runtime is loaded. In our standalone `dotnet run` process, the table is uninitialized and the dispatch crashes.

**Caveat: NOT fixable by reflection-poking a singleton.** The "null singleton" is the Godot native function-pointer table, which is populated only by the Godot runtime's `GDExtension::initialize`. There is no managed-code mechanism to populate it. CLR's `RuntimeHelpers.GetUninitializedObject` also fires the type cctor for "precise init" types (per `coreclr/vm/reflectioninvocation.cpp:1957-1960` — `CheckRunClassInitAsIfConstructingThrowing`), so we cannot create an uninitialized `Logger` either.

---

## Recommended fix

### Primary (surgical, ~20-40 LOC) — manually replicate SetUpCombat body

Replace the reflection invocation of `CombatManager.SetUpCombat` in `UpstreamDriver.cs` (current lines 511-516) with a manually-replicated body that performs the same 6 logical steps EXCEPT the offending `NetCombatCardDb.Instance.StartCombat` call.

**Where to inject:** `engine/headless/test/determinism-probe-upstream-capture/src/UpstreamDriver.cs` lines 499-516 (replace block).

**Manual SetUpCombat body (pseudocode for the successor wave's prompt; **do not implement in this wave**):**

```csharp
// Replaces InvokeMethod(combatManagerType, combatManagerInstance, "SetUpCombat", ...).
// Replicates upstream CombatManager.SetUpCombat(state) lines 195-215 EXCEPT
// the v0.105.1-only NetCombatCardDb.Instance.StartCombat(state.Players) call
// at line 210, which triggers Log/Logger/Godot.OS cctor chain → native SIGSEGV.

// L195: CombatManager._state = state
FieldInfo stateField = combatManagerType.GetField("_state",
    BindingFlags.NonPublic | BindingFlags.Instance)!;
stateField.SetValue(combatManagerInstance, combatState);

// L196: MultiplayerScalingModel?.OnCombatEntered(state)
object? multiplayerScaling = ReflectionFlex.TryGetProperty(combatState, "MultiplayerScalingModel");
if (multiplayerScaling is not null)
{
    multiplayerScaling.GetType()
        .GetMethod("OnCombatEntered", BindingFlags.Public | BindingFlags.Instance)!
        .Invoke(multiplayerScaling, new object[] { combatState });
}

// L197: StateTracker.SetState(state)
object stateTracker = combatManagerType
    .GetProperty("StateTracker", BindingFlags.Public | BindingFlags.Instance)!
    .GetValue(combatManagerInstance)!;
stateTracker.GetType()
    .GetMethod("SetState", BindingFlags.Public | BindingFlags.Instance)!
    .Invoke(stateTracker, new object[] { combatState });

// L198-201: _playerReadyLock.EnterScope(); _playersTakingExtraTurn.Clear()
//   SKIP — irrelevant to byte serialization (no state observable).

// L202-205: foreach player: ResetCombatState()
// L206-209: foreach player: PopulateCombatState(player.RunState.Rng.Shuffle, state)
object playersList = combatState.GetType()
    .GetProperty("Players", BindingFlags.Public | BindingFlags.Instance)!
    .GetValue(combatState)!;
MethodInfo resetMi = playerType.GetMethod("ResetCombatState",
    BindingFlags.Public | BindingFlags.Instance)!;
MethodInfo populateMi = playerType.GetMethod("PopulateCombatState",
    BindingFlags.Public | BindingFlags.Instance)!;
foreach (object p in (IEnumerable)playersList)
{
    resetMi.Invoke(p, null);
}
foreach (object p in (IEnumerable)playersList)
{
    object runState_p = playerType.GetProperty("RunState")!.GetValue(p)!;
    object rng = runState_p.GetType().GetProperty("Rng")!.GetValue(runState_p)!;
    object shuffleRng = rng.GetType().GetProperty("Shuffle")!.GetValue(rng)!;
    populateMi.Invoke(p, new object[] { shuffleRng, combatState });
}

// L210: NetCombatCardDb.Instance.StartCombat(state.Players) — SKIPPED
//   This is the v0.105.1-regression injection site. The dictionary it
//   populates is multiplayer net-id tracking; the byte snapshot we
//   produce does not include net-ids, so skipping is semantically a no-op
//   for our use case.

// L211-214: foreach creature: AddCreature(creature)
MethodInfo addCreatureMi = combatManagerType.GetMethod("AddCreature",
    BindingFlags.Public | BindingFlags.Instance)!;
object creaturesList = combatState.GetType()
    .GetProperty("Creatures", BindingFlags.Public | BindingFlags.Instance)!
    .GetValue(combatState)!;
foreach (object c in (IEnumerable)creaturesList)
{
    addCreatureMi.Invoke(combatManagerInstance, new object[] { c });
}

// L215: CombatSetUp?.Invoke(state) — event subscribers; SKIPPED (no subscribers in headless).
```

**Estimated LOC:** ~30-45 net (replaces a 6-line `InvokeMethod(...)` block).

**Why this is safe:**
1. `NetCombatCardDb.StartCombat` populates a card-id dictionary used by multiplayer net-serialization. Our byte snapshot serializes `(CurrentHp, Block, Powers, Energy, pile counts)` — none of which involve net-ids. Skipping the call produces semantically-identical bytes.
2. The OTHER 5 steps (state assignment, MultiplayerScalingModel.OnCombatEntered, StateTracker.SetState, ResetCombatState, PopulateCombatState, AddCreature loop) all complete WITHOUT touching `Log.*`. Confirmed by grep across `Player.cs`, `PlayerCombatState.cs`, `CardPile.cs`, `CombatState.cs`, `CombatStateTracker.cs`, `MultiplayerScalingModel.cs`, `MonsterModel.cs` — only Log call in those files is `Player.AddPotionInternal` (warn on slot collision), which our `PopulateStartingInventory` path doesn't hit.
3. Determinism preserved: same `runState.Rng.Shuffle` feeds `PopulateCombatState`, same `state.SortEnemiesBySlotName()` inside AddCreature.

**Verification plan for the successor wave:**
1. Run `make probe-upstream-capture` and confirm exit code 0 (was 139).
2. Run `make probe-upstream-initial-state` (the 22 encounters x 10 seeds byte-compare gate). Expected: 140 passed / 0 diverged / 20 skipped / 0 errored — IDENTICAL to v0.103.2 baseline. Any divergence would indicate the skipped `NetCombatCardDb.StartCombat` HAD a side-effect we missed.
3. Run `make q1-ci` / `q1-gate` to confirm no regression in headless test suite.

### Secondary (defensive, optional ~15-30 LOC) — child-process isolation

Even with the primary fix, the v0.105.1 regression demonstrates a class of risk: **any future upstream patch that adds a `Log.*` call (or other Godot interop) to a code path we exercise will re-introduce SIGSEGV.** Defense-in-depth: run each `Capture()` in a forked child process. If a child SIGSEGVs, the parent detects exit code 139 and records an `.error` sidecar; remaining encounters proceed. Compressed scope is sufficient — no need for full bidirectional pipe / progress reporting.

**Note:** child-process isolation **alone** does NOT solve the current bug (each child would still SIGSEGV before producing bytes). It is ONLY useful in combination with the primary fix above, OR for future upstream regressions in DIFFERENT code paths (e.g., AddCreature's `Monster.SetUpForCombat` adding a `Log.Info` in a future patch). Recommended as a defense-in-depth measure but not a Wave 6.5 must-have.

### If approach reversal needed — Harmony patching (NOT RECOMMENDED for Wave 6.5)

Theoretical alternative: use `0Harmony.dll` (already in Steam install) to monkey-patch `Logger.GetIsRunningFromGodotEditor()` returning `false` before Logger..cctor fires. **Brittle** (JIT inlining can prevent patch attachment; ordering with cctor is fragile); **larger LOC** (~80-150); fights upstream rather than working with it. Document for posterity but do not implement.

---

## Wave 6.5.β scope recommendation

**COMPRESSED scope (~40-80 LOC).** Recommended composition:
- ~30-45 LOC: manually-replicated SetUpCombat body in `UpstreamDriver.cs` (primary fix).
- ~15-30 LOC: optional child-process isolation in `Program.cs` (defense-in-depth; nice-to-have).
- Skip: bidirectional sidecar progress machinery (overkill for batch capture of 220 tuples).

**Rationale:**
- Full scope (~80-150 LOC, child-process + sidecar + progress) is unjustified given a precise root cause exists with a 30-LOC surgical fix.
- Skip-entirely is wrong because v0.105.1 will continue to expand its Log call sites in future versions; we need a stable adapter strategy.
- Compressed scope keeps the engineer focused on the ONE code-edit site (UpstreamDriver.cs line 499-520 replacement) plus optional Program.cs hardening.

---

## Cross-references

- `engine/headless/test/determinism-probe-upstream-capture/src/UpstreamDriver.cs` (Wave 6 / `b679d0e`) — lines 499-520 are the SetUpCombat-invocation block to replace.
- `engine/headless/test/determinism-probe-upstream-capture/src/Program.cs` — lines 17-22 (Stream-C invariant comment); will need an update for the successor wave's documented exclusion of `NetCombatCardDb.StartCombat`.
- `/home/clydew372/development/projects/godot/sts2/src/Core/Combat/CombatManager.cs` (upstream v0.105.1) — `SetUpCombat` lines 189-216.
- `/home/clydew372/development/projects/godot/sts2/src/Core/GameActions/Multiplayer/NetCombatCardDb.cs` (upstream v0.105.1) — `IdCardIfNecessary` lines 113-127 (the new Log calls).
- `/home/clydew372/development/projects/godot/sts2/src/Core/Logging/Logger.cs` (upstream v0.105.1) — line 14 (`_isRunningFromGodotEditor` static field initializer firing `OS.GetCmdlineArgs()`).
- Upstream git tags: `v0.103.2` (pinned baseline) vs `v0.105.1` (current Steam build) — diff above documents the regression.
- ADR-026 (pin semantics) — Wave 6 work correctly leaves pin at v0.103.2 (content unchanged); bridge work in progress.
- ADR-027 (Phase-1 caps track upstream) — successor wave's surgical fix is bridge-wave scope, not pin advancement.
- `.claude/state/waves/6.json` — Wave 6 close-out state.
- Core dumps for repro: `/tmp/wave6-5-dumps/coredump.{53289,53812,54211}` (3× independent, identical stacks); reproduction logs at `/tmp/wave6-5-capture-run{3,4,5}.log`.
- `dotnet-dump` tool installed at `~/.dotnet/tools/dotnet-dump` (user-global, no elevation required).

---

## Time spent

~3.5 hours wall-clock:
- ~30 min: pre-flight, environment inspection, file reads.
- ~1 hr: reproducing crash (3× with mini-dump capture), dotnet-dump install/setup.
- ~1.5 hr: stack trace analysis, IL inspection, upstream source spelunking, dotnet-runtime source spelunking (RuntimeHelpers.GetUninitializedObject precise-init confirmation), version-diff identification.
- ~30 min: hypothesis ranking, fix space exploration (Harmony, GetUninitializedObject, child-process), report authoring.

## Deferrals

- **DllSignatureGate addition:** the gate could in principle add a check for "upstream Log calls in NetCombatCardDb paths" to detect this class of regression in CI. NOT recommended — too narrow / brittle. Defer to a broader "scene-tree-coupling sentinel" gate (Wave 7+ Q1 architecture work).
- **Upstream PR / issue filing:** considered filing an upstream issue ("Log.LogMessage in net-id assignment forces eager Godot.OS init"). Out of scope for Wave 6.5; project lead may decide whether to engage.
- **dotnet-dump availability automation:** Wave 6.5 engineer should not be expected to install dotnet-dump on demand. Add to repo's dev-setup docs as part of "dev machine prerequisites" (alongside Steam install location). Out of scope for this spike.

## Re-surface triggers hit

- **None.** Reproduction was deterministic (3 identical stacks); both `dotnet-dump` and `lldb` had a path (dotnet-dump installed cleanly as user-global tool, no elevation); crash address resolved unambiguously to a JIT'd P/Invoke trampoline inside `GodotSharp.dll`; Wave 6 work did NOT introduce a CLR regression (it correctly unblocked a pre-existing v0.105.1 latent bug).
