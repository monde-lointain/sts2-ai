// HookContext — payload passed into every HookHandler. Carries the ambient
// ExecutionContext plus per-hook-shape mutable payload fields (added per
// ADR-030 §6 for the boolean-aggregation convention).
//
// Struct for cheap pass-by-value through the hot Fire loop. Reference-typed
// payload fields (DeferCombatEnd, etc.) are mutable through the heap object;
// the struct itself stays readonly so a `HookContext` value can't be silently
// reassigned mid-iteration.

using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Per-hook-firing payload. Carries the ambient <see cref="Execution"/>
/// context plus optional per-hook fields:
/// <list type="bullet">
///   <item>
///     <see cref="DyingCreatureId"/> — populated when firing
///     <see cref="HookType.AfterDeath"/> (or the reserved
///     <see cref="HookType.BeforeDeath"/>); the id of the creature whose death
///     transition is being announced. <c>null</c> for non-death hooks.
///   </item>
///   <item>
///     <see cref="DeferCombatEnd"/> — boolean-aggregation flag for
///     <see cref="HookType.ShouldStopCombatFromEnding"/> per ADR-030 §6.
///     Caller (<see cref="Combat.CombatEngine.CheckCombatEnd"/>) allocates a
///     <c>bool[1]</c> initialised to <c>false</c>; subscribers set
///     <c>DeferCombatEnd[0] = true</c> to veto the combat-end transition;
///     caller reads the flag after <see cref="HookRegistry.Fire"/> returns.
///     <c>null</c> when not consulting that hook.
///   </item>
/// </list>
/// <para>
/// Existing call sites that construct a <see cref="HookContext"/> with the
/// 1-arg constructor (just an <see cref="ExecutionContext"/>) get both
/// optional fields as <c>null</c>; backward-compatible.
/// </para>
/// </summary>
public readonly struct HookContext
{
    /// <summary>Ambient action/queue/clock/rng/hook-registry bag.</summary>
    public ExecutionContext Execution { get; }

    /// <summary>
    /// Creature whose death transition is being announced. Populated by
    /// <see cref="Combat.CombatEngine"/> when firing
    /// <see cref="HookType.AfterDeath"/> (or the reserved
    /// <see cref="HookType.BeforeDeath"/>). <c>null</c> for non-death hooks.
    /// </summary>
    public uint? DyingCreatureId { get; }

    /// <summary>
    /// Boolean-aggregation veto flag for
    /// <see cref="HookType.ShouldStopCombatFromEnding"/>. Allocated by the
    /// firing site as a single-element array so subscribers can OR-mutate
    /// <c>[0] = true</c> to defer the combat-end transition. <c>null</c> for
    /// hooks that don't consult a veto. See ADR-030 §6.
    /// </summary>
    public bool[]? DeferCombatEnd { get; }

    /// <summary>
    /// Construct with the ambient context and optional per-hook payload. Pass
    /// <paramref name="dyingCreatureId"/> when firing AfterDeath/BeforeDeath;
    /// pass <paramref name="deferCombatEnd"/> when firing
    /// ShouldStopCombatFromEnding (a fresh <c>bool[1]</c>). Existing call
    /// sites that pass only the <see cref="Actions.ExecutionContext"/> get
    /// both optional fields as <c>null</c> — backward-compatible.
    /// </summary>
    public HookContext(
        ExecutionContext execution,
        uint? dyingCreatureId = null,
        bool[]? deferCombatEnd = null
    )
    {
        Execution = execution;
        DyingCreatureId = dyingCreatureId;
        DeferCombatEnd = deferCombatEnd;
    }
}
