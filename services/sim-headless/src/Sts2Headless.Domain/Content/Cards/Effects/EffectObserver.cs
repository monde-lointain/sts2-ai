using System;
using System.Collections.Generic;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// Optional observer for effect actions (<see cref="DealDamageAction"/>,
/// <see cref="GainBlockAction"/>, etc.). When an observer is attached via
/// <see cref="Attach"/>, every effect action invokes <see cref="Record"/> from its
/// <c>Execute</c>; combat-side glue does not need to set one for production.
///
/// <para>
/// <b>Why this exists:</b> S5 ships card OnPlay implementations that enqueue effect
/// actions. The natural way to verify those values is to drain the queue and observe.
/// <see cref="ActionQueue"/> is sealed and exposes no peek/snapshot, and S4 is fenced
/// from modification per the S5 prompt — so we provide the observation point inside
/// our own effect-action records, where it is scoped to this folder.
/// </para>
///
/// <para>
/// <b>Concurrency:</b> the observer slot is <see cref="ThreadStaticAttribute"/>,
/// matching Q1-ADR-008's single-threaded domain. Tests that run in parallel each
/// install their own observer in their own thread.
/// </para>
///
/// <para>
/// <b>Use from tests:</b>
/// <code>
/// using (EffectObserver.Attach(out var log)) {
///     card.OnPlay(ctx, target);
///     ctx.Queue.Drain(ctx);
/// }
/// // log now contains the actions in execution order.
/// </code>
/// </para>
/// </summary>
public static class EffectObserver
{
    [ThreadStatic]
    private static List<IAction>? _log;

    /// <summary>
    /// Install <paramref name="log"/> as the active observer for the current thread.
    /// Returns a scope object — dispose it (or let <c>using</c> dispose it) to detach.
    /// Attaching while another observer is active throws to catch nested-scope bugs.
    /// </summary>
    public static IDisposable Attach(out List<IAction> log)
    {
        if (_log is not null)
        {
            throw new InvalidOperationException(
                "EffectObserver is already attached on this thread; nested attach is not supported.");
        }
        log = new List<IAction>();
        _log = log;
        return new Scope();
    }

    /// <summary>
    /// Record <paramref name="action"/> if an observer is attached, else no-op.
    /// Called by every effect action's <c>Execute</c>.
    /// </summary>
    internal static void Record(IAction action)
    {
        _log?.Add(action);
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _log = null;
    }
}
