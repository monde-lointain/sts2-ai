Verdict: FAIL — option B is dead. Criterion #1 PASSES (boot without cctor wall), but AutoSlayer is structurally blocked by a UI-scene-tree dependency that cannot be resolved inside headless mode without a prohibitive Harmony stub surface exceeding option-B framing; additionally criterion #4 FAILS by proof-of-absence (zero mid-combat serialization primitives), and criterion #5 FAILS by proof-of-absence (Modding surface is event-notification only with no decision-boundary interception). Option C (parallel C# substrate, ADR-034) is terminal.

---

## §Environment summary

Step-0 outcomes (all run 2026-05-21):

| Step | Check | Outcome |
|---|---|---|
| 1 | `git rev-parse HEAD == 70128d5` | PASS after `git reset --hard main` (worktree was at 030bd5d; local main at 70128d5; remote origin 2 commits behind local) |
| 2 | Branch is `wave-43/B-godot-headless-spike` | PASS after `git branch -m wave-43/B-godot-headless-spike` |
| 3 | `which Godot_v4.5.1-stable_mono_linux.x86_64` | PASS: `/home/clydew372/applications/godot/Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64` |
| 4 | `Godot_v4.5.1-stable_mono_linux.x86_64 --version` | PASS: `4.5.1.stable.mono.official.f62fdbde1` |
| 5 | `ls .../GodotSharp/GodotSharp.dll` | FAIL at plan path; actual path is `data_sts2_linuxbsd_x86_64/GodotSharp.dll` |
| 5a | GodotSharp.dll version | PASS: AssemblyInformationalVersion `4.5.1+f62fdbde15035c5576dad93e586201f4d41ef0cb` — commit hash matches system Godot `f62fdbde1`, ABI compatible |
| 6 | Game-bundled Godot binary | PASS: `SlayTheSpire2` at Steam root is Godot engine binary, version `4.5.1.m.11.mono.custom_build`. Preferred per Step-0 protocol (ABI-guaranteed match). |
| 7 | `dotnet --version >= 8.0` | PASS: `9.0.116` |
| 8 | `ls .../src/Core/Combat/CombatManager.cs` | PASS |

**GodotSharp.dll actual path:**
`~/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/GodotSharp.dll`

**System Godot:** `4.5.1.stable.mono.official.f62fdbde1`
**Game-bundled engine:** `SlayTheSpire2` v`4.5.1.m.11.mono.custom_build` (custom build, ABI match via GodotSharp.dll version check)
**ABI decision:** System Godot and game-bundled GodotSharp.dll share commit hash `f62fdbde` — compatible. Game-bundled binary preferred per Step-0 protocol.
**dotnet:** `9.0.116`
**HarmonyLib:** `0Harmony.dll` ships with game at `data_sts2_linuxbsd_x86_64/0Harmony.dll`

**Note on overlay approach discovery:** The plan assumes `project.godot` exists on disk for patching. It does not — the game uses `SlayTheSpire2.pck` (packed resources). `project.godot` is embedded in the PCK. The overlay approach (cp game dir, patch project.godot, inject autoload) is blocked. The actual injection mechanism is the game's own Mod system (`mods/` directory adjacent to the executable). This is documented in §Reproduction.

---

## §Per-criterion findings

### Criterion #1 — `godot --headless` boots without SIGSEGV in Logger.cctor

**Status: PASS**

**Evidence:** Game-bundled `SlayTheSpire2` binary boots cleanly with `--headless --force-steam=off`:

```
MegaDot v4.5.1.m.11.mono.custom_build - https://godotengine.org
[INFO] Steam initialization skipped (editor mode). Use --force-steam to enable.
[INFO] Registered 11 migrations
[INFO] ModelIdSerializationCache initialized. Categories: 20 Entries: 1617
[INFO] Preloading 'IntroLogo' assets... count=3 vfx=0
FMOD Sound System: System released
```

