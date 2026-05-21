using System.Collections.Generic;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Convenience <see cref="IActionObserver"/> backed by a caller-visible
/// <c>List&lt;IAction&gt;</c>. Records appended in execution order.
///
/// <para>
/// Test ergonomic: use the <see cref="Create"/> factory to get both the
/// observer and the underlying log in one expression:
/// <code>
/// var obs = ListActionObserver.Create(out var log);
/// var ctx = new ExecutionContext(clock, rng, hooks, queue, obs);
/// // ... run actions ...
/// Assert.Collection(log, ...);
/// </code>
/// </para>
/// </summary>
public sealed class ListActionObserver : IActionObserver
{
    private readonly List<IAction> _log;

    public ListActionObserver(List<IAction> log)
    {
        System.ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public void Record(IAction action) => _log.Add(action);

    public static ListActionObserver Create(out List<IAction> log)
    {
        log = new List<IAction>();
        return new ListActionObserver(log);
    }
}
