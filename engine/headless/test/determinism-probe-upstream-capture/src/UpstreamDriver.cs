using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sts2Headless.UpstreamCapture;

/// <summary>
/// Drives upstream <c>MegaCrit.Sts2</c> via reflection: constructs a Silent
/// run state, builds a CombatState with the requested encounter's monsters,
/// runs <c>CombatManager.SetUpCombat</c>, then serializes the post-SetUpCombat
/// snapshot using the same canonical byte format as Q1's
/// <c>StateByteSerializer</c>.
///
/// <para>
/// <b>Why reflection:</b> the upstream <c>sts2.csproj</c> drags in dozens of
/// types and we'd have to track them all in our .csproj if we used compile-time
/// references. Reflection keeps the .csproj surface small (just the assembly
/// references in <see cref="UpstreamCapture"/>'s csproj) at the cost of some
/// type-safety lost at the host level. The Domain stays in Q1, where we DO
/// have compile-time safety against StateByteSerializer.
/// </para>
///
/// <para>
/// <b>Scene-tree safety:</b> all reflection invocations target methods on
/// types reachable from <c>CombatManager.SetUpCombat</c>'s call chain (see
/// Stream-C-T1 inspection report). Trying to touch
/// <c>NRunMusicController.Instance</c>, <c>NCombatRoom.Instance</c>,
/// <c>NModalContainer.Instance</c>, <c>NCombatStartBanner</c>,
/// <c>Cmd.CustomScaledWait</c>, <c>SaveManager.Instance</c>,
/// <c>RunManager.Instance.ActionExecutor</c> would cause a
/// <see cref="TypeLoadException"/> or NRE the first time we tried to invoke
/// any. We intentionally never look any of those up.
/// </para>
/// </summary>
public sealed class UpstreamDriver
{
    private readonly Assembly _sts2;

    // ===== Godot-Logger safety patch (wave-50/A.2) ========================
    // The upstream Logger class triggers Godot.OS.GetCmdlineArgs() in its
    // static initializer, which P/Invokes into the native Godot runtime —
    // crashing headless with SIGSEGV. We use Harmony 2.x (bundled in the
    // Steam dir) to prefix-patch Logger.GetIsRunningFromGodotEditor() to
    // return false immediately, preventing the Godot.OS access.
    //
    // This must be applied before ANY upstream object that creates a Logger
    // in its constructor (ActionQueueSet, ActionExecutor, ChecksumTracker,
    // ActionQueueSynchronizer, CombatStateSynchronizer, PeerInputSynchronizer).
    // All of these are constructed by RunManager.SetUpTest(), which is called
    // by TurnLoopBootstrap (wave-50/A.1).
    private static bool _godotLoggerSafetyApplied;
    private static readonly object _godotLoggerSafetyLock = new object();

    /// <summary>
    /// Harmony-patch <c>Logger.GetIsRunningFromGodotEditor()</c> to return
    /// <c>false</c> without invoking <c>Godot.OS.GetCmdlineArgs()</c>.
    /// Safe to call multiple times (idempotent; patches only once per AppDomain).
    /// </summary>
    private void EnsureGodotLoggerSafe()
    {
        lock (_godotLoggerSafetyLock)
        {
            if (_godotLoggerSafetyApplied)
                return;
            _godotLoggerSafetyApplied = true;
        }

        string harmonyPath = Path.Combine(SteamDir(), "0Harmony.dll");
        if (!File.Exists(harmonyPath))
        {
            throw new InvalidOperationException(
                $"EnsureGodotLoggerSafe: 0Harmony.dll not found at '{harmonyPath}'. "
                + "Cannot patch Logger.GetIsRunningFromGodotEditor."
            );
        }

        Assembly harmonyAsm = Assembly.LoadFrom(harmonyPath);
        Type harmonyType = harmonyAsm.GetType("HarmonyLib.Harmony")
            ?? throw new InvalidOperationException("HarmonyLib.Harmony not found in 0Harmony.dll.");
        Type harmonyMethodType = harmonyAsm.GetType("HarmonyLib.HarmonyMethod")
            ?? throw new InvalidOperationException("HarmonyLib.HarmonyMethod not found in 0Harmony.dll.");

        // Harmony instance (id = arbitrary stable name).
        object harmonyInst = Activator.CreateInstance(harmonyType, "sts2headless.loggersafe")!;

        // Target method: Logger.GetIsRunningFromGodotEditor (private static).
        Type loggerType = TypeOrThrow("MegaCrit.Sts2.Core.Logging.Logger");
        MethodBase targetMethod = loggerType.GetMethod(
            "GetIsRunningFromGodotEditor",
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "Logger.GetIsRunningFromGodotEditor not found in sts2.dll."
        );

        // Prefix method: GetIsRunningFromGodotEditor_Prefix (private static on UpstreamDriver).
        MethodInfo prefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(GetIsRunningFromGodotEditor_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.GetIsRunningFromGodotEditor_Prefix not found (reflection)."
        );

        // Wrap prefix in HarmonyMethod.
        object harmonyPrefix = Activator.CreateInstance(harmonyMethodType, prefixMethod)!;

        // Patch: prefix only (postfix, transpiler, finalizer = null).
        MethodInfo patchMi = harmonyType.GetMethod(
            "Patch",
            new Type[]
            {
                typeof(MethodBase),
                harmonyMethodType,
                harmonyMethodType,
                harmonyMethodType,
                harmonyMethodType,
            }
        ) ?? throw new InvalidOperationException("HarmonyLib.Harmony.Patch(5 params) not found.");
        patchMi.Invoke(
            harmonyInst,
            new object?[] { targetMethod, harmonyPrefix, null, null, null }
        );

        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: Logger.GetIsRunningFromGodotEditor patched via Harmony."
        );

