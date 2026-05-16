using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// Behavior tests for <see cref="RelicModel"/>: id/name/rarity round-trip, hook
/// subscription on add, unsubscription on remove, no leaked subscriptions across
/// add/remove cycles.
/// </summary>
public class RelicModelTests
{
    /// <summary>
    /// Test relic that subscribes one BeforeCombatStart handler and counts firings.
    /// </summary>
    private sealed class FakeRelic : RelicModel
    {
        public int FireCount { get; private set; }

        public FakeRelic()
            : base("fake_relic", "Fake Relic", RelicRarity.Starter) { }

        protected override void SubscribeHooks(HookRegistry hooks)
        {
            Subscribe(hooks, HookType.BeforeCombatStart, _ => FireCount++);
        }
    }

    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    [Fact]
    public void Construction_assigns_canonical_properties()
    {
        FakeRelic relic = new();
        Assert.Equal("fake_relic", relic.Id);
        Assert.Equal("Fake Relic", relic.Name);
        Assert.Equal(RelicRarity.Starter, relic.Rarity);
    }

    [Fact]
    public void OnAdded_subscribes_hook_handler_so_Fire_invokes_it()
    {
        FakeRelic relic = new();
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        ctx.Hooks.Fire(HookType.BeforeCombatStart, new HookContext(ctx));
        Assert.Equal(1, relic.FireCount);
    }

    [Fact]
    public void OnRemoved_unsubscribes_so_Fire_no_longer_invokes_handler()
    {
        FakeRelic relic = new();
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        relic.OnRemoved(ctx);
        ctx.Hooks.Fire(HookType.BeforeCombatStart, new HookContext(ctx));
        Assert.Equal(0, relic.FireCount);
    }

    [Fact]
    public void OnAdded_twice_without_remove_throws()
    {
        FakeRelic relic = new();
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        Assert.Throws<System.InvalidOperationException>(() => relic.OnAdded(ctx));
    }

    [Fact]
    public void OnRemoved_without_OnAdded_is_no_op()
    {
        FakeRelic relic = new();
        ExecutionContext ctx = NewCtx();
        // Should not throw — idempotent.
        relic.OnRemoved(ctx);
    }

    [Fact]
    public void Add_remove_add_cycle_leaves_exactly_one_active_subscription()
    {
        FakeRelic relic = new();
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        relic.OnRemoved(ctx);
        relic.OnAdded(ctx);
        ctx.Hooks.Fire(HookType.BeforeCombatStart, new HookContext(ctx));
        // Should fire exactly once — not twice (the first subscription was unsubscribed).
        Assert.Equal(1, relic.FireCount);
    }

    [Fact]
    public void RelicModel_subclass_registers_in_RelicCatalog()
    {
        RelicCatalog catalog = new();
        FakeRelic relic = new();
        catalog.Register(relic.Id, relic);
        Assert.Same(relic, catalog.Get("fake_relic"));
    }

    [Fact]
    public void Construction_rejects_empty_id_or_name()
    {
        Assert.Throws<System.ArgumentException>(() => new BadRelic("", "ok"));
        Assert.Throws<System.ArgumentException>(() => new BadRelic("ok", ""));
    }

    private sealed class BadRelic : RelicModel
    {
        public BadRelic(string id, string name)
            : base(id, name, RelicRarity.Common) { }
    }
}
