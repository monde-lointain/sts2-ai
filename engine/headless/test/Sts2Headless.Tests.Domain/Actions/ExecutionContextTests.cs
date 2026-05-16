// Tests for ExecutionContext — the shared services bag passed into IAction.Execute
// and HookRegistry.Fire. Holds IClock and IRngSource (M5 ports) plus a HookRegistry
// reference. Designed cheap-clone-friendly: ExecutionContext itself is a class
// (small, holds references) but mutable state hung off it should live in
// value-type / persistent-collection fields owned elsewhere.

using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Actions;

public class ExecutionContextTests
{
    [Fact]
    public void ConstructorWiresUpServices()
    {
        var clock = new LogicalClock();
        var rng = new Rng(42u);
        var registry = new HookRegistry();
        var queue = new ActionQueue();

        var ctx = new ExecutionContext(clock, rng, registry, queue);

        Assert.Same(clock, ctx.Clock);
        Assert.Same(rng, ctx.Rng);
        Assert.Same(registry, ctx.Hooks);
        Assert.Same(queue, ctx.Queue);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var clock = new LogicalClock();
        var rng = new Rng(0u);
        var registry = new HookRegistry();
        var queue = new ActionQueue();

        Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext(null!, rng, registry, queue)
        );
        Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext(clock, null!, registry, queue)
        );
        Assert.Throws<ArgumentNullException>(() => new ExecutionContext(clock, rng, null!, queue));
        Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext(clock, rng, registry, null!)
        );
    }

    [Fact]
    public void IClockIsExposedAsInterfaceNotConcrete()
    {
        // Per S4 constraint: take IClock/IRngSource from Determinism, not Rng concrete.
        var clock = new LogicalClock();
        var rng = new Rng(0u);
        var ctx = new ExecutionContext(clock, rng, new HookRegistry(), new ActionQueue());

        IClock asClock = ctx.Clock;
        IRngSource asRng = ctx.Rng;
        Assert.NotNull(asClock);
        Assert.NotNull(asRng);
    }
}