        // (2) Patch ThinkCmd.Play to be a headless no-op.
        // ThinkCmd.Play calls LocString.GetFormattedText() → LocManager.Instance.SmartFormat()
        // but LocManager.Instance is null headless → NullReferenceException during
        // CardPileCmd.CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot (called by
        // SetupPlayerTurn → CardPileCmd.Draw at every StartTurn).
        // ThinkCmd.Play is purely visual (thought-bubble VFX); skipping it is safe headless.
        Type thinkCmdType = TypeOrThrow("MegaCrit.Sts2.Core.Commands.ThinkCmd");
        MethodBase thinkCmdPlay = thinkCmdType.GetMethod(
            "Play",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException("ThinkCmd.Play not found in sts2.dll.");

        MethodInfo thinkCmdPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(ThinkCmd_Play_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.ThinkCmd_Play_Prefix not found (reflection)."
        );
        object thinkCmdPrefix = Activator.CreateInstance(harmonyMethodType, thinkCmdPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { thinkCmdPlay, thinkCmdPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: ThinkCmd.Play patched (no-op headless) via Harmony."
        );

        // (2b) Patch TalkCmd.Play to be a headless no-op.
        // TalkCmd.Play (used by CalcifiedCultist.IncantationMove etc.) accesses
        // SaveManager.Instance.PrefsSave (null headless) after the LocString call.
        // The entire method is visual (speech bubble VFX); skipping it is safe.
        Type talkCmdType = TypeOrThrow("MegaCrit.Sts2.Core.Commands.TalkCmd");
        MethodBase talkCmdPlay = talkCmdType.GetMethod(
            "Play",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException("TalkCmd.Play not found in sts2.dll.");

        MethodInfo talkCmdPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(TalkCmd_Play_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.TalkCmd_Play_Prefix not found (reflection)."
        );
        object talkCmdPrefix = Activator.CreateInstance(harmonyMethodType, talkCmdPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { talkCmdPlay, talkCmdPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: TalkCmd.Play patched (no-op headless) via Harmony."
        );

        // (3) Patch ConsoleLogPrinter.Print to be a headless no-op.
        // After the Logger patch in (1): _isRunningFromGodotEditor=false →
        // _logPrinter = new ConsoleLogPrinter(). ConsoleLogPrinter.Print calls
        // GD.Print() → godotsharp_string_new_with_utf16_chars P/Invoke → SIGSEGV
        // (MonsterModel.PerformMove, etc. log at LogLevel.Info during enemy turns).
        // Suppressing ConsoleLogPrinter.Print is safe: all output is debug/perf
        // logging; no correctness-affecting side effects.
        Type consolePrinterType = TypeOrThrow(
            "MegaCrit.Sts2.Core.Logging.ConsoleLogPrinter"
        );
        MethodBase consolePrinterPrint = consolePrinterType.GetMethod(
            "Print",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("ConsoleLogPrinter.Print not found in sts2.dll.");

        MethodInfo consolePrinterPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(ConsoleLogPrinter_Print_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.ConsoleLogPrinter_Print_Prefix not found (reflection)."
        );
        object consolePrinterPrefix = Activator.CreateInstance(harmonyMethodType, consolePrinterPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { consolePrinterPrint, consolePrinterPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: ConsoleLogPrinter.Print patched (no-op headless) via Harmony."
        );

        // (4) Patch LocString.GetFormattedText() to return "" headless.
        // LocManager.Instance is null headless — any Cmd class (ThinkCmd, TalkCmd, CardCmd,
        // etc.) that calls locString.GetFormattedText() before LocManager is initialized
        // will throw NRE. Returning "" is safe: result is only ever used for visual display
        // (speech bubbles, thought bubbles) which are no-ops headless anyway.
        Type locStringType = TypeOrThrow("MegaCrit.Sts2.Core.Localization.LocString");
        MethodBase locStringGetFormattedText = locStringType.GetMethod(
            "GetFormattedText",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("LocString.GetFormattedText not found in sts2.dll.");

        MethodInfo locStringPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(LocString_GetFormattedText_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.LocString_GetFormattedText_Prefix not found (reflection)."
        );
        object locStringPrefix = Activator.CreateInstance(harmonyMethodType, locStringPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { locStringGetFormattedText, locStringPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: LocString.GetFormattedText patched (→ \"\") via Harmony."
        );

        // (5) Patch CombatManager.EndCombatInternal to set IsInProgress=false + return.
        // When all enemies die during card-play (e.g. NibbitsWeak kill on turn 3),
        // CheckWinCondition → EndCombatInternal → (CombatRoom)runState.CurrentRoom → NRE
        // (CurrentRoom is null headless; no map navigation setup).
        // We only need IsInProgress=false so the turn loop exits cleanly; all other
        // post-combat bookkeeping (save, achievements, VFX) is headless-unsafe.
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        MethodBase endCombatInternalMethod = combatManagerType.GetMethod(
            "EndCombatInternal",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("CombatManager.EndCombatInternal not found in sts2.dll.");

        MethodInfo endCombatPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(CombatManager_EndCombatInternal_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.CombatManager_EndCombatInternal_Prefix not found (reflection)."
        );
        object endCombatPrefix = Activator.CreateInstance(harmonyMethodType, endCombatPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { endCombatInternalMethod, endCombatPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: CombatManager.EndCombatInternal patched (→ IsInProgress=false) via Harmony."
        );

        // (6) Patch CombatManager.CheckWinCondition to be headless-safe.
        // CheckWinCondition calls ProcessPendingLoss() (→ CombatEnded?.Invoke(room) where
        // room may be null headless) or IsEnding (→ Hook chain) then EndCombatInternal.
        // Our prefix simply: if pendingLoss != null OR isEnding → set IsInProgress=false
        // and return Task<bool>(true), skipping the original. This prevents NREs in
        // ProcessPendingLoss, CombatEnded event handlers, and EndCombatInternal call chains.
        MethodBase checkWinConditionMethod = combatManagerType.GetMethod(
            "CheckWinCondition",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("CombatManager.CheckWinCondition not found in sts2.dll.");

        MethodInfo checkWinConditionPrefixMethod = typeof(UpstreamDriver).GetMethod(
            nameof(CombatManager_CheckWinCondition_Prefix),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "UpstreamDriver.CombatManager_CheckWinCondition_Prefix not found (reflection)."
        );
        object checkWinConditionPrefix = Activator.CreateInstance(harmonyMethodType, checkWinConditionPrefixMethod)!;
        patchMi.Invoke(
            harmonyInst,
            new object?[] { checkWinConditionMethod, checkWinConditionPrefix, null, null, null }
        );
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: CombatManager.CheckWinCondition patched (headless-safe) via Harmony."
        );

        // (7) Patch NCombatRoom.get_Instance to return null safely — allowing callers
        // to use null-conditional operators without crashing. Monsters like Crusher
        // access NCombatRoom.Instance.Background (no null check!) to drive boss VFX.
        // We patch the Crusher.get_Background private property getter to return null,
        // then patch each Background-accessing move to skip VFX but run game logic.
        // Simpler: patch NCombatRoom.get_Instance to return the existing null value
        // (no-op since it already returns null) is insufficient. Instead we patch
        // Crusher's private Background getter to short-circuit to null return,
        // and patch the Background-using methods to guard against null Background.
        // Since NKaiserCrabBossBackground.PlayAttackAnim is an upstream type, we
        // patch it to be a no-op Task-returning method.
        //
        // Simpler still: just skip all NKaiserCrabBossBackground method calls by
        // patching the Background property getter itself to return a stub.
        // We cannot easily create a Godot Node stub. So instead, patch each
        // async move method of Crusher (ThrashMove, BugStingMove, etc.) to run
        // only the game-logic parts (DamageCmd, PowerCmd) and skip VFX calls.
        //
        // Simplest viable: catch InvokeAsyncMethod exceptions during enemy turns
        // and continue — already done in PlayActionsUpstream for card play.
        // For enemy turns (ExecuteEnemyTurn), the CaptureMidCombat already wraps
        // the whole turn loop in a try/catch that writes .error and aborts the seed.
        //
        // Approach: make CaptureMidCombat tolerate enemy-turn InvokeAsyncMethod
        // failure by catching per-enemy-turn exceptions and taking a snapshot of
        // what combat state is available at that point. This requires a code change
        // in CaptureMidCombat rather than a Harmony patch.
        //
        // For now, KaiserCrabBoss skips — tracked. All other encounters succeed.
        Console.Error.WriteLine(
            "info: EnsureGodotLoggerSafe: KaiserCrabBoss enemy-turn VFX crash — not patched in this pass (see CaptureMidCombat graceful-enemy-turn handling)."
        );
    }

    /// <summary>
    /// Harmony prefix for <c>Logger.GetIsRunningFromGodotEditor()</c>.
    /// Sets <c>__result = false</c> and returns <c>false</c> to skip the
    /// original (preventing the <c>Godot.OS.GetCmdlineArgs()</c> native call).
    /// </summary>
#pragma warning disable IDE0051 // All methods below referenced via reflection by Harmony
    private static bool GetIsRunningFromGodotEditor_Prefix(ref bool __result)
    {
        __result = false;
        return false; // skip original
    }

    /// <summary>
    /// Harmony prefix for <c>ThinkCmd.Play</c>.
    /// Returns <c>false</c> to skip the original (headless no-op: no VFX, no LocManager).
    /// </summary>
    private static bool ThinkCmd_Play_Prefix()
    {
        return false; // skip original: thought-bubble VFX is headless-unsafe
    }

    /// <summary>
    /// Harmony prefix for <c>TalkCmd.Play</c>.
    /// Returns <c>false</c> to skip the original. TalkCmd.Play accesses
    /// <c>SaveManager.Instance.PrefsSave</c> (null headless) for timing calculations,
    /// and creates speech-bubble VFX — both are headless-unsafe.
    /// </summary>
    private static bool TalkCmd_Play_Prefix()
    {
        return false; // skip original: speech-bubble VFX + SaveManager access headless-unsafe
    }

    /// <summary>
    /// Harmony prefix for <c>ConsoleLogPrinter.Print</c>.
    /// Returns <c>false</c> to suppress <c>GD.Print()</c> → Godot native P/Invoke → SIGSEGV.
    /// Upstream combat logs (MonsterModel.PerformMove, etc.) are debug/perf only;
    /// suppressing them has no effect on combat-state correctness.
    /// </summary>
    private static bool ConsoleLogPrinter_Print_Prefix()
    {
        return false; // skip original: GD.Print() → godotsharp native → SIGSEGV headless
    }

    /// <summary>
    /// Harmony prefix for <c>LocString.GetFormattedText()</c>.
    /// <c>LocManager.Instance</c> is null headless — any call to <c>GetFormattedText()</c>
    /// (ThinkCmd, TalkCmd, CalcifiedCultist.IncantationMove, etc.) throws NRE.
    /// We short-circuit to return "" — safe because the result is only used for
    /// visual display (speech/thought bubbles), which are no-ops headless.
    /// </summary>
    private static bool LocString_GetFormattedText_Prefix(ref string __result)
    {
        __result = string.Empty;
        return false; // skip original
    }

    /// <summary>
    /// Harmony prefix for <c>CombatManager.EndCombatInternal()</c>.
    /// Sets <c>IsInProgress = false</c> via reflection then returns <c>false</c>
    /// to skip the original. The original calls <c>runState.CurrentRoom</c> (null headless),
    /// <c>SaveManager</c>, <c>NMapScreen</c>, <c>AchievementsHelper</c>, etc. — all
    /// Godot-node-tree paths that crash or NRE headless. We only need <c>IsInProgress=false</c>
    /// so the turn loop exits cleanly when all enemies die mid-combat.
    /// </summary>
    private static bool CombatManager_EndCombatInternal_Prefix(object __instance)
    {
        // Set IsInProgress = false via auto-property setter (public).
        __instance.GetType()
            .GetProperty("IsInProgress", BindingFlags.Public | BindingFlags.Instance)
            ?.SetValue(__instance, false);
        return false; // skip original
    }

    /// <summary>
    /// Harmony prefix for <c>CombatManager.CheckWinCondition()</c>.
    /// <para>
    /// The original calls <c>ProcessPendingLoss()</c> (fires <c>CombatEnded</c> event
    /// which accesses <c>CurrentRoom</c> = null headless) or <c>EndCombatInternal()</c>
    /// (patched via (5) above but async-method Harmony interaction is fragile).
    /// Our prefix: if a pending-loss or ending condition is detected, set
    /// <c>IsInProgress=false</c> and short-circuit with a completed Task(true).
    /// </para>
    /// </summary>
    private static bool CombatManager_CheckWinCondition_Prefix(
        object __instance,
        ref System.Threading.Tasks.Task<bool> __result
    )
    {
        // Check _pendingLoss != null (field, private).
        System.Reflection.FieldInfo? pendingLossField = __instance.GetType()
            .GetField("_pendingLoss", BindingFlags.NonPublic | BindingFlags.Instance);
        bool hasPendingLoss = pendingLossField?.GetValue(__instance) is not null;

        // Check IsEnding property (public).
        bool isEnding = (bool?)__instance.GetType()
            .GetProperty("IsEnding", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(__instance) == true;

        if (hasPendingLoss || isEnding)
        {
            // Clear pendingLoss + set IsInProgress=false headless-safely.
            pendingLossField?.SetValue(__instance, null);
            __instance.GetType()
                .GetProperty("IsInProgress", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(__instance, false);
            __result = System.Threading.Tasks.Task.FromResult(true);
            return false; // skip original
        }
        return true; // run original (combat is still in progress, no win/loss)
    }
#pragma warning restore IDE0051

    // ===== Exposed API ====================================================

    /// <summary>Exposed for diagnose-mode in <see cref="Program"/>.</summary>
    public Assembly Sts2Assembly => _sts2;

    /// <summary>
    /// Reflectively clear <c>CombatManager.Instance._state</c> so a subsequent
    /// <c>SetUpCombat</c> call doesn't throw "Make sure to reset the combat
    /// before setting up a new one". This is the minimum we need for batch
    /// mode — upstream's full <c>Reset(bool)</c> calls
    /// <c>RunManager.Instance.ActionQueueSynchronizer</c> (scene-tree gated)
    /// which we cannot satisfy headless. We only touch the one field that
    /// blocks the next <c>SetUpCombat</c>.
    /// </summary>
    public void ResetCombatManagerState()
    {
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        object instance =
            combatManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)
            ?? throw new InvalidOperationException("CombatManager.Instance returned null.");
        FieldInfo stateField =
            combatManagerType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CombatManager._state field not found.");
        stateField.SetValue(instance, null);
    }

    /// <summary>Load the upstream sts2 assembly. Throws if it can't be found.</summary>
    public UpstreamDriver()
    {
        // Hook AssemblyResolve so the runtime can find GodotSharp.dll, 0Harmony.dll,
        // and the other upstream-bundled DLLs when sts2.dll references them.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSteamDir;
        // Force-load sts2.dll. The MSBuild reference makes it available on
        // probing path; AppDomain.GetAssemblies() may not yet contain it.
        _sts2 = LoadSts2Assembly();
    }

    /// <summary>
    /// Resolve referenced upstream assemblies from the Steam install directory.
    /// </summary>
    private static Assembly? ResolveFromSteamDir(object? sender, ResolveEventArgs args)
    {
        string steamDir = SteamDir();
        string asmFile = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(steamDir, asmFile);
        if (File.Exists(path))
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static string SteamDir() =>
        Environment.GetEnvironmentVariable("STEAM_STS2_DIR")
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("HOME") ?? "",
            "snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64"
        );

    private static Assembly LoadSts2Assembly()
    {
        // Try AppDomain first.
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(a.GetName().Name, "sts2", StringComparison.Ordinal))
            {
                return a;
            }
        }
        // Try loading by name.
        try
        {
            return Assembly.Load("sts2");
        }
        catch
        {
            // Try by path next to this binary.
            string? thisDir = Path.GetDirectoryName(typeof(UpstreamDriver).Assembly.Location);
            string steamDir = SteamDir();
            string sts2Path = Path.Combine(steamDir, "sts2.dll");
            if (File.Exists(sts2Path))
            {
                return Assembly.LoadFile(sts2Path);
            }
            if (thisDir is not null)
            {
                string colocated = Path.Combine(thisDir, "sts2.dll");
                if (File.Exists(colocated))
                {
                    return Assembly.LoadFile(colocated);
                }
            }
            throw new FileNotFoundException(
                $"Could not locate upstream sts2.dll. Set STEAM_STS2_DIR or place sts2.dll next to UpstreamCapture binary. Tried: {sts2Path}."
            );
        }
    }

    /// <summary>
    /// Capture canonical bytes for the (seed, encounter) tuple. Drives
    /// CombatManager.SetUpCombat then serializes the result.
    /// </summary>
    public byte[] Capture(int seed, EncounterCatalog.EncounterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (
            plan.Kind != EncounterCatalog.PlanKind.UpstreamComparable
            && plan.Kind != EncounterCatalog.PlanKind.UpstreamEncounterRng
        )
        {
            throw new InvalidOperationException(
                $"Capture() called for non-comparable encounter '{plan.EncounterId}'; "
                    + $"caller should have routed to MissingUpstream path. Reason: {plan.Reason}."
            );
        }

        // --- 0a. Turn on upstream's TestMode. This switches several singletons
        // (SaveManager, GodotFileIo) to mock variants that don't require the
        // Godot scene tree.
        Type testModeType = TypeOrThrow("MegaCrit.Sts2.Core.TestSupport.TestMode");
        testModeType
            .GetProperty("IsOn", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, true);
        // Inject a fake SaveManager instance (private static field _mockInstance)
        // so Player ctor's `SaveManager.Instance?.Progress.GetStatsForCharacter`
        // line short-circuits to null without trying to construct the real one
        // (which would call Steam / Godot natives that segfault outside game
        // runtime).
        Type saveManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.SaveManager");
        FieldInfo mockField =
            saveManagerType.GetField("_mockInstance", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SaveManager._mockInstance field not found.");
        // Build a SaveManager via uninitialized object, then poke its
        // _progressSaveManager field with a ProgressSaveManager whose
        // Progress is the default ProgressState. Player ctor reads
        // `SaveManager.Instance?.Progress.GetStatsForCharacter(...)` which
        // chains through Progress; GetStatsForCharacter returns
        // CharacterStats? so null is acceptable downstream.
        object fakeSaveManager =
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(saveManagerType);
        Type progressSaveManagerType = TypeOrThrow(
            "MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager"
        );
        object fakeProgressSaveManager =
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                progressSaveManagerType
            );
        // Set ProgressSaveManager.Progress backing field to default ProgressState.
        Type progressStateType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.ProgressState");
        object defaultProgressState = progressStateType
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        FieldInfo progressBackingField =
            progressSaveManagerType.GetField(
                "<Progress>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException(
                "ProgressSaveManager <Progress>k__BackingField not found."
            );
        progressBackingField.SetValue(fakeProgressSaveManager, defaultProgressState);
        // Plug into SaveManager._progressSaveManager.
        FieldInfo psmField =
            saveManagerType.GetField(
                "_progressSaveManager",
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException(
                "SaveManager._progressSaveManager field not found."
            );
        psmField.SetValue(fakeSaveManager, fakeProgressSaveManager);
        mockField.SetValue(null, fakeSaveManager);

        // --- 0. Bootstrap upstream's ModelDb. Without this, UnlockState.cctor
        // (and a hundred other type-inits) try to look up models by id from an
        // empty dictionary and throw. ModelDb.Init() instantiates every
        // AbstractModel subtype by reflection — it's the same call upstream
        // makes during game startup.
        EnsureModelDbInitialized();

        // --- 1. Build run state with Silent character ----------------------
        // Use the same stringSeed format Q1 uses in Host/FileProbeStream.cs:
        // $"seed-{int}". This ensures both sides feed the same uint hash into
        // RunRngSet, keeping the RNG sequence aligned.
        string stringSeed = $"seed-{seed}";

        // We need a Player but want to bypass UnlockState's static cctor
        // (which iterates ModelDb.AllEncounters and crashes when any of the
        // 90+ encounter classes isn't registered). Construct Player directly
        // via the private 10-arg ctor with a fresh UnlockState built the same
        // way — only ever touching its instance constructor, never its type.
        Type playerType = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Players.Player");
        Type unlockStateType = TypeOrThrow("MegaCrit.Sts2.Core.Unlocks.UnlockState");
        Type silentType = TypeOrThrow("MegaCrit.Sts2.Core.Models.Characters.Silent");
        Type modelIdType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelId");
        Type characterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.CharacterModel");
        Type modelDbType_local = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        // The UnlockState ctor itself calls ModelDb.AllEncounters via the
        // encountersSeen hashset population. To dodge that, we BYPASS the ctor
        // using FormatterServices.GetUninitializedObject and then poke the
        // private fields to plausible empty defaults.
        object unlockStateInstance = MakeUninitializedUnlockState(unlockStateType, modelIdType);

        // Reset cached RNGs etc. that may have been left over from a previous
        // capture in the same process. Currently we run one capture per
        // process (the host CLI), so no reset is required, but if we ever
        // batch we'd need to reset CombatManager + NetCombatCardDb here.

        MethodInfo modelDbCharacterGeneric =
            modelDbType_local.GetMethod("Character", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Character not found.");
        object silentCharacter =
            modelDbCharacterGeneric.MakeGenericMethod(silentType).Invoke(null, null)
            ?? throw new InvalidOperationException("ModelDb.Character<Silent> returned null.");

        // Player has a private 10-arg ctor:
        //   Player(CharacterModel, ulong netId, int currentHp, int maxHp,
        //          int maxEnergy, int gold, int potionSlotCount, int orbSlotCount,
        //          RelicGrabBag, UnlockState, ...optional discovered lists)
        // We invoke it directly with Silent's defaults.
        int startingHp = ToInt(
            characterModelType.GetProperty("StartingHp")!.GetValue(silentCharacter)!
        );
        int maxEnergy = ToInt(
            characterModelType.GetProperty("MaxEnergy")!.GetValue(silentCharacter)!
        );
        int startingGold = ToInt(
            characterModelType.GetProperty("StartingGold")!.GetValue(silentCharacter)!
        );
        int orbSlotCount = ToInt(
            characterModelType.GetProperty("BaseOrbSlotCount")!.GetValue(silentCharacter)!
        );
        Type relicGrabBagType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RelicGrabBag");
        object relicGrabBag =
            Activator.CreateInstance(
                relicGrabBagType,
                args: new object[]
                { /* refreshAllowed: */
                    false,
                }
            ) ?? throw new InvalidOperationException("RelicGrabBag ctor returned null.");

        ConstructorInfo? playerCtor = playerType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 15);
        if (playerCtor is null)
        {
            throw new InvalidOperationException(
                $"Player ctor with 15 params not found. Found these ctors: "
                    + string.Join(
                        "; ",
                        playerType
                            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                            .Select(c => $"({c.GetParameters().Length} params)")
                    )
            );
        }
        // Match the upstream Player ctor signature exactly:
        //   (CharacterModel, ulong, int, int, int, int, int, int, RelicGrabBag,
        //    UnlockState, List<ModelId>?, List<ModelId>?, List<string>?,
        //    List<ModelId>?, List<ModelId>?)
        object player = playerCtor.Invoke(
            new object?[]
            {
                silentCharacter, /* netId */
                1uL,
                /* currentHp */startingHp, /* maxHp */
                startingHp,
                /* maxEnergy */maxEnergy, /* gold */
                startingGold,
                /* potionSlotCount */3, /* orbSlotCount */
                orbSlotCount,
                relicGrabBag,
                unlockStateInstance,
                /* discoveredCards */null, /* discoveredEnemies */
                null,
                /* discoveredEpochs */null, /* discoveredPotions */
                null,
                /* discoveredRelics */null,
            }
        );
        // Populate the player's starting inventory (mimic CreateForNewRun
        // which calls player.PopulateStartingInventory() after the ctor).
        MethodInfo populateInventoryMi =
            playerType.GetMethod(
                "PopulateStartingInventory",
                BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new InvalidOperationException("PopulateStartingInventory not found.");
        populateInventoryMi.Invoke(player, null);

        // RunState.CreateForNewRun(players, ActModel.GetDefaultList(), [], GameMode.Standard, 0, seed)
        Type runStateType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RunState");
        Type actModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ActModel");
        Type modifierModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModifierModel");
        Type gameModeType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.GameMode");
        object gameModeStandard = Enum.Parse(gameModeType, "Standard");
        object canonicalActs =
            actModelType
                .GetMethod("GetDefaultList", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, null)
            ?? throw new InvalidOperationException("ActModel.GetDefaultList returned null.");
        // RunState.CreateForNewRun asserts each act is mutable. Convert.
        MethodInfo actToMutableMi =
            actModelType.GetMethod("ToMutable", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ActModel.ToMutable not found.");
        var actList = (System.Collections.IEnumerable)canonicalActs;
        Type listOfAct = typeof(List<>).MakeGenericType(actModelType);
        object mutableActs = Activator.CreateInstance(listOfAct)!;
        MethodInfo addActMi = listOfAct.GetMethod("Add")!;
        foreach (object a in actList)
        {
            object mutA = actToMutableMi.Invoke(a, null)!;
            addActMi.Invoke(mutableActs, new[] { mutA });
        }
        object acts = mutableActs;
        object emptyModifiers = Array.CreateInstance(modifierModelType, 0);

        // Convert player to IReadOnlyList<Player>.
        Type readOnlyListOfPlayer = typeof(IReadOnlyList<>).MakeGenericType(playerType);
        object players = MakeReadOnlyList(playerType, new[] { player });
        Type readOnlyListOfAct = typeof(IReadOnlyList<>).MakeGenericType(actModelType);
        Type readOnlyListOfModifier = typeof(IReadOnlyList<>).MakeGenericType(modifierModelType);
        // acts is already IReadOnlyList<ActModel> per GetDefaultList signature.

        MethodInfo createRunFromNew =
            runStateType.GetMethod("CreateForNewRun", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("RunState.CreateForNewRun not found.");
        object runState =
            createRunFromNew.Invoke(
                null,
                new object[]
                {
                    players,
                    acts,
                    emptyModifiers,
                    gameModeStandard, /* ascensionLevel */
                    0,
                    stringSeed,
                }
            ) ?? throw new InvalidOperationException("RunState.CreateForNewRun returned null.");

        // --- 2. Resolve the encounter list (Q1 invented encounters; we drive
        // by direct monster instantiation rather than EncounterModel.ToMutable
        // because Q1's encounter ids don't map 1:1 to upstream encounter
        // classes — see EncounterCatalog).
        Type combatStateType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatState");
        Type combatSideType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatSide");
        object combatSideEnemy = Enum.Parse(combatSideType, "Enemy");

        // Find IRunState and the modifiers/multiplayer fields.
        object multiplayerScalingModel = runStateType
            .GetProperty("MultiplayerScalingModel")!
            .GetValue(runState)!;
        object modifiers = runStateType.GetProperty("Modifiers")!.GetValue(runState)!;

        // new CombatState(encounter: null, runState, modifiers, multiplayerScalingModel,
        //                 [badgeModels])
        // v0.103.2 ctor: 4 params. v0.105.1 ctor: 5 params (adds IReadOnlyList<BadgeModel>
        // badgeModels between modifiers and multiplayerScalingModel). We resolve by
        // named-parameter SUBSET so the same code drives both DLLs; extras are sourced
        // from RunState properties (or filled with flex defaults).
        // (Encounter is optional, used only by certain hooks. We pass null and
        // let SetUpCombat call AddCreature for our pre-built monster list.)
        var combatStateNamedArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["encounter"] = null,
            ["runState"] = runState,
            ["modifiers"] = modifiers,
            ["multiplayerScalingModel"] = multiplayerScalingModel,
        };
        // v0.105.1 introduced badgeModels: forward the RunState.BadgeModels property
        // if it exists. ReflectionFlex.GetOptionalProperty returns null if absent
        // (v0.103.2), in which case the flex-resolver fills an empty list.
        object? badgeModels = ReflectionFlex.TryGetProperty(runState, "BadgeModels");
        if (badgeModels is not null)
        {
            combatStateNamedArgs["badgeModels"] = badgeModels;
        }
        (ConstructorInfo combatStateCtor, object?[] combatStateArgs) =
            ReflectionFlex.FindCtorByParameterNames(
                combatStateType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                combatStateNamedArgs,
                requiredNames: new[]
                {
                    "encounter",
                    "runState",
                    "modifiers",
                    "multiplayerScalingModel",
                }
            );
        object combatState = combatStateCtor.Invoke(combatStateArgs);

        // state.AddPlayer(player) — registers player.Creature in _allies.
        combatStateType
            .GetMethod("AddPlayer", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(combatState, new object[] { player });

        // --- 3. Spawn enemies via CombatState.CreateCreature.
        // For UpstreamEncounterRng plans (slimes), drive the actual upstream encounter
        // class's GenerateMonstersWithSlots to get seed-accurate monster+slot pairs.
        // For UpstreamComparable plans, instantiate each monster class directly.
        Type monsterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.MonsterModel");
        Type modelDbType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        MethodInfo modelDbMonsterGeneric =
            modelDbType.GetMethod("Monster", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Monster not found.");

        // Resolve the (monsterModel, slot) pairs — either statically or via upstream encounter RNG.
        List<(object mutableMonster, string? slot)> spawnList;
        if (plan.Kind == EncounterCatalog.PlanKind.UpstreamEncounterRng)
        {
            spawnList = ResolveViaUpstreamEncounterRng(
                plan,
                runState,
                modelDbType,
                modelDbMonsterGeneric,
                monsterModelType
            );
        }
        else
        {
            spawnList = new List<(object, string?)>();
            for (int mi = 0; mi < plan.MonsterIds.Count; mi++)
            {
                string monsterId = plan.MonsterIds[mi];
                string? slot = plan.Slots[mi];
                Type monsterClassType = TypeOrThrow(
                    $"MegaCrit.Sts2.Core.Models.Monsters.{monsterId}"
                );
                MethodInfo modelDbMonster = modelDbMonsterGeneric.MakeGenericMethod(
                    monsterClassType
                );
                object canonicalMonster =
                    modelDbMonster.Invoke(null, null)
                    ?? throw new InvalidOperationException(
                        $"ModelDb.Monster<{monsterId}> returned null."
                    );
                object mutableMonster =
                    monsterModelType
                        .GetMethod("ToMutable", BindingFlags.Public | BindingFlags.Instance)!
                        .Invoke(canonicalMonster, null)
                    ?? throw new InvalidOperationException($"{monsterId}.ToMutable returned null.");
                spawnList.Add((mutableMonster, slot));
            }
        }

        var monsters = new List<object>();
        MethodInfo createCreatureMi =
            combatStateType.GetMethod("CreateCreature", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CombatState.CreateCreature not found.");
        foreach ((object mutableMonster, string? slot) in spawnList)
        {
            monsters.Add(mutableMonster);
            object creature =
                createCreatureMi.Invoke(
                    combatState,
                    new object?[] { mutableMonster, combatSideEnemy, slot }
                ) ?? throw new InvalidOperationException("CreateCreature returned null.");
            combatStateType
                .GetMethod("AddCreature", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(combatState, new object[] { creature });
        }

        // --- 4. Replicate CombatManager.SetUpCombat body (sans NetCombatCardDb) --
        // Upstream v0.105.1 added Log.LogMessage calls inside
        // NetCombatCardDb.IdCardIfNecessary which trigger the Log/Logger/Godot.OS
        // cctor chain → P/Invoke into uninitialized GDExtension function-pointer
        // table → SIGSEGV. We replicate the 6 logical steps of SetUpCombat via
        // reflection, skipping only the NetCombatCardDb.Instance.StartCombat call.
        // See tools/upstream-sync/docs/wave-6-sigsegv-spike-report.md §Primary.
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        object combatManagerInstance =
            combatManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)
            ?? throw new InvalidOperationException("CombatManager.Instance returned null.");

        // L195: CombatManager._state = state
        combatManagerType
            .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(combatManagerInstance, combatState);

        // L196: _state.MultiplayerScalingModel?.OnCombatEntered(_state)
        object? multiplayerScaling = ReflectionFlex.TryGetProperty(
            combatState,
            "MultiplayerScalingModel"
        );
        if (multiplayerScaling is not null)
        {
            multiplayerScaling
                .GetType()
                .GetMethod("OnCombatEntered", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(multiplayerScaling, new object[] { combatState });
        }

        // L197: StateTracker.SetState(state)
        object stateTracker = combatManagerType
            .GetProperty("StateTracker", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatManagerInstance)!;
        stateTracker
            .GetType()
            .GetMethod("SetState", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(stateTracker, new object[] { combatState });

        // L198-201: _playerReadyLock.EnterScope(); _playersTakingExtraTurn.Clear()
        // SKIP — fresh CombatManager instance; list is already empty; no observable state.

        // L202-205: foreach player: player.ResetCombatState()
        // L206-209: foreach player: player.PopulateCombatState(player.RunState.Rng.Shuffle, state)
        MethodInfo resetMi = playerType.GetMethod(
            "ResetCombatState",
            BindingFlags.Public | BindingFlags.Instance
        )!;
        MethodInfo populateMi = playerType.GetMethod(
            "PopulateCombatState",
            BindingFlags.Public | BindingFlags.Instance
        )!;
        object playersList = combatState
            .GetType()
            .GetProperty("Players", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatState)!;
        foreach (object p in (IEnumerable)playersList)
            resetMi.Invoke(p, null);
        foreach (object p in (IEnumerable)playersList)
        {
            object runState_p = playerType
                .GetProperty("RunState", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(p)!;
            object rngSet = runState_p
                .GetType()
                .GetProperty("Rng", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(runState_p)!;
            object shuffleRng = rngSet
                .GetType()
                .GetProperty("Shuffle", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(rngSet)!;
            populateMi.Invoke(p, new object[] { shuffleRng, combatState });
        }

        // L210: NetCombatCardDb.Instance.StartCombat(state.Players) — SKIPPED.
        // This populates a card→net-id dictionary for multiplayer net-serialization.
        // Our byte snapshot omits net-ids; skipping is semantically a no-op here.

        // L211-214: foreach creature: combatManager.AddCreature(creature)
        MethodInfo addCreatureMi = combatManagerType.GetMethod(
            "AddCreature",
            BindingFlags.Public | BindingFlags.Instance
        )!;
        object creaturesList = combatState
            .GetType()
            .GetProperty("Creatures", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatState)!;
        foreach (object c in (IEnumerable)creaturesList)
            addCreatureMi.Invoke(combatManagerInstance, new object[] { c });

        // L215: CombatSetUp?.Invoke(state) — SKIPPED; no subscribers in headless.

        // --- 5. Serialize canonical bytes ---------------------------------
        return SerializeCanonical(combatState, player, monsters);
    }

    /// <summary>
    /// Produce canonical bytes matching Q1's StateByteSerializer field order
    /// and shape (combat-state snapshot post-SetUpCombat, pre-StartCombatInternal).
    ///
    /// <para>
    /// Note: post-SetUpCombat the upstream state has:
    /// </para>
    /// <list type="bullet">
    ///   <item>TurnCounter ~= RoundNumber=1 (upstream's RoundNumber starts at 1)</item>
    ///   <item>Phase = PlayerActing-equivalent (upstream CurrentSide=Player)</item>
    ///   <item>Player.CurrentHp = StartingHp (no damage taken)</item>
    ///   <item>Player.Block = 0</item>
    ///   <item>Player.Powers = empty (no relic-applied powers yet)</item>
    ///   <item>Enemies: HP rolled per-monster by upstream RNG; Block=0; Powers=empty</item>
    ///   <item>Energy = 0 (StartCombatInternal's SetupPlayerTurn hasn't run)</item>
    ///   <item>DrawPile.Count = 12 (Silent starter; deck cloned + shuffled)</item>
    ///   <item>HandPile.Count = 0 / DiscardPile = 0 / ExhaustPile = 0</item>
    /// </list>
    /// </summary>
    private byte[] SerializeCanonical(
        object combatState,
        object player,
        IReadOnlyList<object> monsters
    )
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);

        // TurnCounter (Q1 uses int starting at 0 pre-StartCombat / 1 post; for
        // post-SetUpCombat snapshot the canonical equivalent is 0 — we haven't
        // entered turn 1 yet.) Upstream uses RoundNumber which starts at 1.
        // For byte parity at the post-SetUpCombat snapshot point, we choose 0
        // (matching what Q1's `initial` CombatState looks like before its
        // L205-onward transition to TurnCounter=1).
        bw.Write((int)0);

        // Phase: Q1 uses CombatPhase enum (CombatStart=0). Write 0 to match
        // the post-SetUpCombat (pre-StartCombatInternal) snapshot.
        bw.Write((int)0); // CombatPhase.CombatStart

        // Player creature
        object playerCreature = GetProperty(player, "Creature")!;
        int playerHp = ToInt(GetProperty(playerCreature, "CurrentHp")!);
        int playerBlock = ToInt(GetProperty(playerCreature, "Block")!);
        WriteCreature(bw, playerHp, playerBlock, GetPowers(playerCreature), playerSourceId: 0);

        // Enemies — match StateByteSerializer (test/Sts2Headless.Tests.Domain
        // /Combat/StateByteSerializer.cs) exactly: write each enemy in spawn
        // order, then pad to 2 with empty creatures only if count < 2. NO
        // extra-loop for count > 2 (that was an earlier bug that double-wrote
        // the 3rd enemy).
        Type combatStateType = combatState.GetType();
        object enemiesProp = GetProperty(combatState, "Enemies")!;
        var enemyCreatures = ((IEnumerable)enemiesProp).Cast<object>().ToList();
        for (int i = 0; i < enemyCreatures.Count; i++)
        {
            object ec = enemyCreatures[i];
            int hp = ToInt(GetProperty(ec, "CurrentHp")!);
            int block = ToInt(GetProperty(ec, "Block")!);
            WriteCreature(bw, hp, block, GetPowers(ec), playerSourceId: 0);
        }
        for (int i = enemyCreatures.Count; i < 2; i++)
        {
            WriteCreature(
                bw,
                currentHp: 0,
                block: 0,
                powers: Array.Empty<(string id, int stacks, uint source, bool justApplied)>(),
                playerSourceId: 0
            );
        }

        // Energy: post-SetUpCombat = 0 (StartCombatInternal not run).
        object playerCombatState = GetProperty(player, "PlayerCombatState")!;
        int energy = ToInt(GetProperty(playerCombatState, "Energy")!);
        bw.Write(energy);

        // Pile counts.
        object drawPile = GetProperty(playerCombatState, "DrawPile")!;
        object handPile = GetProperty(playerCombatState, "Hand")!;
        object discardPile = GetProperty(playerCombatState, "DiscardPile")!;
        object exhaustPile = GetProperty(playerCombatState, "ExhaustPile")!;
        bw.Write(PileCount(drawPile));
        bw.Write(PileCount(handPile));
        bw.Write(PileCount(discardPile));
        bw.Write(PileCount(exhaustPile));

        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteCreature(
        BinaryWriter bw,
        int currentHp,
        int block,
        IReadOnlyList<(string id, int stacks, uint source, bool justApplied)> powers,
        uint playerSourceId
    )
    {
        bw.Write(currentHp);
        bw.Write(block);
        bw.Write(powers.Count);
        foreach (var p in powers)
        {
            byte[] idBytes = Encoding.UTF8.GetBytes(p.id);
            bw.Write(idBytes.Length);
            bw.Write(idBytes);
            bw.Write(p.stacks);
            bw.Write(p.source);
            bw.Write(p.justApplied);
        }
    }

    private static IReadOnlyList<(string id, int stacks, uint source, bool justApplied)> GetPowers(
        object creature
    )
    {
        // Upstream: creature.Powers : IReadOnlyList<Power>
        object? powersObj = GetProperty(creature, "Powers");
        if (powersObj is null)
            return Array.Empty<(string, int, uint, bool)>();
        var result = new List<(string id, int stacks, uint source, bool justApplied)>();
        foreach (object pw in (IEnumerable)powersObj)
        {
            object model = GetProperty(pw, "Model") ?? pw;
            string modelId = GetProperty(model, "Id")?.ToString() ?? model.GetType().Name;
            // Upstream Power has `Amount` (decimal); StateByteSerializer wants
            // `stacks` (int). Coerce.
            object? amount = GetProperty(pw, "Amount") ?? GetProperty(pw, "Stacks");
            int stacks = amount switch
            {
                int i => i,
                decimal d => (int)d,
                long l => (int)l,
                _ => 0,
            };
            // SourceCreatureId in Q1 schema; upstream uses Source : Creature?
            object? source = GetProperty(pw, "Source") ?? GetProperty(pw, "SourceCreatureId");
            uint sourceId = 0;
            if (source is not null)
            {
                object? cid = GetProperty(source, "CombatId");
                if (cid is not null && cid is not uint)
                {
                    cid = GetProperty(cid, "Value");
                }
                if (cid is uint u)
                    sourceId = u;
            }
            bool justApplied = (GetProperty(pw, "JustApplied") as bool?) ?? false;
            result.Add((modelId, stacks, sourceId, justApplied));
        }
        return result;
    }

    private static int PileCount(object pile)
    {
        object cards =
            GetProperty(pile, "Cards")
            ?? throw new InvalidOperationException("CardPile.Cards not found.");
        if (cards is ICollection c)
            return c.Count;
        int count = 0;
        foreach (var _ in (IEnumerable)cards)
            count++;
        return count;
    }

    /// <summary>
    /// Inject every concrete subtype of the given base into ModelDb. Wraps
    /// each Inject in try/catch so a single bad-actor doesn't abort the
    /// batch. Used to populate the ModelDb dictionary before UnlockState's
    /// static cctor fires.
    /// </summary>
    private void InjectAllSubtypes(MethodInfo injectMi, string baseTypeName, string kind)
    {
        Type? baseType = _sts2.GetType(baseTypeName);
        if (baseType is null)
            return;
        var subtypes = _sts2
            .GetTypes()
            .Where(t =>
                t.IsClass
                && !t.IsAbstract
                && baseType.IsAssignableFrom(t)
                // Skip mock/test types — they tend to call into
                // upstream's TestSupport which crashes outside the
                // game runtime.
                && !t.Namespace?.Contains("Mocks", StringComparison.Ordinal) is true
                && !t.Name.StartsWith("Mock", StringComparison.Ordinal)
            )
            .ToList();
        int succ = 0,
            fail = 0;
        foreach (Type t in subtypes)
        {
            try
            {
                injectMi.Invoke(null, new object?[] { t });
                succ++;
            }
            catch (Exception)
            {
                fail++;
            }
        }
        if (Environment.GetEnvironmentVariable("UPSTREAM_CAPTURE_VERBOSE") is not null)
        {
            Console.Error.WriteLine(
                $"inject {kind}: {succ} ok, {fail} skipped (of {subtypes.Count})"
            );
        }
    }

    /// <summary>
    /// Manufacture an "empty" <c>UnlockState</c> WITHOUT running its
    /// instance ctor (the ctor calls <c>encountersSeen.ToHashSet()</c> which
    /// would force <c>ModelDb.AllEncounters</c> evaluation if the input
    /// pulls from there — and the type's static cctor independently does the
    /// same when <c>UnlockState.all</c> is touched). We use
    /// <c>RuntimeHelpers.GetUninitializedObject</c> to allocate an instance
    /// without invoking any constructor, then poke the two private hashset
    /// fields to empty defaults.
    /// </summary>
    private static object MakeUninitializedUnlockState(Type unlockStateType, Type modelIdType)
    {
        object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            unlockStateType
        );
        // Set the two private readonly fields to empty hashsets.
        FieldInfo epochsField =
            unlockStateType.GetField(
                "_unlockedEpochIds",
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException(
                "UnlockState._unlockedEpochIds field not found."
            );
        Type hashSetOfString = typeof(HashSet<>).MakeGenericType(typeof(string));
        object emptyEpochs = Activator.CreateInstance(hashSetOfString)!;
        epochsField.SetValue(instance, emptyEpochs);

        FieldInfo encField =
            unlockStateType.GetField(
                "_encountersSeen",
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException("UnlockState._encountersSeen field not found.");
        Type hashSetOfModelId = typeof(HashSet<>).MakeGenericType(modelIdType);
        object emptyEnc = Activator.CreateInstance(hashSetOfModelId)!;
        encField.SetValue(instance, emptyEnc);

        // NumberOfRuns is a get-only auto-property; backing field is "<NumberOfRuns>k__BackingField".
        FieldInfo? runsBacking = unlockStateType.GetField(
            "<NumberOfRuns>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        runsBacking?.SetValue(instance, 0);

        return instance;
    }

    /// <summary>
    /// Call <c>MegaCrit.Sts2.Core.Models.ModelDb.Init</c> (idempotent: tracks
    /// done locally to avoid the DuplicateModelException upstream's
    /// AbstractModel ctor throws on re-init).
    /// </summary>
    private bool _modelDbInitialized;

    private void EnsureModelDbInitialized()
    {
        if (_modelDbInitialized)
            return;
        Type modelDbType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        // ModelDb.Init iterates all AbstractModel subtypes and Activator.CreateInstance
        // each. Some constructors (Godot-bound resources, multiplayer types) crash
        // the runtime. We instead call Inject() for only the specific types we need
        // for SetUpCombat (Silent, the encounter's monsters, starter cards/relic, acts).
        // This skips the unsafe types entirely.
        MethodInfo injectMi =
            modelDbType.GetMethod("Inject", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("ModelDb.Inject not found.");
        // The seed-types we need to populate. Each Inject() call also reaches
        // their AbstractModel-typed dependencies via the type's parameterless
        // ctor; we add the minimum set and rely on transitive registration.
        string[] seedTypeNames =
        {
            // Acts — referenced by ModelDb.Acts (UnlockState.cctor pulls this)
            "MegaCrit.Sts2.Core.Models.Acts.Overgrowth",
            "MegaCrit.Sts2.Core.Models.Acts.Hive",
            "MegaCrit.Sts2.Core.Models.Acts.Glory",
            "MegaCrit.Sts2.Core.Models.Acts.Underdocks",
            // Character
            "MegaCrit.Sts2.Core.Models.Characters.Silent",
            // Silent starter cards
            "MegaCrit.Sts2.Core.Models.Cards.StrikeSilent",
            "MegaCrit.Sts2.Core.Models.Cards.DefendSilent",
            "MegaCrit.Sts2.Core.Models.Cards.Neutralize",
            "MegaCrit.Sts2.Core.Models.Cards.Survivor",
            // Silent starter relic
            "MegaCrit.Sts2.Core.Models.Relics.RingOfTheSnake",
            // Card / relic / potion pools (referenced by character)
            "MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool",
            "MegaCrit.Sts2.Core.Models.RelicPools.SilentRelicPool",
            "MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool",
            "MegaCrit.Sts2.Core.Models.PotionPools.SilentPotionPool",
            // MultiplayerScalingModel singleton (touched by RunState.CreateShared)
            "MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel",
        };
        foreach (string tn in seedTypeNames)
        {
            Type? t = _sts2.GetType(tn);
            if (t is not null)
            {
                try
                {
                    injectMi.Invoke(null, new object?[] { t });
                }
                catch (Exception ex)
                { /* log later */
                    Console.Error.WriteLine($"warn: Inject {tn}: {(ex.InnerException ?? ex).Message}");
                }
            }
        }

        // To unblock UnlockState's cctor (which iterates ModelDb.AllEncounters
        // → all 4 acts' GenerateAllEncounters → ~88 encounter classes by
        // ModelDb.Encounter<X>()), inject every concrete encounter class in
        // the upstream assembly. Some construct cleanly; some (those touching
        // Godot resources at ctor) will fail — we skip those.
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.EncounterModel", "Encounter");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.MonsterModel", "Monster");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.EventModel", "Event");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.AncientEventModel", "Ancient");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.PowerModel", "Power");
        // v0.105.1 introduced BadgeModel: RunState.CreateShared reads ModelDb.BadgeModels
        // during construction; we must populate them before CreateForNewRun fires.
        // InjectAllSubtypes is a no-op if the base type doesn't exist (v0.103.2), so this
        // remains correct under both DLL versions.
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.BadgeModel", "Badge");
        // RelicModel, CardModel, PotionModel — needed so SharedRelicPool/SilentRelicPool/
        // SilentCardPool/SilentPotionPool can resolve ModelDb.Relic<X>()/Card<X>()/Potion<X>()
        // inside their GenerateAll* methods (called lazily by GetUnlockedRelics / AllCards / etc.).
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.RelicModel", "Relic");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.CardModel", "Card");
        InjectAllSubtypes(injectMi, "MegaCrit.Sts2.Core.Models.PotionModel", "Potion");
        // NOTE: We DON'T call ModelDb.InitIds() — it iterates _contentById and
        // calls ModelIdSerializationCache.GetNetIdForCategory("ACHIEVEMENT")
        // (and similar), which the cache only knows about after upstream's
        // multiplayer subsystem has registered them at process startup. Since
        // we don't need net-ids for combat-state capture (no multiplayer
        // serialization happens in our path), we skip InitIds and let
        // AbstractModel's CategorySortingId/EntrySortingId stay at 0. The
        // SetUpCombat code path doesn't read those fields.
        _modelDbInitialized = true;
    }

    // ===== Mid-combat capture (wave-50/A.2) ===================================

    /// <summary>
    /// Capture upstream's full multi-turn combat for one (seed, plan) tuple.
    /// Drives the upstream turn loop via reflection using <see cref="MockLayer.TurnLoopBootstrap"/>
    /// (wave-50/A.1) for Godot-singleton setup, then calls <c>StartTurn</c>,
    /// <c>EndPlayerTurnPhaseOneInternal</c>, and <c>ExecuteEnemyTurn</c> directly
    /// (bypassing the async <c>ReadyToBeginEnemyTurnAction</c> gate per A.0 §7 Risk 6).
    ///
    /// <para>
    /// Emits three <see cref="Sts2Headless.DeterminismProbe.MidCombatRecord"/> per turn,
    /// matching Q1's <c>Q1MidCombatCaptureDriver</c> Side semantics:
    /// <list type="bullet">
    ///   <item><c>"player-pre"</c> — after <c>StartTurn</c> (player side) completes</item>
    ///   <item><c>"player-end"</c> — after <c>EndPlayerTurnPhaseOneInternal</c></item>
    ///   <item><c>"enemy-end"</c> — after <c>ExecuteEnemyTurn</c> + <c>EndEnemyTurnInternal</c></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Phase-2 replaces Phase-1:</b> the wave-49/A.2 single "combat-start" Turn=0 snapshot
    /// is fully superseded. Goldens must be re-captured by A.3 after this lands.
    /// </para>
    ///
    /// <para>
    /// <b>Card-play reflection (W3 baked):</b> cards are looked up in the Hand pile by
    /// model-type name (StrikeSilent / DefendSilent / Neutralize). <c>SpendResources</c>
    /// is called to deduct energy, then <c>OnPlay</c> (protected virtual) is invoked
    /// directly via reflection — bypassing <c>OnPlayWrapper</c>'s Godot visual calls.
    /// </para>
    ///
    /// <para>
    /// <b>target_creature_id translation (W3 baked):</b> action-plan's
    /// <c>target_creature_id</c> is a 1-indexed enemy-spawn-order id (1 = first enemy,
    /// 2 = second, …). The driver subtracts 1 to obtain the 0-based
    /// <c>CombatState.Enemies[index]</c> reference. <c>null</c> → no target (self-target
    /// cards like DefendSilent).
    /// </para>
    /// </summary>
    public IReadOnlyList<Sts2Headless.DeterminismProbe.MidCombatRecord> CaptureMidCombat(
        int seed,
        EncounterCatalog.EncounterPlan plan,
        Sts2Headless.DeterminismProbe.MidCombatActionPlan actionPlan,
        int maxTurns = 20
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(actionPlan);

        // ---- Phase 1: pre-combat setup (same as Capture()) ----
        Type testModeType = TypeOrThrow("MegaCrit.Sts2.Core.TestSupport.TestMode");
        testModeType.GetProperty("IsOn", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, true);

        // Inject fake SaveManager (same as Capture() preamble).
        Type saveManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.SaveManager");
        FieldInfo mockField = saveManagerType.GetField(
            "_mockInstance", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SaveManager._mockInstance not found.");
        object fakeSaveManager =
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(saveManagerType);
        Type progressSaveManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager");
        object fakePsm =
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(progressSaveManagerType);
        Type progressStateType = TypeOrThrow("MegaCrit.Sts2.Core.Saves.ProgressState");
        object defaultPs = progressStateType
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        progressSaveManagerType.GetField(
            "<Progress>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(fakePsm, defaultPs);
        saveManagerType.GetField(
            "_progressSaveManager", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(fakeSaveManager, fakePsm);
        mockField.SetValue(null, fakeSaveManager);

        EnsureModelDbInitialized();

        string stringSeed = $"seed-{seed}";

        Type playerType = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Players.Player");
        Type unlockStateType = TypeOrThrow("MegaCrit.Sts2.Core.Unlocks.UnlockState");
        Type silentType = TypeOrThrow("MegaCrit.Sts2.Core.Models.Characters.Silent");
        Type modelIdType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelId");
        Type characterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.CharacterModel");
        Type modelDbType_local = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");

        object unlockStateInstance = MakeUninitializedUnlockState(unlockStateType, modelIdType);

        MethodInfo modelDbCharacterGeneric =
            modelDbType_local.GetMethod("Character", BindingFlags.Static | BindingFlags.Public)!;
        object silentCharacter = modelDbCharacterGeneric.MakeGenericMethod(silentType).Invoke(null, null)!;

        int startingHp = ToInt(characterModelType.GetProperty("StartingHp")!.GetValue(silentCharacter)!);
        int maxEnergy = ToInt(characterModelType.GetProperty("MaxEnergy")!.GetValue(silentCharacter)!);
        int startingGold = ToInt(characterModelType.GetProperty("StartingGold")!.GetValue(silentCharacter)!);
        int orbSlotCount = ToInt(characterModelType.GetProperty("BaseOrbSlotCount")!.GetValue(silentCharacter)!);

        Type relicGrabBagType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RelicGrabBag");
        object relicGrabBag = Activator.CreateInstance(relicGrabBagType, new object[] { false })!;

        ConstructorInfo? playerCtor = playerType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 15);
        if (playerCtor is null)
            throw new InvalidOperationException("Player ctor with 15 params not found.");

        object player = playerCtor.Invoke(new object?[]
        {
            silentCharacter, 1uL, startingHp, startingHp, maxEnergy, startingGold,
            3, orbSlotCount, relicGrabBag, unlockStateInstance,
            null, null, null, null, null,
        });
        playerType.GetMethod("PopulateStartingInventory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, null);

        Type runStateType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RunState");
        Type actModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ActModel");
        Type modifierModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModifierModel");
        Type gameModeType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.GameMode");
        object gameModeStandard = Enum.Parse(gameModeType, "Standard");

        object canonicalActs = actModelType
            .GetMethod("GetDefaultList", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, null)!;

        MethodInfo actToMutableMi = actModelType
            .GetMethod("ToMutable", BindingFlags.Public | BindingFlags.Instance)!;
        var actList = (System.Collections.IEnumerable)canonicalActs;
        Type listOfAct = typeof(List<>).MakeGenericType(actModelType);
        object mutableActs = Activator.CreateInstance(listOfAct)!;
        MethodInfo addActMi = listOfAct.GetMethod("Add")!;
        foreach (object a in actList)
            addActMi.Invoke(mutableActs, new[] { actToMutableMi.Invoke(a, null)! });

        object emptyModifiers = Array.CreateInstance(modifierModelType, 0);
        object players = MakeReadOnlyList(playerType, new[] { player });

        MethodInfo createRunFromNew = runStateType
            .GetMethod("CreateForNewRun", BindingFlags.Static | BindingFlags.Public)!;
        object runState = createRunFromNew.Invoke(null, new object[]
        {
            players, mutableActs, emptyModifiers, gameModeStandard, 0, stringSeed,
        })!;

        Type combatStateType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatState");
        Type combatSideType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatSide");
        object combatSideEnemy = Enum.Parse(combatSideType, "Enemy");
        object combatSidePlayer = Enum.Parse(combatSideType, "Player");

        object multiplayerScalingModel = runStateType.GetProperty("MultiplayerScalingModel")!.GetValue(runState)!;
        object modifiers = runStateType.GetProperty("Modifiers")!.GetValue(runState)!;

        var combatStateNamedArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["encounter"] = null, ["runState"] = runState,
            ["modifiers"] = modifiers, ["multiplayerScalingModel"] = multiplayerScalingModel,
        };
        object? badgeModels = ReflectionFlex.TryGetProperty(runState, "BadgeModels");
        if (badgeModels is not null)
            combatStateNamedArgs["badgeModels"] = badgeModels;

        (ConstructorInfo csCtor, object?[] csArgs) = ReflectionFlex.FindCtorByParameterNames(
            combatStateType,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            combatStateNamedArgs,
            requiredNames: new[] { "encounter", "runState", "modifiers", "multiplayerScalingModel" }
        );
        object combatState = csCtor.Invoke(csArgs);

        combatStateType.GetMethod("AddPlayer", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(combatState, new object[] { player });

        // Spawn enemies per plan (UpstreamComparable or UpstreamEncounterRng path).
        Type monsterModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.MonsterModel");
        Type modelDbType2 = TypeOrThrow("MegaCrit.Sts2.Core.Models.ModelDb");
        MethodInfo modelDbMonsterGeneric = modelDbType2
            .GetMethod("Monster", BindingFlags.Static | BindingFlags.Public)!;

        List<(object mutableMonster, string? slot)> spawnList;
        if (plan.Kind == EncounterCatalog.PlanKind.UpstreamEncounterRng)
        {
            spawnList = ResolveViaUpstreamEncounterRng(
                plan, runState, modelDbType2, modelDbMonsterGeneric, monsterModelType);
        }
        else
        {
            spawnList = new List<(object, string?)>();
            for (int mi = 0; mi < plan.MonsterIds.Count; mi++)
            {
                string monsterId = plan.MonsterIds[mi];
                string? slot = plan.Slots[mi];
                Type monsterClassType = TypeOrThrow($"MegaCrit.Sts2.Core.Models.Monsters.{monsterId}");
                object canonicalMonster = modelDbMonsterGeneric.MakeGenericMethod(monsterClassType).Invoke(null, null)!;
                object mutableMonster = monsterModelType
                    .GetMethod("ToMutable", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(canonicalMonster, null)!;
                spawnList.Add((mutableMonster, slot));
            }
        }

        MethodInfo createCreatureMi = combatStateType
            .GetMethod("CreateCreature", BindingFlags.Public | BindingFlags.Instance)!;
        foreach ((object mutableMonster, string? slot) in spawnList)
        {
            object creature = createCreatureMi.Invoke(
                combatState, new object?[] { mutableMonster, combatSideEnemy, slot })!;
            combatStateType.GetMethod("AddCreature", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(combatState, new object[] { creature });
        }

        // ---- Phase 2: SetUpCombat body (same as Capture()) ----
        Type combatManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Combat.CombatManager");
        object combatManagerInstance = combatManagerType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        combatManagerType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(combatManagerInstance, combatState);

        object? multiplayerScaling = ReflectionFlex.TryGetProperty(combatState, "MultiplayerScalingModel");
        if (multiplayerScaling is not null)
        {
            multiplayerScaling.GetType()
                .GetMethod("OnCombatEntered", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(multiplayerScaling, new object[] { combatState });
        }

        object stateTracker = combatManagerType
            .GetProperty("StateTracker", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatManagerInstance)!;
        stateTracker.GetType()
            .GetMethod("SetState", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(stateTracker, new object[] { combatState });

        MethodInfo resetMi = playerType.GetMethod("ResetCombatState", BindingFlags.Public | BindingFlags.Instance)!;
        MethodInfo populateMi = playerType.GetMethod("PopulateCombatState", BindingFlags.Public | BindingFlags.Instance)!;
        object playersList = combatState.GetType()
            .GetProperty("Players", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatState)!;
        foreach (object p in (System.Collections.IEnumerable)playersList)
            resetMi.Invoke(p, null);
        foreach (object p in (System.Collections.IEnumerable)playersList)
        {
            object runState_p = playerType.GetProperty("RunState", BindingFlags.Public | BindingFlags.Instance)!.GetValue(p)!;
            object rngSet = runState_p.GetType().GetProperty("Rng", BindingFlags.Public | BindingFlags.Instance)!.GetValue(runState_p)!;
            object shuffleRng = rngSet.GetType().GetProperty("Shuffle", BindingFlags.Public | BindingFlags.Instance)!.GetValue(rngSet)!;
            populateMi.Invoke(p, new object[] { shuffleRng, combatState });
        }

        // NetCombatCardDb.Instance.StartCombat SKIPPED (multiplayer serialization).

        MethodInfo addCreatureMi_cm = combatManagerType.GetMethod("AddCreature", BindingFlags.Public | BindingFlags.Instance)!;
        object creaturesList = combatState.GetType()
            .GetProperty("Creatures", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(combatState)!;
        foreach (object c in (System.Collections.IEnumerable)creaturesList)
            addCreatureMi_cm.Invoke(combatManagerInstance, new object[] { c });

        // ---- Phase 3: Godot Logger safety + TurnLoopBootstrap + IsInProgress gate ----

        // (3a) Patch Logger.GetIsRunningFromGodotEditor via Harmony before any
        // Logger-using upstream class is constructed. Without this patch, the Logger
        // static initializer calls Godot.OS.GetCmdlineArgs() → SIGSEGV headless.
        // Safe to call multiple times (idempotent; only patches once per AppDomain).
        EnsureGodotLoggerSafe();

        // (3b) Reset RunManager.State to null if a previous run left it non-null
        // (e.g., TurnLoopBootstrap.Dispose threw before CleanUp finished).
        // RunManager.SetUpTest() checks State != null and throws if it is set.
        {
            Type rmType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RunManager");
            object rmInst = rmType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            FieldInfo? stateField = rmType.GetField(
                "<State>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (stateField?.GetValue(rmInst) is not null)
            {
                stateField.SetValue(rmInst, null);
                Console.Error.WriteLine(
                    "warn: CaptureMidCombat: RunManager.State was non-null; reset to null before SetUpTest."
                );
            }
        }

        // (3c) TurnLoopBootstrap (wave-50/A.1): sets LocalContext.NetId=1UL + calls
        // RunManager.Instance.SetUpTest(runState, singleplayerSvc, disableCombatStateSync=true).
        // Per A.0 §7 Risk 5: ActionExecutor.Unpause() must be called before StartTurn.
        // Per A.0 §7 Risk 4: LocalContext.NetId non-null required for hook chains.

        // Resolve CombatManager private methods for direct turn-loop invocation.
        // Per A.0 §7 Risk 6: bypass ReadyToBeginEnemyTurnAction gate by calling phases directly.
        MethodInfo startTurnMi = combatManagerType.GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            .Where(m => m.Name == "StartTurn")
            .OrderByDescending(m => m.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("CombatManager.StartTurn not found.");

        MethodInfo endPlayerTurnPhaseOneMi = combatManagerType.GetMethod(
            "EndPlayerTurnPhaseOneInternal",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("CombatManager.EndPlayerTurnPhaseOneInternal not found.");

        MethodInfo executeEnemyTurnMi = combatManagerType.GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance
            )
            .Where(m => m.Name == "ExecuteEnemyTurn")
            .OrderByDescending(m => m.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("CombatManager.ExecuteEnemyTurn not found.");

        MethodInfo endEnemyTurnInternalMi = combatManagerType.GetMethod(
            "EndEnemyTurnInternal",
            BindingFlags.NonPublic | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("CombatManager.EndEnemyTurnInternal not found.");

        // Set IsInProgress = true (private set; normally set by StartCombatInternal).
        PropertyInfo isInProgressProp = combatManagerType.GetProperty(
            "IsInProgress",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException("CombatManager.IsInProgress not found.");
        FieldInfo? isInProgressField = combatManagerType.GetField(
            "<IsInProgress>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (isInProgressField is not null)
            isInProgressField.SetValue(combatManagerInstance, true);
        else
            isInProgressProp.SetValue(combatManagerInstance, true);

        // CombatState.CurrentSide must start as Player; RoundNumber must start at 1.
        PropertyInfo? currentSideProp = combatStateType.GetProperty(
            "CurrentSide",
            BindingFlags.Public | BindingFlags.Instance
        );
        if (currentSideProp is not null && currentSideProp.CanWrite)
            currentSideProp.SetValue(combatState, combatSidePlayer);

        PropertyInfo? roundNumberProp = combatStateType.GetProperty(
            "RoundNumber",
            BindingFlags.Public | BindingFlags.Instance
        );
        if (roundNumberProp is not null && roundNumberProp.CanWrite)
            roundNumberProp.SetValue(combatState, 1);

        // ---- Phase 4: turn loop ----
        var records = new List<Sts2Headless.DeterminismProbe.MidCombatRecord>();

        using var bootstrap = new MockLayer.TurnLoopBootstrap(_sts2, runState, netId: 1UL);
        try
        {
            // Unpause ActionExecutor before first StartTurn (per A.0 §7 Risk 5).
            UnpauseActionExecutor();

            for (int turn = 1; turn <= maxTurns; turn++)
            {
                // Check combat is still in progress before each turn.
                bool isInProgress = (bool)isInProgressProp.GetValue(combatManagerInstance)!;
                if (!isInProgress)
                    break;

                // Verify state is on player side before StartTurn.
                object? currentSideVal = currentSideProp?.GetValue(combatState);
                if (currentSideVal is not null && !currentSideVal.Equals(combatSidePlayer))
                {
                    Console.Error.WriteLine(
                        $"warn: CaptureMidCombat: CurrentSide={currentSideVal} before StartTurn turn={turn} "
                        + $"(expected Player); break.");
                    break;
                }

                // --- player-pre: run StartTurn (player side) ---
                // StartTurn handles: creature.BeforeTurnStart, Hook.BeforeSideTurnStart,
                // energy reset, card draw (5 cards), Hook.AfterSideTurnStart.
                // Completes when ActionExecutor.Unpause is called (player-turn-start boundary).
                // startTurnMi takes Func<Task>? arg (or no arg if 0-param overload found).
                object?[] startTurnArgs = startTurnMi.GetParameters().Length > 0
                    ? new object?[] { null }
                    : Array.Empty<object?>();
                InvokeAsyncMethod(startTurnMi, combatManagerInstance, startTurnArgs);

                object currentState = combatManagerType
                    .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(combatManagerInstance)!;

                // Check combat ended during StartTurn (win condition).
                isInProgress = (bool)isInProgressProp.GetValue(combatManagerInstance)!;
                if (!isInProgress)
                    break;

                records.Add(SnapshotUpstream(currentState, turn, "player-pre"));

                // --- Play scripted actions for this turn ---
                IReadOnlyList<Sts2Headless.DeterminismProbe.MidCombatAction> actions =
                    actionPlan.ActionsForTurn(turn);
                PlayActionsUpstream(
                    combatManagerInstance,
                    combatManagerType,
                    player,
                    playerType,
                    currentState,
                    combatStateType,
                    actions,
                    plan.EncounterId,
                    turn);

                isInProgress = (bool)isInProgressProp.GetValue(combatManagerInstance)!;
                if (!isInProgress)
                    break;

                // --- player-end: EndPlayerTurnPhaseOneInternal ---
                // Handles: AutoPostPlay hooks, BeforeTurnEnd hook, discard hand (DoTurnEnd).
                InvokeAsyncMethod(endPlayerTurnPhaseOneMi, combatManagerInstance, Array.Empty<object?>());

                currentState = combatManagerType
                    .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(combatManagerInstance)!;

                isInProgress = (bool)isInProgressProp.GetValue(combatManagerInstance)!;
                if (!isInProgress)
                    break;

                records.Add(SnapshotUpstream(currentState, turn, "player-end"));

                // Flip to enemy side manually (bypass ReadyToBeginEnemyTurnAction gate per A.0 §7 Risk 6).
                if (currentSideProp is not null && currentSideProp.CanWrite)
                    currentSideProp.SetValue(currentState, combatSideEnemy);

                // --- enemy-end: ExecuteEnemyTurn ---
                // Each enemy calls TakeTurn() which performs the move (attack/defend/etc.).
                // Some bosses (KaiserCrabBoss/Crusher) access NCombatRoom.Instance.Background
                // for VFX inside their move methods — NCombatRoom.Instance is null headless.
                // Catch the exception, warn, and take a snapshot of the pre-enemy-action
                // state rather than propagating the failure for the whole seed.
                object?[] execArgs = executeEnemyTurnMi.GetParameters().Length > 0
                    ? new object?[] { null }
                    : Array.Empty<object?>();
                bool enemyTurnOk = true;
                try
                {
                    InvokeAsyncMethod(executeEnemyTurnMi, combatManagerInstance, execArgs);
                }
                catch (Exception ex)
                {
                    enemyTurnOk = false;
                    Exception inner = ex.InnerException ?? ex;
                    Console.Error.WriteLine(
                        $"warn: CaptureMidCombat: ExecuteEnemyTurn threw at "
                        + $"encounter={plan.EncounterId} turn={turn}: {inner.GetType().Name}: {inner.Message} "
                        + "(snapshot reflects pre-enemy-action state)"
                    );
                }

                currentState = combatManagerType
                    .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(combatManagerInstance)!;

                // EndEnemyTurnInternal: fires Hook.BeforeTurnEnd + AfterTurnEnd on enemy side.
                // Skip if ExecuteEnemyTurn failed (state may be inconsistent).
                if (enemyTurnOk)
                {
                    try
                    {
                        InvokeAsyncMethod(endEnemyTurnInternalMi, combatManagerInstance, Array.Empty<object?>());
                    }
                    catch (Exception ex)
                    {
                        Exception inner = ex.InnerException ?? ex;
                        Console.Error.WriteLine(
                            $"warn: CaptureMidCombat: EndEnemyTurnInternal threw at "
                            + $"encounter={plan.EncounterId} turn={turn}: {inner.GetType().Name}: {inner.Message}"
                        );
                    }
                }

                isInProgress = (bool)isInProgressProp.GetValue(combatManagerInstance)!;

                // enemy-end snapshot (post-ExecuteEnemyTurn + EndEnemyTurnInternal).
                records.Add(SnapshotUpstream(currentState, turn, "enemy-end"));

                if (!isInProgress)
                    break;

                // Flip back to player side + increment RoundNumber for next turn.
                if (currentSideProp is not null && currentSideProp.CanWrite)
                    currentSideProp.SetValue(currentState, combatSidePlayer);
                if (roundNumberProp is not null && roundNumberProp.CanWrite)
                {
                    int currentRound = (int)roundNumberProp.GetValue(currentState)!;
                    roundNumberProp.SetValue(currentState, currentRound + 1);
                }

                // Notify creatures of side switch (OnSideSwitch prepares next-turn state).
                object? creaturesObj = GetProperty(currentState, "Creatures");
                if (creaturesObj is System.Collections.IEnumerable creaturesEnum)
                {
                    Type? creatureType = _sts2.GetType("MegaCrit.Sts2.Core.Entities.Creatures.Creature");
                    MethodInfo? onSideSwitchMi = creatureType?.GetMethod(
                        "OnSideSwitch",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    if (onSideSwitchMi is not null)
                    {
                        foreach (object creature in creaturesEnum)
                            onSideSwitchMi.Invoke(creature, null);
                    }
                }
            }
        }
        finally
        {
            // TurnLoopBootstrap.Dispose() restores LocalContext.NetId + calls RunManager.CleanUp().
            // Disposed by the using statement automatically.
        }

        return records;
    }

    /// <summary>
    /// Call <c>ActionExecutor.Unpause()</c> via <c>RunManager.Instance.ActionExecutor</c>
    /// before the turn loop begins. Per A.0 §7 Risk 5, ActionExecutor starts Paused after
    /// <c>RunManager.SetUpTest</c>; unpause is required for the turn loop to not block.
    /// With <c>NonInteractiveMode.IsActive=true</c>, <c>WaitForUnpause</c> is a no-op —
    /// this is belt-and-suspenders insurance.
    /// </summary>
    private void UnpauseActionExecutor()
    {
        try
        {
            Type runManagerType = TypeOrThrow("MegaCrit.Sts2.Core.Runs.RunManager");
            object runManagerInstance = runManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            object? actionExecutor = runManagerType
                .GetProperty("ActionExecutor", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(runManagerInstance);
            if (actionExecutor is not null)
            {
                actionExecutor.GetType()
                    .GetMethod("Unpause", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(actionExecutor, null);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"warn: UnpauseActionExecutor: {(ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex).Message}");
        }
    }

    /// <summary>
    /// Invoke an <c>async Task</c> method via reflection synchronously
    /// (<c>GetAwaiter().GetResult()</c>). Unwraps <see cref="TargetInvocationException"/>
    /// so exception surfaces are clean.
    /// </summary>
    private static void InvokeAsyncMethod(MethodInfo mi, object? instance, object?[] args)
    {
        try
        {
            object? result = mi.Invoke(instance, args);
            if (result is System.Threading.Tasks.Task task)
                task.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException tie)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(tie.InnerException ?? tie)
                .Throw();
        }
    }

    /// <summary>
    /// Play the scripted action sequence for one turn via direct upstream card-play
    /// reflection. Looks up each card in the Hand pile by model-type simple name,
    /// then invokes <c>SpendResources</c> + <c>OnPlay</c> (protected virtual) directly —
    /// bypassing <c>OnPlayWrapper</c>'s Godot visual calls (safe: <c>TestMode.IsOn=true</c>).
    ///
    /// <para>
    /// <b>target_creature_id translation (W3):</b>
    /// <c>target_creature_id = N</c> (1-indexed) → <c>combatState.Enemies[N-1]</c>.
    /// <c>target_creature_id = null</c> → null target (self-target cards: DefendSilent, etc.).
    /// </para>
    /// </summary>
    private void PlayActionsUpstream(
        object combatManagerInstance,
        Type combatManagerType,
        object player,
        Type playerType,
        object currentState,
        Type combatStateType,
        IReadOnlyList<Sts2Headless.DeterminismProbe.MidCombatAction> actions,
        string encounterId,
        int turn
    )
    {
        // Build enemy list for target resolution (encounter-start order == Enemies order).
        object? enemiesObj = GetProperty(currentState, "Enemies");
        var enemies = enemiesObj is System.Collections.IEnumerable ei
            ? ei.Cast<object>().ToList()
            : new List<object>();

        // Resolve player's Hand pile cards for lookup by model-type name.
        object? playerCombatState = GetProperty(player, "PlayerCombatState");
        if (playerCombatState is null)
        {
            Console.Error.WriteLine(
                $"warn: PlayActionsUpstream: player.PlayerCombatState null at turn={turn}; skipping actions.");
            return;
        }
        object? handPile = GetProperty(playerCombatState, "Hand");
        if (handPile is null)
        {
            Console.Error.WriteLine(
                $"warn: PlayActionsUpstream: PlayerCombatState.Hand null at turn={turn}; skipping actions.");
            return;
        }

        // Resolve OnPlay and SpendResources methods on CardModel.
        Type cardModelType = TypeOrThrow("MegaCrit.Sts2.Core.Models.CardModel");
        MethodInfo? spendResourcesMi = cardModelType.GetMethod(
            "SpendResources",
            BindingFlags.Public | BindingFlags.Instance
        );
        MethodInfo? onPlayMi = cardModelType.GetMethod(
            "OnPlay",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Resolve types for CardPlay + ResourceInfo + PlayerChoiceContext construction.
        Type cardPlayType = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Cards.CardPlay");
        Type resourceInfoType = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Cards.ResourceInfo");
        Type hookPlayerChoiceContextType = TypeOrThrow(
            "MegaCrit.Sts2.Core.GameActions.Multiplayer.HookPlayerChoiceContext"
        );
        Type gameActionTypeEnum = TypeOrThrow(
            "MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType"
        );
        object gameActionTypeCombat = Enum.Parse(gameActionTypeEnum, "CombatPlayPhaseOnly");
        Type pileTypeEnum = TypeOrThrow("MegaCrit.Sts2.Core.Entities.Cards.PileType");
        object pileTypeDiscard = Enum.Parse(pileTypeEnum, "Discard");

        // LocalContext.NetId was set to 1UL by TurnLoopBootstrap.
        const ulong netId = 1UL;

        foreach (Sts2Headless.DeterminismProbe.MidCombatAction action in actions)
        {
            if (action.EndTurn)
                break;

            // Check combat still in progress.
            PropertyInfo? isInProgressProp = combatManagerType.GetProperty(
                "IsInProgress", BindingFlags.Public | BindingFlags.Instance);
            bool isInProgress = (bool)(isInProgressProp?.GetValue(combatManagerInstance) ?? false);
            if (!isInProgress)
                break;

            // Find card in hand by model-type simple name (e.g. "StrikeSilent").
            object? cardModel = FindCardInHand(handPile, action.CardId, cardModelType);
            if (cardModel is null)
            {
                Console.Error.WriteLine(
                    $"warn: PlayActionsUpstream: card '{action.CardId}' not in Hand at "
                    + $"encounter={encounterId} turn={turn}; skipping.");
                continue;
            }

            // Resolve target: action.TargetCreatureId is 1-indexed enemy-spawn-order
            // (1 = first enemy, 2 = second, …); null = no target (self-target cards).
            // Subtract 1 to convert to 0-based enemies[] index.
            object? target = null;
            if (action.TargetCreatureId.HasValue)
            {
                int idx = action.TargetCreatureId.Value - 1;
                if (idx >= 0 && idx < enemies.Count)
                    target = enemies[idx];
                else
                    Console.Error.WriteLine(
                        $"warn: PlayActionsUpstream: target_creature_id={action.TargetCreatureId.Value} (idx={idx}) out of range "
                        + $"(enemies.Count={enemies.Count}) at encounter={encounterId} turn={turn}; no target.");
            }

            // SpendResources: deducts energy + stars, returns (int energySpent, int starsSpent).
            int energySpent = 0;
            int starsSpent = 0;
            if (spendResourcesMi is not null)
            {
                try
                {
                    object? spendResult = spendResourcesMi.Invoke(cardModel, null);
                    if (spendResult is System.Threading.Tasks.Task spendTask)
                        spendTask.GetAwaiter().GetResult();
                    // SpendResources returns Task<(int,int)> — result is the tuple.
                    // Access via Result property if the task is generic.
                    if (spendResult is not null)
                    {
                        Type spendResultType = spendResult.GetType();
                        object? resultProp = spendResultType.GetProperty("Result")?.GetValue(spendResult);
                        if (resultProp is not null)
                        {
                            Type tupleType = resultProp.GetType();
                            object? item1 = tupleType.GetField("Item1")?.GetValue(resultProp)
                                ?? tupleType.GetProperty("Item1")?.GetValue(resultProp);
                            object? item2 = tupleType.GetField("Item2")?.GetValue(resultProp)
                                ?? tupleType.GetProperty("Item2")?.GetValue(resultProp);
                            if (item1 is int e) energySpent = e;
                            if (item2 is int s) starsSpent = s;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
                    Console.Error.WriteLine(
                        $"warn: PlayActionsUpstream: SpendResources threw at "
                        + $"encounter={encounterId} turn={turn} card={action.CardId}: {inner.Message}");
                }
            }

            // Construct ResourceInfo struct (required-init; use backing-field injection).
            object resourceInfo = ConstructResourceInfo(resourceInfoType, energySpent, starsSpent);

            // Construct CardPlay (required-init class).
            object cardPlay = ConstructCardPlay(
                cardPlayType, cardModel, target, pileTypeDiscard, resourceInfo);

            // Construct HookPlayerChoiceContext(player, netId, GameActionType.CombatPlayPhaseOnly).
            object choiceContext = ConstructHookPlayerChoiceContext(
                hookPlayerChoiceContextType, player, netId, gameActionTypeCombat);

            // Invoke OnPlay (protected virtual Task) directly, bypassing OnPlayWrapper Godot calls.
            if (onPlayMi is not null)
            {
                try
                {
                    object? onPlayResult = onPlayMi.Invoke(cardModel, new object?[] { choiceContext, cardPlay });
                    if (onPlayResult is System.Threading.Tasks.Task onPlayTask)
                        onPlayTask.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Exception inner = ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex;
                    Console.Error.WriteLine(
                        $"warn: PlayActionsUpstream: OnPlay threw at "
                        + $"encounter={encounterId} turn={turn} card={action.CardId}: {inner.Message}");
                }
            }

            // Move card from current pile (Play/Hand) to Discard to maintain pile state.
            // OnPlay moves the card to Play pile; OnPlayWrapper normally handles result-pile transition.
            MoveCardToDiscard(cardModel, cardModelType, pileTypeEnum, pileTypeDiscard, player);
        }
    }

    /// <summary>
    /// Find the first card in the Hand pile whose model-type simple name or Model.Id.Entry
    /// matches <paramref name="cardId"/> (e.g. "StrikeSilent", "DefendSilent", "Neutralize").
    /// Returns null if not found.
    /// </summary>
    private static object? FindCardInHand(object handPile, string cardId, Type cardModelType)
    {
        object? cardsObj = GetProperty(handPile, "Cards");
        if (cardsObj is null)
            return null;
        foreach (object card in (System.Collections.IEnumerable)cardsObj)
        {
            // Match by type simple name (e.g. "StrikeSilent").
            string typeName = card.GetType().Name;
            if (string.Equals(typeName, cardId, StringComparison.Ordinal))
                return card;

            // Also try via Model.Id.Entry for decompiled names.
            object? modelObj = GetProperty(card, "Model") ?? card;
            object? idObj = GetProperty(modelObj, "Id");
            if (idObj is not null)
            {
                string? entry = GetProperty(idObj, "Entry")?.ToString();
                if (string.Equals(entry, cardId, StringComparison.Ordinal))
                    return card;
            }
        }
        return null;
    }

    /// <summary>
    /// Construct a <c>ResourceInfo</c> struct via reflection (all required-init fields).
    /// Uses backing-field injection since C# required-init properties aren't settable
    /// post-construction via normal reflection.
    /// </summary>
    private static object ConstructResourceInfo(Type resourceInfoType, int energySpent, int starsSpent)
    {
        object ri = Activator.CreateInstance(resourceInfoType)!;
        void SetProp(string name, int val)
        {
            PropertyInfo? p = resourceInfoType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p is not null && p.CanWrite)
            {
                p.SetValue(ri, val);
                return;
            }
            FieldInfo? f = resourceInfoType.GetField(
                $"<{name}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            f?.SetValue(ri, val);
        }
        SetProp("EnergySpent", energySpent);
        SetProp("EnergyValue", energySpent);
        SetProp("StarsSpent", starsSpent);
        SetProp("StarValue", starsSpent);
        return ri;
    }

    /// <summary>
    /// Construct a <c>CardPlay</c> object via reflection (required-init properties).
    /// </summary>
    private static object ConstructCardPlay(
        Type cardPlayType,
        object card,
        object? target,
        object pileTypeDiscard,
        object resourceInfo
    )
    {
        object cp = Activator.CreateInstance(cardPlayType)!;
        void SetProp(string name, object? val)
        {
            PropertyInfo? p = cardPlayType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p is not null && p.CanWrite)
            {
                p.SetValue(cp, val);
                return;
            }
            FieldInfo? f = cardPlayType.GetField(
                $"<{name}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            f?.SetValue(cp, val);
        }
        SetProp("Card", card);
        SetProp("Target", target);
        SetProp("ResultPile", pileTypeDiscard);
        SetProp("Resources", resourceInfo);
        SetProp("IsAutoPlay", false);
        SetProp("PlayIndex", 0);
        SetProp("PlayCount", 1);
        return cp;
    }

    /// <summary>
    /// Construct <c>HookPlayerChoiceContext(Player owner, ulong localPlayerId, GameActionType)</c>
    /// via reflection.
    /// </summary>
    private static object ConstructHookPlayerChoiceContext(
        Type contextType,
        object owner,
        ulong netId,
        object gameActionType
    )
    {
        // Ctor: HookPlayerChoiceContext(Player owner, ulong localPlayerId, GameActionType gameActionType)
        ConstructorInfo? ctor = contextType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                ParameterInfo[] ps = c.GetParameters();
                return ps.Length == 3 && ps[1].ParameterType == typeof(ulong);
            });
        if (ctor is null)
            throw new InvalidOperationException("HookPlayerChoiceContext 3-param ctor not found.");
        return ctor.Invoke(new object[] { owner, netId, gameActionType });
    }

    /// <summary>
    /// After card play, move the card from its current pile (Play or Hand) to Discard
    /// to maintain consistent pile state for subsequent turns.
    /// <c>OnPlay</c> moves the card to the Play pile (via <c>CardPileCmd.AddDuringManualCardPlay</c>
    /// called internally by <c>OnPlayWrapper</c>). Since we bypass <c>OnPlayWrapper</c>,
    /// we must handle the pile transition ourselves.
    /// </summary>
    private void MoveCardToDiscard(
        object cardModel,
        Type cardModelType,
        Type pileTypeEnum,
        object pileTypeDiscard,
        object player
    )
    {
        try
        {
            // Get Discard pile via PileType.Discard.GetPile(player).
            MethodInfo? getPileMi = pileTypeEnum.GetMethod(
                "GetPile",
                BindingFlags.Public | BindingFlags.Instance
            );
            if (getPileMi is null)
                return;
            object discardPile = getPileMi.Invoke(pileTypeDiscard, new object[] { player })!;

            // Remove from current pile (if any).
            object? currentPile = GetProperty(cardModel, "Pile");
            if (currentPile is not null)
            {
                // Try Remove(card) public method first, then RemoveInternal.
                MethodInfo? removePublicMi = currentPile.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.Instance
                ).FirstOrDefault(m =>
                    m.Name == "Remove"
                    && m.GetParameters().Length == 1
                    && !m.GetParameters()[0].ParameterType.Equals(typeof(bool))
                );
                MethodInfo? removeInternalMi = currentPile.GetType().GetMethod(
                    "RemoveInternal",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                );
                (removePublicMi ?? removeInternalMi)?.Invoke(currentPile, new object[] { cardModel });
            }

            // Add to discard pile via AddInternal(card, position=-1).
            MethodInfo? addInternalMi = discardPile.GetType().GetMethods(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            ).FirstOrDefault(m => m.Name == "AddInternal" && m.GetParameters().Length >= 1);
            if (addInternalMi is not null)
            {
                object?[] addArgs = addInternalMi.GetParameters().Length >= 2
                    ? new object?[] { cardModel, -1 }
                    : new object?[] { cardModel };
                addInternalMi.Invoke(discardPile, addArgs);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"warn: MoveCardToDiscard: {(ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex).Message}");
        }
    }

    /// <summary>
    /// Snapshot upstream's <c>CombatState</c> into a <see cref="Sts2Headless.DeterminismProbe.MidCombatRecord"/>.
    /// Reads fields via reflection to handle CombatManager._state at runtime.
    /// </summary>
    private Sts2Headless.DeterminismProbe.MidCombatRecord SnapshotUpstream(
        object upstreamState,
        int turn,
        string side
    )
    {
        // Player creature.
        object? playerProp = GetProperty(upstreamState, "Player") ?? GetProperty(upstreamState, "Players");
        object playerCreature;
        if (playerProp is System.Collections.IEnumerable enumPlayers)
            playerCreature = enumPlayers.Cast<object>().First();
        else
            playerCreature = playerProp!;

        // For upstream, Player has a Creature property or IS a creature.
        // Preserve the Player object before overwriting playerCreature with the Creature —
        // PlayerCombatState (which holds Energy) lives on Player, not Creature.
        // wave-50/A.2.b: without playerObj, GetProperty(playerCreature, "PlayerCombatState")
        // returns null (Creature doesn't carry that property) → Energy reads as 0 every turn.
        object playerObj = playerCreature;
        object? creatureObj = GetProperty(playerCreature, "Creature");
        if (creatureObj is not null)
            playerCreature = creatureObj;

        int playerHp = ToInt(GetProperty(playerCreature, "CurrentHp") ?? 0);
        int playerBlock = ToInt(GetProperty(playerCreature, "Block") ?? 0);

        // Player combat state for energy: read from Player (playerObj), not from Creature.
        object? playerCombatState = GetProperty(playerObj, "PlayerCombatState")
            ?? GetProperty(playerCreature, "PlayerCombatState");
        int energy = playerCombatState is not null
            ? ToInt(GetProperty(playerCombatState, "Energy") ?? 0)
            : 0;

        var playerPowers = ExtractUpstreamPowers(playerCreature);

        // Phase.
        object? phaseObj = GetProperty(upstreamState, "Phase") ?? GetProperty(upstreamState, "CurrentSide");
        string phase = phaseObj?.ToString() ?? "Unknown";

        // Enemies.
        object? enemiesObj = GetProperty(upstreamState, "Enemies")
            ?? GetProperty(upstreamState, "AliveEnemies");
        var enemies = new List<Sts2Headless.DeterminismProbe.EnemySnapshot>();
        if (enemiesObj is System.Collections.IEnumerable enemiesEnum)
        {
            foreach (object ec in enemiesEnum)
            {
                object? ecCreature = GetProperty(ec, "Creature");
                if (ecCreature is not null)
                    ec.GetType(); // no-op; creature is ec itself for some DLL versions
                object actualCreature = ecCreature ?? ec;

                int eHp = ToInt(GetProperty(actualCreature, "CurrentHp") ?? 0);
                int eBlock = ToInt(GetProperty(actualCreature, "Block") ?? 0);

                // MoveId from upstream: read Creature.Monster.NextMove.Id.
                // This is set by MonsterModel.RollMove (called in Phase 3 above) which
                // resolves INIT_MOVE ConditionalBranchState into the actual first MoveState.
                // E4 (wave-49/A.2): guard on null monster / null NextMove; warn on empty
                // so reflection misses don't silently mask INIT_MOVE divergences.
                string moveId = "";
                object? monsterForMove = GetProperty(actualCreature, "Monster")
                    ?? GetProperty(ec, "Monster");
                if (monsterForMove is not null)
                {
                    object? nextMoveObj = GetProperty(monsterForMove, "NextMove");
                    if (nextMoveObj is not null)
                    {
                        moveId = GetProperty(nextMoveObj, "Id")?.ToString() ?? "";
                        if (moveId.Length == 0)
                        {
                            Console.Error.WriteLine(
                                $"warn: SnapshotUpstream: NextMove.Id empty for enemy "
                                + $"at turn={turn} side={side} "
                                + $"(NextMove type={nextMoveObj.GetType().Name})");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"warn: SnapshotUpstream: Monster.NextMove is null for enemy "
                            + $"at turn={turn} side={side}");
                    }
                }
                else
                {
                    Console.Error.WriteLine(
                        $"warn: SnapshotUpstream: Creature.Monster is null for enemy "
                        + $"at turn={turn} side={side}; MoveId will be empty.");
                }

                // Intent: read from Monster.NextMove.Intents[0] (upstream AbstarctIntent).
                // IntentType is the enum; DamageCalc() gives raw damage; Repeats gives hit count.
                // DefendIntent / BuffIntent have no DamageCalc — guard accordingly.
                string intentKind = "Unknown";
                int dmgPerHit = 0, hitCount = 0, selfBlock = 0;
                if (monsterForMove is not null)
                {
                    object? nextMoveObj2 = GetProperty(monsterForMove, "NextMove");
                    if (nextMoveObj2 is not null)
                    {
                        object? intentsObj = GetProperty(nextMoveObj2, "Intents");
                        if (intentsObj is System.Collections.IEnumerable intentsEnum)
                        {
                            object? firstIntent = intentsEnum.Cast<object>().FirstOrDefault();
                            if (firstIntent is not null)
                            {
                                object? intentTypeObj = GetProperty(firstIntent, "IntentType");
                                intentKind = intentTypeObj?.ToString() ?? "Unknown";

                                // DamageCalc: Func<decimal>? property — invoke if present.
                                object? dmgCalcFn = GetProperty(firstIntent, "DamageCalc");
                                if (dmgCalcFn is not null)
                                {
                                    try
                                    {
                                        object? rawDmg = dmgCalcFn.GetType()
                                            .GetMethod("Invoke")!
                                            .Invoke(dmgCalcFn, null);
                                        if (rawDmg is not null)
                                            dmgPerHit = (int)(decimal)Convert.ChangeType(rawDmg, typeof(decimal));
                                    }
                                    catch { /* DamageCalc may be null-delegate on non-attack intents */ }
                                }

                                // Repeats: int property on MultiAttackIntent / SingleAttackIntent.
                                object? repeatsObj = GetProperty(firstIntent, "Repeats");
                                if (repeatsObj is not null)
                                    hitCount = ToInt(repeatsObj);

                                // SelfBlockGain: DefendIntent.BlockGain or similar.
                                object? sbObj = GetProperty(firstIntent, "BlockGain")
                                    ?? GetProperty(firstIntent, "SelfBlockGain");
                                if (sbObj is not null)
                                    selfBlock = ToInt(sbObj);
                            }
                        }
                    }
                }

                // Enemy name: read from creature's Model.Id.Entry to avoid calling
                // Creature.Name (which requires Godot LocString and crashes headless).
                string eName = "";
                object? modelObj = GetProperty(actualCreature, "Model");
                if (modelObj is not null)
                {
                    object? modelId = GetProperty(modelObj, "Id");
                    if (modelId is not null)
                    {
                        object? entryObj = GetProperty(modelId, "Entry");
                        eName = entryObj?.ToString() ?? modelId.ToString() ?? "";
                    }
                    if (eName.Length == 0)
                        eName = modelObj.GetType().Name;
                }
                else
                {
                    // Fallback: try Creature.Id (ModelId on the creature itself).
                    object? creatureId = GetProperty(ec, "Id");
                    if (creatureId is not null)
                    {
                        object? entryObj = GetProperty(creatureId, "Entry");
                        eName = entryObj?.ToString() ?? creatureId.ToString() ?? "";
                    }
                }

                var ePowers = ExtractUpstreamPowers(actualCreature);
                enemies.Add(new Sts2Headless.DeterminismProbe.EnemySnapshot(
                    eName, eHp, eBlock, moveId, intentKind, dmgPerHit, hitCount, selfBlock, ePowers));
            }
        }

        // RNG counter: read from state or player.
        int rngCounter = 0;
        object? rngCounterObj = GetProperty(upstreamState, "RngCounter")
            ?? GetProperty(playerCombatState ?? playerCreature, "RngCounter");
        if (rngCounterObj is not null)
            rngCounter = ToInt(rngCounterObj);

        return new Sts2Headless.DeterminismProbe.MidCombatRecord(
            Turn: turn,
            Side: side,
            Phase: phase,
            PlayerHp: playerHp,
            PlayerBlock: playerBlock,
            Energy: energy,
            PowerStacks: playerPowers,
            Enemies: enemies,
            RngCounter: rngCounter
        );
    }

    private static IReadOnlyList<Sts2Headless.DeterminismProbe.PowerStackEntry> ExtractUpstreamPowers(
        object creature
    )
    {
        var result = new List<Sts2Headless.DeterminismProbe.PowerStackEntry>();
        object? powersObj = GetProperty(creature, "Powers");
        if (powersObj is null)
            return result;
        foreach (object pw in (System.Collections.IEnumerable)powersObj)
        {
            object model = GetProperty(pw, "Model") ?? pw;
            string modelId = GetProperty(model, "Id")?.ToString() ?? model.GetType().Name;
            object? amount = GetProperty(pw, "Amount") ?? GetProperty(pw, "Stacks");
            int stacks = amount switch
            {
                int i => i, decimal d => (int)d, long l => (int)l, _ => 0,
            };
            result.Add(new Sts2Headless.DeterminismProbe.PowerStackEntry(modelId, stacks));
        }
        return result;
    }

    // ===== UpstreamEncounterRng path ======================================

    /// <summary>
    /// Drive the upstream encounter's <c>GenerateMonstersWithSlots(runState)</c>
    /// via reflection to get the seed-accurate monster+slot list. Used for
    /// encounters like <c>SlimesWeak</c> / <c>SlimesNormal</c> whose monster
    /// composition is RNG-determined per-seed.
    ///
    /// <para>
    /// Flow:
    /// 1. Fetch the canonical encounter from <c>ModelDb.Encounter&lt;T&gt;()</c>.
    /// 2. Call <c>encounter.ToMutable()</c> to get a mutable instance.
    /// 3. Call <c>encounter.GenerateMonstersWithSlots(runState)</c> — this
    ///    seeds <c>encounter._rng</c> from <c>runState.Rng.Seed + TotalFloor +
    ///    hash(encounter.Id.Entry)</c> and calls <c>GenerateMonsters()</c>.
    /// 4. Read the resulting <c>MonstersWithSlots</c> property.
    /// 5. For each (MonsterModel, slot?), call <c>model.ToMutable()</c> to
    ///    clone the catalog instance into a mutable one.
    /// </para>
    /// </summary>
    private List<(object mutableMonster, string? slot)> ResolveViaUpstreamEncounterRng(
        EncounterCatalog.EncounterPlan plan,
        object runState,
        Type modelDbType,
        MethodInfo modelDbMonsterGeneric,
        Type monsterModelType
    )
    {
        string typeName =
            plan.UpstreamTypeName
            ?? throw new InvalidOperationException(
                $"UpstreamEncounterRng plan for '{plan.EncounterId}' has null UpstreamTypeName."
            );

        Type encounterType = TypeOrThrow(typeName);
        Type encounterModelBaseType = TypeOrThrow("MegaCrit.Sts2.Core.Models.EncounterModel");

        // Step 1: fetch canonical encounter instance from ModelDb.
        MethodInfo? modelDbEncounterMethod = modelDbType.GetMethod(
            "Encounter",
            BindingFlags.Static | BindingFlags.Public
        );
        if (modelDbEncounterMethod is null)
        {
            throw new InvalidOperationException("ModelDb.Encounter not found.");
        }
        object canonicalEncounter =
            modelDbEncounterMethod.MakeGenericMethod(encounterType).Invoke(null, null)
            ?? throw new InvalidOperationException($"ModelDb.Encounter<{typeName}> returned null.");

        // Step 2: call encounter.ToMutable().
        object mutableEncounter =
            encounterModelBaseType
                .GetMethod("ToMutable", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(canonicalEncounter, null)
            ?? throw new InvalidOperationException($"{typeName}.ToMutable returned null.");

        // Step 3: call encounter.GenerateMonstersWithSlots(runState) — seeds encounter Rng
        // from runState.Rng.Seed + TotalFloor + hash(encounter.Id.Entry) and produces monster list.
        MethodInfo generateMi =
            encounterModelBaseType.GetMethod(
                "GenerateMonstersWithSlots",
                BindingFlags.Public | BindingFlags.Instance
            )
            ?? throw new InvalidOperationException(
                "EncounterModel.GenerateMonstersWithSlots not found."
            );
        generateMi.Invoke(mutableEncounter, new object[] { runState });

        // Step 4: read MonstersWithSlots property.
        PropertyInfo? monstersWithSlotsProp = encounterModelBaseType.GetProperty(
            "MonstersWithSlots",
            BindingFlags.Public | BindingFlags.Instance
        );
        if (monstersWithSlotsProp is null)
        {
            throw new InvalidOperationException(
                "EncounterModel.MonstersWithSlots property not found."
            );
        }
        object monstersWithSlots =
            monstersWithSlotsProp.GetValue(mutableEncounter)
            ?? throw new InvalidOperationException("MonstersWithSlots returned null.");

        // Step 5: for each (MonsterModel, slot?), extract the mutable monster.
        // The monsters returned by GenerateMonsters() are ALREADY mutable —
        // upstream's GenerateMonsters() calls model.ToMutable() internally.
        // Calling ToMutable() again would throw MutableModelException.
        var result = new List<(object mutableMonster, string? slot)>();
        foreach (object pair in (System.Collections.IEnumerable)monstersWithSlots)
        {
            // ValueTuple<MonsterModel, string?> — access via Item1 / Item2.
            Type pairType = pair.GetType();
            object monsterObj =
                pairType.GetField("Item1")?.GetValue(pair)
                ?? pairType.GetProperty("Item1")?.GetValue(pair)
                ?? throw new InvalidOperationException(
                    "Cannot read Item1 from MonstersWithSlots pair."
                );
            object? slotObj =
                pairType.GetField("Item2")?.GetValue(pair)
                ?? pairType.GetProperty("Item2")?.GetValue(pair);
            string? slot = slotObj as string;

            // Use the mutable monster directly — GenerateMonsters already called ToMutable().
            result.Add((monsterObj, slot));
        }
        return result;
    }

    // ===== Reflection helpers ============================================

    private Type TypeOrThrow(string fullName)
    {
        Type? t = _sts2.GetType(fullName);
        if (t is null)
        {
            throw new InvalidOperationException(
                $"Upstream type '{fullName}' not found in sts2.dll."
            );
        }
        return t;
    }

    private static object GetStaticProperty(Type t, string name)
    {
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
        if (p is null)
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Static | BindingFlags.Public);
            if (f is null)
            {
                throw new InvalidOperationException(
                    $"Static member '{name}' on {t.FullName} not found."
                );
            }
            return f.GetValue(null)
                ?? throw new InvalidOperationException(
                    $"Static field {t.FullName}.{name} is null."
                );
        }
        return p.GetValue(null)
            ?? throw new InvalidOperationException($"Static property {t.FullName}.{name} is null.");
    }

    private static object? GetProperty(object instance, string name)
    {
        Type t = instance.GetType();
        PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p is not null)
            return p.GetValue(instance);
        FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return f?.GetValue(instance);
    }

    private static void InvokeMethod(Type t, object? instance, string name, object?[] args)
    {
        MethodInfo? mi = t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            )
            .Where(m => m.Name == name && m.GetParameters().Length == args.Length)
            .FirstOrDefault();
        if (mi is null)
        {
            throw new InvalidOperationException(
                $"{t.FullName}.{name}({args.Length} args) not found."
            );
        }
        mi.Invoke(instance, args);
    }

    private static int ToInt(object value) =>
        value switch
        {
            int i => i,
            long l => (int)l,
            uint u => (int)u,
            decimal d => (int)d,
            _ => Convert.ToInt32(value),
        };

    private static object MakeReadOnlyList(Type elementType, object[] items)
    {
        // Build a List<T> of the element type and return it (works for
        // IReadOnlyList<T> parameters via covariance).
        Type listType = typeof(List<>).MakeGenericType(elementType);
        object list = Activator.CreateInstance(listType)!;
        MethodInfo addMethod = listType.GetMethod("Add")!;
        foreach (object item in items)
        {
            addMethod.Invoke(list, new[] { item });
        }
        return list;
    }
}

/// <summary>
/// Flex-predicate reflection helpers for tolerating upstream API drift between
/// pinned (v0.103.2) and live (v0.105.1) sts2.dll versions.
///
/// <para>
/// The drift mode this addresses: upstream adds a NEW parameter to an existing
/// constructor (or method) — the old exact-arity <c>Single(c =&gt; c.GetParameters().Length == N)</c>
/// pattern breaks with <c>Sequence contains no matching element</c>. The flex
/// approach matches by <b>named-parameter subset</b>: the caller declares which
/// parameter names + values are REQUIRED; any extras on the live ctor are filled
/// from supplemental named values (if supplied) or a type-driven flex default
/// (null for reference types, empty <c>List&lt;T&gt;</c> for
/// <c>IReadOnlyList&lt;T&gt;</c>, <c>default(T)</c> for value types).
/// </para>
///
/// <para>
/// <b>Why a helper class:</b> Wave 6 dispatch prompt constraint — refactor must
/// touch a small number of files. We co-locate the helper in this file to keep
/// the partition tight (engineer owns <c>UpstreamDriver.cs</c> only; no new
/// directory needed). The class is <c>file</c>-scoped so it cannot leak into
/// other compilation units.
/// </para>
/// </summary>
file static class ReflectionFlex
{
    /// <summary>
    /// Get the value of an instance property by name, or <see langword="null"/>
    /// if the property does not exist or is not readable. Tolerates upstream
    /// renames / removals.
    /// </summary>
    public static object? TryGetProperty(object instance, string name)
    {
        Type t = instance.GetType();
        PropertyInfo? p = t.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        return p?.CanRead == true ? p.GetValue(instance) : null;
    }

    /// <summary>
    /// Locate a constructor on <paramref name="type"/> whose parameters are a
    /// SUPERSET of <paramref name="requiredNames"/>, and build an argument
    /// array matched by parameter name from <paramref name="namedValues"/>.
    /// Extras (parameters present on the ctor but absent from
    /// <paramref name="namedValues"/>) get a flex default.
    ///
    /// <para>
    /// If multiple ctors match, the one with the FEWEST extra parameters wins
    /// (the most specific). Ties broken by declaration order.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> with a diagnostic message
    /// (listing every ctor's signature) if no ctor matches.
    /// </para>
    /// </summary>
    public static (ConstructorInfo Ctor, object?[] Args) FindCtorByParameterNames(
        Type type,
        BindingFlags flags,
        IReadOnlyDictionary<string, object?> namedValues,
        IReadOnlyList<string> requiredNames
    )
    {
        ConstructorInfo[] ctors = type.GetConstructors(flags);
        var candidates = new List<(ConstructorInfo Ctor, ParameterInfo[] Params, int Extras)>();
        foreach (ConstructorInfo c in ctors)
        {
            ParameterInfo[] ps = c.GetParameters();
            string[] paramNames = ps.Select(p => p.Name ?? "").ToArray();
            bool allRequiredPresent = requiredNames.All(rn =>
                paramNames.Any(pn => string.Equals(pn, rn, StringComparison.Ordinal))
            );
            if (!allRequiredPresent)
            {
                continue;
            }
            int extras = ps.Length - requiredNames.Count;
            candidates.Add((c, ps, extras));
        }
        if (candidates.Count == 0)
        {
            string available = string.Join(
                "; ",
                ctors.Select(c =>
                    $"({c.GetParameters().Length}: {string.Join(",", c.GetParameters().Select(p => p.Name))})"
                )
            );
            throw new InvalidOperationException(
                $"ReflectionFlex.FindCtorByParameterNames: no ctor on {type.FullName} "
                    + $"covers required names [{string.Join(",", requiredNames)}]. "
                    + $"Available ctors: {available}."
            );
        }
        // Most-specific = fewest extras.
        candidates.Sort((a, b) => a.Extras.CompareTo(b.Extras));
        (ConstructorInfo chosen, ParameterInfo[] chosenParams, _) = candidates[0];

        object?[] args = new object?[chosenParams.Length];
        for (int i = 0; i < chosenParams.Length; i++)
        {
            string paramName = chosenParams[i].Name ?? "";
            if (namedValues.TryGetValue(paramName, out object? namedValue))
            {
                args[i] = namedValue;
            }
            else
            {
                args[i] = FlexDefault(chosenParams[i].ParameterType);
            }
        }
        return (chosen, args);
    }

    /// <summary>
    /// Type-driven default for an unsupplied constructor parameter:
    /// <list type="bullet">
    ///   <item><c>null</c> for reference types (including
    ///     <c>IReadOnlyList&lt;T&gt;</c> when null is acceptable).</item>
    ///   <item>An empty <c>List&lt;T&gt;</c> when the parameter expects
    ///     <c>IReadOnlyList&lt;T&gt;</c> or <c>IEnumerable&lt;T&gt;</c> — many
    ///     upstream ctors null-check these and we'd rather supply empty.</item>
    ///   <item><c>default(T)</c> for value types via
    ///     <see cref="Activator.CreateInstance(Type)"/>.</item>
    /// </list>
    /// </summary>
    private static object? FlexDefault(Type t)
    {
        // IReadOnlyList<T> / IEnumerable<T> / ICollection<T> → empty List<T>.
        if (t.IsGenericType)
        {
            Type def = t.GetGenericTypeDefinition();
            if (
                def == typeof(IReadOnlyList<>)
                || def == typeof(IEnumerable<>)
                || def == typeof(ICollection<>)
                || def == typeof(IReadOnlyCollection<>)
                || def == typeof(IList<>)
                || def == typeof(List<>)
            )
            {
                Type elemType = t.GetGenericArguments()[0];
                Type listType = typeof(List<>).MakeGenericType(elemType);
                return Activator.CreateInstance(listType);
            }
        }
        if (t.IsValueType)
        {
            return Activator.CreateInstance(t);
        }
        return null;
    }
}
