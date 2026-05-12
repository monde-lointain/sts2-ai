// ExecutionContext — the shared services bag passed into IAction.Execute and
// HookRegistry.Fire.
//
// Naming note: the BCL has System.Threading.ExecutionContext, so callers that
// import both System.Threading and Sts2Headless.Domain.Actions will see an
// ambiguity. Resolve via either:
//   using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;
// or fully-qualify at use site. We keep the name `ExecutionContext` because
// docs/specs/modules/action-queue.md mandates it; deviating from the spec
// would be a worse cost than the alias chore.
//
// Class (not struct) by design:
//   - It holds reference types (IClock, IRngSource, HookRegistry, ActionQueue)
//     anyway, so a struct would carry the same reference cost without gaining
//     value semantics.
//   - Passing by value through deep recursion of cascading actions would copy
//     four pointers per frame — wasteful for nothing.
//   - Future combat/run state references will hang off here; we want a single
//     identity for the duration of a Drain.
//
// Mutable state owned BY the context itself (other than reference targets)
// should stay minimal — combat state lives on the future ICombatContext
// reference, not on ExecutionContext fields, so cheap-clone (S17) snapshots
// the state graph, not the services bag.

using System;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Services and queue/registry handles available to an executing <see cref="IAction"/>.
/// One instance lives for the duration of an action-queue drain; passed by reference
/// (it's a class) into Execute and Fire. Determinism ports — <see cref="IClock"/> and
/// <see cref="IRngSource"/> — come from S1 (Sts2Headless.Domain.Determinism), not from
/// concrete Rng/LogicalClock types, so action code stays agnostic of the kernel
/// implementation (per Q1-ADR-001 hexagonal discipline).
/// </summary>
public sealed class ExecutionContext
{
    public IClock Clock { get; }
    public IRngSource Rng { get; }
    public HookRegistry Hooks { get; }
    public ActionQueue Queue { get; }

    public ExecutionContext(IClock clock, IRngSource rng, HookRegistry hooks, ActionQueue queue)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Rng = rng ?? throw new ArgumentNullException(nameof(rng));
        Hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }
}
