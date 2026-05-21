using System;
using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using DomainExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Shared envelope for the four hook-fire sites in the combat engine.
/// Each site fires a different thing (a registered <see cref="HookType"/>,
/// a card <c>OnPlay</c> callback, or a per-id <see cref="HookType.AfterDeath"/>
/// call), but every site has the same structure:
/// <list type="number">
///   <item>Attach a <see cref="ListActionObserver"/> to the execution context.</item>
///   <item>Invoke the caller-supplied <paramref name="fire"/> callback.</item>
///   <item>Drain <see cref="HookPlumbing.Queue"/>.</item>
///   <item>Replay the accumulated log via <see cref="EffectDispatcher.Apply"/>.</item>
/// </list>
/// The <paramref name="fire"/> callback is per-site; only the envelope is shared.
/// </summary>
internal static class HookFireSession
{
    /// <summary>
    /// Run one hook-fire envelope.
    /// </summary>
    /// <param name="plumbing">Per-combat plumbing (hooks + queue + base context).</param>
    /// <param name="dispatch">Per-call dispatch context (target resolution).</param>
    /// <param name="combatCtx">Live combat context for effect application.</param>
    /// <param name="fire">
    /// Per-site callback that fires the hook or card body into
    /// <c>execCtxObs</c> (an <see cref="DomainExecutionContext"/> with the
    /// observer attached). The callback must not drain the queue — this
    /// method drains after the callback returns.
    /// </param>
    internal static void Run(
        HookPlumbing plumbing,
        EffectDispatcher.DispatchContext dispatch,
        ICombatContext combatCtx,
        Action<DomainExecutionContext> fire)
    {
        var obs = ListActionObserver.Create(out List<IAction> log);
        var execCtxObs = new DomainExecutionContext(
            plumbing.Context.Clock,
            plumbing.Context.Rng,
            plumbing.Context.Hooks,
            plumbing.Context.Queue,
            obs);
        fire(execCtxObs);
        plumbing.Queue.Drain(execCtxObs);
        foreach (IAction action in log)
        {
            EffectDispatcher.Apply(action, combatCtx, dispatch);
        }
    }
}