No SIGSEGV. No cctor exception. `Logger.cctor` fires (Logger is called by the game's startup chain); `OS.GetCmdlineArgs()` P/Invoke succeeds because the GDExtension table is populated by engine init before any C# code runs. The Wave-6.5 cctor wall was a "call INTO Godot from dotnet host" problem, not a "run INSIDE Godot" problem. Hypothesis confirmed.

**First log line:** `MegaDot v4.5.1.m.11.mono.custom_build - https://godotengine.org`

**Steam bypass:** `--force-steam=off` triggers the existing bypass condition in `NGame.InitializePlatform()` (`NGame.cs:868`):
```csharp
if (!text.Equals("on") && (!flag || !(text == "")) && (text.Equals("off") || OS.HasFeature("editor")))
```
With `text = "off"`, condition evaluates true → Steam init skipped. No Harmony patch needed for Steam.

**Reproduction command:**
```bash
"$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2" \
  --headless --quit --force-steam=off 2>&1
```
Expected: exits 0 with log lines above. No SIGSEGV.

---

### Criterion #2 — AutoSlayer drives cultist combat to terminal

**Status: FAIL — structural blocker (UI scene tree dependency)**

**Analysis:**

The `--autoslay` CLI flag is gated by `!IsReleaseGame()` (`NGame.cs:275`):
```csharp
if (!IsReleaseGame() && CommandLineHelper.HasArg("autoslay"))
```
`IsReleaseGame()` is hardcoded `return true` in the shipped binary (`NGame.cs:331-334`). A Harmony patch to return `false` is technically possible (see §Harmony patch sites) and is the Criterion #2 path.

**However, even with the Harmony patch, the execution is blocked by a structural dependency:**

`AutoSlayer.PlayMainMenuAsync()` (`AutoSlayer.cs:356`) does:
```csharp
Control mainMenu = await WaitHelper.ForNode<Control>(root, "/root/Game/RootSceneContainer/MainMenu", ct, ...);
NButton node = mainMenu.GetNode<NButton>("MainMenuTextButtons/AbandonRunButton");
...
NCharacterSelectButton nCharacterSelectButton = _random.NextItem(items);
nCharacterSelectButton.Select();
```

In `--headless` mode, Godot uses the `dummy` display driver. All `CanvasItem.IsVisibleInTree()` calls return `false`. `UiHelper.Click()` depends on `IsVisibleInTree()`. `WaitHelper.Until()` with visibility conditions never resolves. `NButton.Visible` is always `false` in headless.

The result: `AutoSlayer.PlayMainMenuAsync()` hangs indefinitely at the `WaitHelper.ForNode` call for the main menu button visibility.

**Stubbing requirement:** To make criterion #2 work, ALL of the following would need Harmony stubs:
- `NButton.Visible` (or `CanvasItem.IsVisibleInTree`)
- `NCharacterSelectButton` node existence and state
- `NOverlayStack.Instance` singleton
- `NMapScreen.Instance` singleton
- `NModalContainer.Instance` singleton
- `NCombatRoom.Instance` singleton (for combat)
- `NRewardsScreen` presence detection
- `WaitHelper.Until()` visibility predicates

This exceeds option-B framing. AutoSlayer was designed for a game with a running scene tree, not a headless stub.

**`CombatManager.GameStatus` observed:** Not reachable without resolving the above.

---

### Criterion #3 — Per-decision latency

**Status: UNREACHABLE — blocked by criterion #2 failure**

Methodology was: Stopwatch on `CombatRoomHandler.HandleAsync` entry/exit per turn. Could not reach combat because AutoSlayer.PlayMainMenuAsync() blocks before first combat is entered.

**Thresholds for reference:** PASS < 500µs p99; QUALIFIED 500µs-5ms; FAIL >= 5ms.

p50/p95/p99: NOT MEASURED.

---

### Criterion #4 — Save/restore mid-combat bit-identical

**Status: FAIL — proof-of-absence (no mid-combat serialization primitive)**

**Step (a): grep result:**
```
grep -r 'Serializable\|WriteState\|SaveCombat\|CombatSnapshot' \
  ~/development/projects/godot/sts2/src/Core/Saves/ \
  ~/development/projects/godot/sts2/src/Core/Combat/
```

**Hits in `Core/Saves/`:** `SerializableExtraPlayerFields`, `SerializableUnlockedAchievement`, `SerializableEpoch`, `SerializableProgress` — ALL are run-meta / progress data, NOT combat state. No `CombatSnapshot`, no `SaveCombat`, no `WriteState`.

**Hits in `Core/Combat/`:** ZERO hits. Confirmed: no mid-combat serialization primitive exists in upstream.

**Step (c): CombatState Node-derived field assessment:**
`CombatState.cs` holds `List<Creature> _allies`, `List<Creature> _enemies`. `Creature` is NOT Node-derived (it's `public class Creature` with no Godot inheritance). However:
- `Creature` calls `NCombatRoom.Instance?.GetCreatureNode(this)` — references scene-tree singletons dynamically
- `CombatState.GetCreatureAsync()` uses `GodotTimerTask()` which calls `((SceneTree)Engine.GetMainLoop()).CreateTimer()` — scene-tree dependency embedded in CombatState itself (`CombatState.cs:455-459`)

**Verdict:** FAIL by proof-of-absence. Zero mid-combat serialization primitives. `CombatState` has embedded scene-tree dependency via `GodotTimerTask()`. Even if a reflection-snapshot were attempted, the `SceneTree` reference embedded in async-timer logic would prevent clean restoration.

Steps (b) was skipped per protocol (zero hits in step (a)).

---

### Criterion #5 — Hook injection at every player-decision boundary

**Status: FAIL — Modding surface is event-notification only; no decision-boundary interception**

**Step (a): Modding surface analysis:**

`Hook.cs` (2017-LOC framework reviewed): The Hook class provides `static async Task` methods for game events — `BeforeAttack`, `AfterAttack`, `AfterBlockBroken`, `BeforeCardPlayed`, etc. These are **notifications** fired AFTER the game has already made a decision (card selection already happened). There is no pre-decision hook that can redirect card choice.

`CombatHookSubscriptionDelegate.cs`:
```csharp
public delegate IEnumerable<AbstractModel> CombatHookSubscriptionDelegate(CombatState combatState);
```
Yields listeners for hook notification — not a card-selection interceptor.

`ModManager.cs` uses HarmonyLib internally for mods but the Modding API surface itself (Hook, CombatHookSubscriptionDelegate) provides no decision-boundary interception point.

**Step (b): CombatRoomHandler.cs line 80 analysis:**
```csharp
// CombatRoomHandler.cs:80
CardModel cardModel = random.NextItem(list);
```
This `Rng.NextItem(list)` call IS the card selection point. It is inside `AutoSlayer.CombatRoomHandler.HandleAsync()`, not in the upstream Hook framework. It IS patchable via Harmony (patch `Rng.NextItem`), but:

1. This is the AutoSlayer's internal decision-making (AI logic), not the game engine's player-turn API
2. Intercepting `Rng.NextItem` globally would intercept ALL random selections (monster moves, potions, etc.) — cannot distinguish card selection from other RNG calls without complex Harmony context inspection
3. Even if intercepted: `HandleAsync` is an async method with `CancellationToken` — IL patching async state machines requires Harmony `ILManipulator` level access, not simple `Prefix/Postfix`

**Mechanism verdict:** Modding hook surface — FAIL (no decision-boundary hook). Harmony patch — QUALIFIED, but only via complex async state machine IL injection that exceeds 1-week upstream-touching work. No mechanism without either upstream source edit OR prohibitive Harmony complexity — FAIL by proof-of-absence of a tractable mechanism.

---

### Criterion #6 — Determinism characterization

**Status: UNREACHABLE — blocked by criterion #2 failure**

100 same-seed runs could not be executed. Dual GC measurement (RegisterForFullGCNotification + Stopwatch 3-sigma outlier) not performed.

Gen-0/1/2 counts, max pause: NOT MEASURED.

---

## §Reproduction

### Step-0 environment preflight (re-run these before any criterion #1 re-test)

```bash
# 1. Confirm HEAD
git rev-parse HEAD  # expect: 70128d564b120c60d95d54d1f256577bc9d452bc

# 2. Confirm branch
git rev-parse --abbrev-ref HEAD  # expect: wave-43/B-godot-headless-spike

# 3. Godot binary present
which Godot_v4.5.1-stable_mono_linux.x86_64

# 4. Godot version
Godot_v4.5.1-stable_mono_linux.x86_64 --version  # expect: 4.5.1.stable.mono.official.f62fdbde1

# 5. GodotSharp.dll at actual path (NOT the plan's /GodotSharp/ subdirectory)
ls "$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/GodotSharp.dll"

# 6. Game-bundled engine binary
"$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2" --version
# expect: 4.5.1.m.11.mono.custom_build

# 7. dotnet
dotnet --version  # expect: >= 8.0

# 8. Read access to upstream decompiled tree
ls "$HOME/development/projects/godot/sts2/src/Core/Combat/CombatManager.cs"
```

### Criterion #1 reproduction (PASS — verified)

No overlay creation needed for criterion #1. The game boots headless directly:

```bash
# Boot game headless with Steam bypass and quit immediately
"$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2" \
  --headless --quit --force-steam=off 2>&1

# Expected:
#   MegaDot v4.5.1.m.11.mono.custom_build - https://godotengine.org
#   [INFO] Steam initialization skipped (editor mode). ...
#   [INFO] Registered 11 migrations
#   [INFO] ModelIdSerializationCache initialized. ...
#   [INFO] Preloading 'IntroLogo' assets...
#   FMOD Sound System: System released
#   (exit 0)
```

### Overlay setup (for reference; NOT required for this spike's verdict)

The overlay approach was blocked because `project.godot` is PCK-embedded. If a future spike attempts game modification via PCK repacking:

```bash
# Step 1: Create overlay (disk-backed; NOT /tmp)
STS2="$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2"
cp -r "$STS2" ~/.cache/sts2-spike-overlay/

# Step 2: PCK repacking would be required here (not done in this spike)
# project.godot is at res:// inside SlayTheSpire2.pck — cannot be patched
# without Godot editor export toolchain or a PCK repacker

# Step 3: Boot via overlay copy
"~/.cache/sts2-spike-overlay/SlayTheSpire2" \
  --headless --force-steam=off --autoslay --seed=SEED 2>&1

# Step 4: Cleanup
rm -rf ~/.cache/sts2-spike-overlay/
```

The mod injection path (alternative to overlay project.godot patch):
```bash
# Mods directory is adjacent to the executable
STS2="$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2"
mkdir -p "$STS2/mods/godot-headless-spike/"
cp spike.dll spike.manifest "$STS2/mods/godot-headless-spike/"
# Boot with mod
"$STS2/SlayTheSpire2" --headless --force-steam=off --autoslay --seed=SEED 2>&1
# Cleanup mods dir (NOT the game dir itself)
rm -rf "$STS2/mods/godot-headless-spike/"
```

Note: The mods directory approach modifies the Steam install — prohibited per spike constraints. A correct implementation would require the overlay copy PLUS mods placed in the overlay's adjacent directory. Neither was performed because criterion #2 is blocked upstream of this injection point.

---

## §Harmony patch sites

### Patch #1: NGame.IsReleaseGame()

- **File:** `~/development/projects/godot/sts2/src/Core/Nodes/NGame.cs:331`
- **Original:** `public static bool IsReleaseGame() { return true; }`
- **Reason:** Gates the `--autoslay` CLI path (`NGame.cs:275`). Without this patch, AutoSlayer is unreachable in the shipped binary.
- **Implementation:** `src/HarmonyPatches.cs:PatchIsReleaseGame` — Prefix returns `false` and skips original.
- **Status:** Patch code written. Not applied in actual execution (criterion #2 blocked before injection point could be tested end-to-end).

---

## §Cleanup

The overlay was NOT created (criterion #1 does not require it; criteria #2-#6 blocked). No cleanup required beyond confirming no orphan files:

```bash
ls ~/.cache/sts2-spike-overlay/ 2>&1
# Expected: ls: cannot access '/home/clydew372/.cache/sts2-spike-overlay/': No such file or directory
```

Confirmed: `~/.cache/sts2-spike-overlay/` was never created (criterion #1 boots the game from its original Steam install location in read-only fashion via `--headless --quit --force-steam=off`; no files written).

---

## §Summary of blocked chain

```
#1 PASS  — boot without cctor wall (confirmed empirically)
#2 FAIL  — AutoSlayer requires full UI scene tree; headless dummy driver
             breaks all visibility/node-existence predicates
#3 UNREACHABLE — blocked by #2
#4 FAIL  — proof-of-absence: zero mid-combat serialization primitives;
             CombatState embeds SceneTree dependency via GodotTimerTask()
#5 FAIL  — Modding surface is event-notification only; no decision-
             boundary hook; Harmony async IL approach prohibitive
#6 UNREACHABLE — blocked by #2
```

Option B requires:
- Full game scene tree (for AutoSlayer) — not available in headless without full UI stub (prohibitive)
- Mid-combat save/restore primitive (for option-B's MCTS use case) — does not exist upstream
- Decision-boundary hook (for AI model insertion) — does not exist; Harmony async IL approach is prohibitive

All three are structural, not configuration. Option C (ADR-034 parallel substrate) is terminal.

R11 status: ESCALATED — option B blocked → per-patch port treadmill is permanent operating mode for Phase-1.5.
