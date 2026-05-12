namespace Sts2Q1ConsoleHost;

/// <summary>
/// <para>
/// Empty-bodied interface surface for the 12 SceneTree-coupled singletons
/// that <c>CombatManager.StartCombatInternal</c> references. Method
/// signatures are placeholders only — <b>P-1.5-1.β fills them in.</b>
/// </para>
///
/// <para>
/// Enumerated from <c>engine/headless/docs/phase1-gate-report.md</c>
/// (Approach A blockers section) + <c>docs/specs/modules/stub-pin-harness.md</c>:
/// </para>
/// <list type="number">
///   <item><c>NRunMusicController.Instance</c> — audio control during combat start/end.</item>
///   <item><c>NCombatRoom.Instance</c> — enemy positioning + per-creature scene-node refs.</item>
///   <item><c>NModalContainer.Instance</c> — modal-dialog stack (e.g. retry / surrender).</item>
///   <item><c>NCombatStartBanner.Create()</c> — banner factory (combat-start UI animation).</item>
///   <item><c>NCombatRulesFtue</c> — tutorial gating (first-combat UI flow).</item>
///   <item><c>Cmd.CustomScaledWait</c> — animation-scaled wait command.</item>
///   <item><c>SaveManager.Instance</c> — persistence hooks (run save / unlock writes).</item>
///   <item><c>RunManager.Instance.ActionExecutor</c> — global action queue executor.</item>
///   <item><c>NetCombatCardDb.Instance</c> — combat card database (multiplayer-aware).</item>
///   <item><c>await</c> point #1 — first <c>await</c> in <c>StartCombatInternal</c>.</item>
///   <item><c>await</c> point #2 — second <c>await</c>.</item>
///   <item><c>await</c> point #3 — third <c>await</c>.</item>
/// </list>
///
/// <para>
/// <b>α (this sub-stream) declares signatures only.</b> No bodies. Bodies
/// are P-1.5-1.β scope. The <c>Pinned&lt;TStub&gt;</c> harness wrapping
/// these is P-1.5-1.γ scope. Wiring into the per-step probe driver is
/// P-1.5-1.δ scope.
/// </para>
///
/// <para>
/// Argument / return-types are deliberately <c>object?</c> at α — concrete
/// upstream types reflect into the host via <see cref="UpstreamBinding"/>
/// at runtime, and the β stub bodies will downcast as needed. Locking
/// concrete signatures here would force α to depend on upstream
/// assemblies at compile time, which the project explicitly avoids.
/// </para>
/// </summary>
public interface ISceneTreeSingletons
{
    /// <summary>
    /// Surface for <c>NRunMusicController.Instance</c>. β stubs to a silent
    /// no-op (see Phase-1.5 plan, line "Stub strategy: No-op audio").
    /// </summary>
    object? RunMusicController { get; }

    /// <summary>
    /// Surface for <c>NCombatRoom.Instance</c>. β stubs to a deterministic
    /// enemy-position table; no animation.
    /// </summary>
    object? CombatRoom { get; }

    /// <summary>
    /// Surface for <c>NModalContainer.Instance</c>. β stubs to a no-op
    /// modal stack.
    /// </summary>
    object? ModalContainer { get; }

    /// <summary>
    /// Surface for <c>NCombatStartBanner.Create()</c>. β returns a sentinel
    /// banner; no UI animation.
    /// </summary>
    object? CreateCombatStartBanner();

    /// <summary>
    /// Surface for <c>NCombatRulesFtue</c>. β always allows tutorial gates.
    /// </summary>
    object? CombatRulesFtue { get; }

    /// <summary>
    /// Surface for <c>Cmd.CustomScaledWait</c>. β bypasses the wait
    /// synchronously (no actual wall-clock delay).
    /// </summary>
    object? RunCustomScaledWait(object? arg);

    /// <summary>
    /// Surface for <c>SaveManager.Instance</c>. β stubs to no-op
    /// persistence (writes discarded).
    /// </summary>
    object? SaveManager { get; }

    /// <summary>
    /// Surface for <c>RunManager.Instance.ActionExecutor</c>. β routes
    /// directly to Q1's M6d action queue.
    /// </summary>
    object? RunManagerActionExecutor { get; }

    /// <summary>
    /// Surface for <c>NetCombatCardDb.Instance</c>. β binds to Q1's
    /// existing <c>CardCatalog</c>.
    /// </summary>
    object? NetCombatCardDb { get; }

    /// <summary>
    /// Surface for <see cref="StartCombatInternal"/> await point #1.
    /// β returns a completed task / patched continuation.
    /// </summary>
    object? AwaitPoint1();

    /// <summary>
    /// Surface for <see cref="StartCombatInternal"/> await point #2.
    /// β returns a completed task / patched continuation.
    /// </summary>
    object? AwaitPoint2();

    /// <summary>
    /// Surface for <see cref="StartCombatInternal"/> await point #3.
    /// β returns a completed task / patched continuation.
    /// </summary>
    object? AwaitPoint3();
}
