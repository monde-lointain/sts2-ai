using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Tests for the test-affordance <see cref="EffectObserver"/>: attach/detach via
/// scope; no-op when unattached; rejects nested attach.
/// </summary>
public class EffectObserverTests
{
    private static ExecutionContext NewCtx()
        => new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    [Fact]
    public void Unattached_observer_is_silent_no_op()
    {
        ExecutionContext ctx = NewCtx();
        // No attach scope; Record should be a no-op.
        new DealDamageAction(6, "m0").Execute(ctx);
        // No exception, nothing to assert except absence of crash.
    }

    [Fact]
    public void Attached_observer_captures_all_executed_actions_in_order()
    {
        ExecutionContext ctx = NewCtx();
        List<IAction> log;
        using (EffectObserver.Attach(out log))
        {
            new DealDamageAction(6, "m0").Execute(ctx);
            new GainBlockAction(5).Execute(ctx);
            new DrawCardsAction(2).Execute(ctx);
        }
        Assert.Equal(3, log.Count);
        Assert.IsType<DealDamageAction>(log[0]);
        Assert.IsType<GainBlockAction>(log[1]);
        Assert.IsType<DrawCardsAction>(log[2]);
    }

    [Fact]
    public void Detached_after_scope_disposes_so_new_executes_arent_recorded()
    {
        ExecutionContext ctx = NewCtx();
        List<IAction> log;
        using (EffectObserver.Attach(out log))
        {
            new DealDamageAction(1, "x").Execute(ctx);
        }
        new DealDamageAction(2, "x").Execute(ctx);
        Assert.Single(log); // only the in-scope record was captured.
    }

    [Fact]
    public void Nested_attach_throws()
    {
        using (EffectObserver.Attach(out _))
        {
            Assert.Throws<System.InvalidOperationException>(() => EffectObserver.Attach(out _));
        }
    }
}
