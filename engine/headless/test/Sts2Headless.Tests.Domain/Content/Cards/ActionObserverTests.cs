using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Tests for <see cref="ListActionObserver"/> / <see cref="IActionObserver"/>:
/// per-context observation; no-op when no observer; independent contexts don't
/// interfere with each other.
/// </summary>
public class ActionObserverTests
{
    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    [Fact]
    public void No_observer_on_context_is_silent_no_op()
    {
        ExecutionContext ctx = NewCtx();
        // No observer attached; Execute should be a no-op for observation.
        new DealDamageAction(6, new global::Sts2Headless.Domain.Combat.CreatureId(1u)).Execute(ctx);
        // No exception, nothing to assert except absence of crash.
    }

    [Fact]
    public void Observer_captures_all_executed_actions_in_order()
    {
        var obs = ListActionObserver.Create(out List<IAction> log);
        ExecutionContext ctx = new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue(), obs);
        new DealDamageAction(6, new global::Sts2Headless.Domain.Combat.CreatureId(1u)).Execute(ctx);
        new GainBlockAction(5).Execute(ctx);
        new DrawCardsAction(2).Execute(ctx);
        Assert.Equal(3, log.Count);
        Assert.IsType<DealDamageAction>(log[0]);
        Assert.IsType<GainBlockAction>(log[1]);
        Assert.IsType<DrawCardsAction>(log[2]);
    }

    [Fact]
    public void Two_independent_contexts_each_observe_only_their_own_actions()
    {
        // Per-context observers must not bleed into each other.
        var obs1 = ListActionObserver.Create(out List<IAction> log1);
        ExecutionContext ctx1 = new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue(), obs1);

        var obs2 = ListActionObserver.Create(out List<IAction> log2);
        ExecutionContext ctx2 = new(new LogicalClock(), new Rng(1u), new HookRegistry(), new ActionQueue(), obs2);

        new DealDamageAction(6, new global::Sts2Headless.Domain.Combat.CreatureId(1u)).Execute(ctx1);
        new GainBlockAction(5).Execute(ctx2);
        new DrawCardsAction(2).Execute(ctx1);

        Assert.Equal(2, log1.Count);
        Assert.IsType<DealDamageAction>(log1[0]);
        Assert.IsType<DrawCardsAction>(log1[1]);

        Assert.Single(log2);
        Assert.IsType<GainBlockAction>(log2[0]);
    }
}
