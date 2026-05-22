# UpstreamDriver Harmony Runtime-Patching

**Audience:** Engineers extending or maintaining `UpstreamDriver.CaptureMidCombat()` (wave-50/A.2 onward).
**Status:** Engineer-facing informational. Wave-50/A.2 introduced; documented at wave-51/B.2.

## What is Harmony

Harmony (`HarmonyLib`) is a runtime IL-patching library for .NET. It rewrites method bodies in loaded assemblies at runtime — letting headless test code shim out Godot-bound methods in upstream code without modifying the upstream source tree.

The Steam install of Slay the Spire 2 ships `0Harmony.dll` (used by the game's own mod-loading system). Q1's UpstreamDriver loads this binary at runtime via `Assembly.LoadFrom` — **no NuGet package dependency, no csproj reference**.

## Why runtime-load (no csproj dependency)

1. Keeps the Q1 substrate's dependency surface minimal. Q1 builds without Steam; only `Sts2Headless.UpstreamCapture` test project needs Harmony, and only at test runtime.
2. Reuses a binary already on disk (Steam install path). Adding HarmonyLib as a NuGet dep would version-conflict with what the game ships.
3. Fail-loud: if `0Harmony.dll` is missing (no Steam install / wrong path), the driver throws immediately rather than silently skipping patches.

## How it integrates with the mock layer

`UpstreamDriver.CaptureMidCombat()` calls `EnsureGodotLoggerSafe()` once per driver lifetime (lock-guarded; idempotent). That method:

1. Locates `0Harmony.dll` at `<SteamDir>/0Harmony.dll` (`SteamDir()` returns the standard Steam install path).
2. Loads it via `Assembly.LoadFrom`.
3. Resolves `HarmonyLib.Harmony` + `HarmonyLib.HarmonyMethod` types via reflection.
4. Applies 5 prefix-patches to Godot-bound upstream methods that would SIGSEGV or NullReferenceException in a headless process (no Godot engine, no `SceneTree`, no `NRun.Instance`).

Patches are applied BEFORE `TurnLoopBootstrap` installs upstream's per-combat singleton state. Patches persist for the lifetime of the test process.

## Patch list (wave-50/A.2)

| # | Target method | Patch effect | Reason |
|---|---|---|---|
| 1 | `Logger.GetIsRunningFromGodotEditor` | Return `false` | Prevents Godot.OS P/Invoke crash on missing Godot runtime |
| 2 | `ThinkCmd.Play` | No-op | `LocManager.Instance` is null headless; method would NRE |
| 3 | `TalkCmd.Play` | No-op | Same as ThinkCmd |
| 4 | `ConsoleLogPrinter.Print` | No-op | Internally calls `GD.Print()` → godotsharp native → SIGSEGV |
| 5 | `LocString.GetFormattedText` | Return `""` | Covers cases like `CalcifiedCultist.IncantationMove` that format localized strings |
| 6 | `CombatManager.CheckWinCondition` | Safe headless variant | `CurrentRoom` null headless; `ProcessPendingLoss` crashes |

(Yes, 6 patches — original wave-50/A.2 commit message said "5"; CheckWinCondition was the 6th added during A.2's mid-flight scope expansion.)

## Fallback if `0Harmony.dll` missing

`EnsureGodotLoggerSafe()` throws `InvalidOperationException` with a diagnostic message:

```
EnsureGodotLoggerSafe: 0Harmony.dll not found at '<SteamDir>/0Harmony.dll'.
Cannot patch Logger.GetIsRunningFromGodotEditor.
```

This blocks any further driver work — fail-loud rather than fail-silent. To resolve: install Steam version of the game OR adjust `SteamDir()` to point at a copy of the install.

## Cross-references

- Wave-50/A.2 commit `5ed1b21` — introduction of Harmony runtime-load + 5 (later 6) patches
- Wave-50 plan — `/home/clydew372/.claude/plans/re-adr-002-falsification-compressed-marble.md` (historical reference; superseded by wave-51 plan)
- Wave-50 close `.claude/state/waves/50.json` — A.2 scope-expansion documented as R17 instance #5
- ADR-035 Amendment #2 (decisions-log §ADR-035) — Phase-1/Phase-2 split context
- `EnsureGodotLoggerSafe()` implementation: `src/UpstreamDriver.cs`
