using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using DomainExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// The trio of <see cref="HookRegistry"/>, <see cref="ActionQueue"/>, and
/// <see cref="DomainExecutionContext"/> shared by the engine across one combat's
/// turn lifecycle. Immutable; passed at ctor time so <see cref="CombatContext"/>
/// is fully constructed on construction.
/// </summary>
public sealed record HookPlumbing(HookRegistry Hooks, ActionQueue Queue, DomainExecutionContext Context)
{
    /// <summary>
    /// Builds inert plumbing for <b>snapshot / inspection contexts</b>
    /// (ControlPlaneSession state restore, hand-constructed unit tests).
    /// The returned registry has zero subscribers; the queue is empty.
    ///
    /// <para>
    /// <b>Do not pass the resulting <see cref="CombatContext"/> to
    /// <see cref="CombatEngine.StartPlayerTurn"/>,
    /// <see cref="CombatEngine.PlayerPlayCard"/>,
    /// <see cref="CombatEngine.EndPlayerTurn"/>, or
    /// <see cref="CombatEngine.EnemyTurn"/></b> — hook-driven powers
    /// (Ritual, BloodVial, OnApplied bridges, etc.) will silently no-op
    /// against the empty registry, producing semantically-broken combat.
    /// Snapshot contexts are read-only.
    /// </para>
    /// </summary>
    public static HookPlumbing Empty(IClock clock, IRngSource rng)
    {
        var hooks = new HookRegistry();
        var queue = new ActionQueue();
        return new HookPlumbing(hooks, queue, new DomainExecutionContext(clock, rng, hooks, queue));
    }
}
