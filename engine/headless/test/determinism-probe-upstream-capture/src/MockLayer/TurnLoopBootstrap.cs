using System;
using System.Reflection;

namespace Sts2Headless.UpstreamCapture.MockLayer;

/// <summary>
/// Headless-safe wrapper for upstream's turn-loop bootstrap state.
///
/// <para>
/// Per wave-50/A.0 survey: <c>LocalContext</c> is a pure static class — no
/// instance, no singleton getter; <c>NetId</c> is set via reflection
/// (<c>SetValue(null, ...)</c>) and restored on dispose.
/// <c>RunManager.Instance.SetUpTest(runState, gameService, disableCombatStateSync)</c>
/// is upstream's built-in test-mode initializer — populates
/// <c>ActionQueueSynchronizer</c>, <c>ActionExecutor</c>, and
/// <c>ChecksumTracker (IsEnabled=false)</c> with pure C# objects.
/// <c>NRunMusicController.Instance</c> is null headless and all call sites
/// use <c>?.</c> guarded by <c>NonInteractiveMode.IsActive</c> —
/// no action required.
/// </para>
///
/// <para>
/// Usage: construct before entering the turn loop; dispose (in a
/// <c>finally</c> block or via <c>using</c>) after capture completes.
/// </para>
/// </summary>
internal sealed class TurnLoopBootstrap : IDisposable
{
    private readonly Assembly _sts2;
    private readonly ulong? _prevNetId;
    private bool _disposed;

    /// <summary>
    /// Sets <c>LocalContext.NetId = netId</c> and calls
    /// <c>RunManager.Instance.SetUpTest(runState, new NetSingleplayerGameService(),
    /// disableCombatStateSync: true)</c> via reflection.
    /// </summary>
    /// <param name="sts2">The upstream sts2 assembly (from <c>UpstreamDriver</c>).</param>
    /// <param name="runState">The constructed <c>RunState</c> object (already created by caller).</param>
    /// <param name="netId">Player net id; defaults to 1UL (singleplayer canonical id).</param>
    internal TurnLoopBootstrap(Assembly sts2, object runState, ulong netId = 1UL)
    {
        _sts2 = sts2;

        // (1) Capture and set LocalContext.NetId via reflection.
        // LocalContext is a pure static class — SetValue(null, ...) targets the
        // static property. The property is public with get; set; so no special flags needed.
        Type localContextType = GetTypeOrThrow("MegaCrit.Sts2.Core.Context.LocalContext");
        PropertyInfo netIdProp = localContextType.GetProperty(
            "NetId",
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException(
            "LocalContext.NetId property not found in upstream assembly."
        );
        _prevNetId = (ulong?)netIdProp.GetValue(null);
        netIdProp.SetValue(null, (ulong?)netId);

        // (2) Construct NetSingleplayerGameService via reflection.
        // Plain C# class — parameterless implicit constructor, no Godot dependencies.
        Type singleplayerSvcType = GetTypeOrThrow(
            "MegaCrit.Sts2.Core.Multiplayer.NetSingleplayerGameService"
        );
        object singleplayerSvc = Activator.CreateInstance(singleplayerSvcType)
            ?? throw new InvalidOperationException(
                "Failed to construct NetSingleplayerGameService."
            );

        // (3) Call RunManager.Instance.SetUpTest(runState, gameService,
        //     disableCombatStateSync: true, shouldSave: false).
        // This populates ActionQueueSynchronizer, ActionExecutor, ActionQueueSet, and
        // ChecksumTracker (IsEnabled=false for singleplayer). All pure C# — no Godot.
        // Signature: SetUpTest(RunState state, INetGameService gameService,
        //                      bool disableCombatStateSync = true, bool shouldSave = false)
        Type runManagerType = GetTypeOrThrow("MegaCrit.Sts2.Core.Runs.RunManager");
        object runManagerInstance = runManagerType
            .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)
            ?? throw new InvalidOperationException("RunManager.Instance returned null.");

        MethodInfo setUpTestMi = runManagerType.GetMethod(
            "SetUpTest",
            BindingFlags.Public | BindingFlags.Instance
        ) ?? throw new InvalidOperationException(
            "RunManager.SetUpTest method not found in upstream assembly."
        );

        // Pass all 4 parameters explicitly (2 required + 2 optional-with-defaults).
        // disableCombatStateSync=true: CombatStateSynchronizer.IsDisabled=true (headless-safe).
        // shouldSave=false: no file I/O attempted during the run.
        setUpTestMi.Invoke(
            runManagerInstance,
            new object[] { runState, singleplayerSvc, /* disableCombatStateSync */ true, /* shouldSave */ false }
        );
    }

    /// <summary>
    /// Calls <c>RunManager.Instance.CleanUp(graceful: true)</c> then restores
    /// <c>LocalContext.NetId</c> to its prior value.
    ///
    /// <para>
    /// <c>CleanUp</c> internally: resets <c>ActionQueueSet</c>, disposes synchronizers,
    /// calls <c>CombatManager.Instance.Reset(graceful)</c> (pure C# path when
    /// <c>TestMode.IsOn=true</c>), and nulls <c>RunManager.State</c>. All Godot
    /// node calls (NAudioManager, NOverlayStack, etc.) are <c>?.null-safe</c> and
    /// are no-ops headless. <c>CleanUp</c>'s own finally block sets
    /// <c>LocalContext.NetId = null</c>; we then restore to our captured prior value.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            // (1) CleanUp RunManager — resets AQS, ActionExecutor, etc.
            // graceful=true: iterates creatures for Reset/RemoveCreature before null-ing _state.
            Type runManagerType = GetTypeOrThrow("MegaCrit.Sts2.Core.Runs.RunManager");
            object runManagerInstance = runManagerType
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            MethodInfo cleanUpMi = runManagerType.GetMethod(
                "CleanUp",
                BindingFlags.Public | BindingFlags.Instance
            ) ?? throw new InvalidOperationException("RunManager.CleanUp not found.");
            cleanUpMi.Invoke(runManagerInstance, new object[] { /* graceful */ true });
        }
        catch (Exception ex)
        {
            // Log but continue — LocalContext.NetId must be restored regardless.
            Console.Error.WriteLine(
                $"warn: TurnLoopBootstrap.Dispose: RunManager.CleanUp threw: "
                + $"{(ex is TargetInvocationException tie ? tie.InnerException ?? tie : ex).Message}"
            );
        }
        finally
        {
            // (2) Restore LocalContext.NetId to its pre-bootstrap value.
            // CleanUp already sets NetId=null in its own finally block; we write again
            // to honour the pre-bootstrap state (normally null, but may differ in tests).
            Type localContextType = GetTypeOrThrow("MegaCrit.Sts2.Core.Context.LocalContext");
            PropertyInfo netIdProp = localContextType.GetProperty(
                "NetId",
                BindingFlags.Public | BindingFlags.Static
            )!;
            netIdProp.SetValue(null, _prevNetId);
        }
    }

    private Type GetTypeOrThrow(string fullName)
    {
        Type? t = _sts2.GetType(fullName);
        if (t is null)
        {
            throw new InvalidOperationException(
                $"TurnLoopBootstrap: upstream type '{fullName}' not found in sts2 assembly."
            );
        }
        return t;
    }
}
