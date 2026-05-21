using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// Behavior tests for the hook-subscription lifecycle added to
/// <see cref="PowerModel"/> in wave-26/Q1.A (RelicModel mirror pattern,
/// per-attach rather than per-singleton).
///
/// <para>
/// Verifies: OnApplied subscribes, OnRemoved unsubscribes, multiple independent
/// PowerInstance attachments (same PowerModel, distinct creature ids), re-attach
/// cycles, double-apply guard, and the HookContext mutable-flag convention for
/// boolean-aggregation hooks (ShouldStopCombatFromEnding).
/// </para>
/// </summary>
public class PowerModelHookLifecycleTests
{
    // ---------------------------------------------------------------------------
    // Fixture helpers
    // ---------------------------------------------------------------------------

    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    /// <summary>
    /// Minimal concrete power that counts AfterDeath firings per attach.
    /// Uses the Subscribe helper exposed by PowerModel base.
    /// </summary>
    private sealed class FakePower : PowerModel
    {
        // Counts indexed by ownerCreatureId for multi-attach test isolation.
        private readonly Dictionary<global::Sts2Headless.Domain.Combat.CreatureId, int> _fireCounts = new();

        public FakePower()
            : base("fake_power", PowerType.Buff, PowerStackType.Counter) { }

        public int GetFireCount(global::Sts2Headless.Domain.Combat.CreatureId creatureId) =>
            _fireCounts.TryGetValue(creatureId, out int c) ? c : 0;

        protected override void SubscribeHooks(
            HookRegistry hooks,
            global::Sts2Headless.Domain.Combat.CreatureId ownerCreatureId,
            List<HookSubscriptionHandle> handleSink
        )
        {
            Subscribe(
                hooks,
                handleSink,
                HookType.AfterDeath,
                _ =>
                {
                    _fireCounts.TryGetValue(ownerCreatureId, out int prev);
                    _fireCounts[ownerCreatureId] = prev + 1;
                },
                ownerCreatureId
            );
        }
    }

    /// <summary>
    /// Power that subscribes ShouldStopCombatFromEnding and sets a shared
    /// mutable-flag container (bool[1]) on the context. Demonstrates the
    /// HookContext boolean-aggregation convention documented in PowerModel.
    /// </summary>
    private sealed class DeferCombatEndPower : PowerModel
    {
        /// <summary>
        /// Shared container provided by the caller before Fire. Handlers set
        /// [0] = true to signal deferred combat end. Q1.C (CheckCombatEnd)
        /// will own this pattern in production; here it's an in-test stub.
        /// </summary>
        public readonly bool[] DeferFlag = new bool[1];

        public DeferCombatEndPower()
            : base("defer_combat_end_power", PowerType.Buff, PowerStackType.Single) { }

        protected override void SubscribeHooks(
            HookRegistry hooks,
            global::Sts2Headless.Domain.Combat.CreatureId ownerCreatureId,
            List<HookSubscriptionHandle> handleSink
        )
        {
            bool[] flag = DeferFlag; // capture by ref-type (array), not value
            Subscribe(
                hooks,
                handleSink,
                HookType.ShouldStopCombatFromEnding,
                _ => flag[0] = true,
                ownerCreatureId
            );
        }
    }

    // ---------------------------------------------------------------------------
    // Tests: basic subscription lifecycle
    // ---------------------------------------------------------------------------

    [Fact]
    public void OnApplied_subscribes_hook_handler_so_Fire_invokes_it()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);
        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        Assert.Equal(1, power.GetFireCount(creature));
    }

    [Fact]
    public void OnRemoved_unsubscribes_so_Fire_no_longer_invokes_handler()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);
        power.OnRemoved(creature, ctx.Hooks);
        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        Assert.Equal(0, power.GetFireCount(creature));
    }

    [Fact]
    public void OnRemoved_without_OnApplied_is_no_op()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        // Must not throw — idempotent.
        power.OnRemoved(new global::Sts2Headless.Domain.Combat.CreatureId(42u), ctx.Hooks);
    }

    [Fact]
    public void OnApplied_twice_for_same_creature_throws()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);

        Assert.Throws<System.InvalidOperationException>(() => power.OnApplied(creature, ctx.Hooks));
    }

    // ---------------------------------------------------------------------------
    // Tests: per-instance independence (multiple creatures, same PowerModel)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Multiple_PowerInstances_same_PowerModel_attach_independently()
    {
        // Same PowerModel singleton attached to two different creatures.
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creatureA = new global::Sts2Headless.Domain.Combat.CreatureId(10u);
        global::Sts2Headless.Domain.Combat.CreatureId creatureB = new global::Sts2Headless.Domain.Combat.CreatureId(20u);

        power.OnApplied(creatureA, ctx.Hooks);
        power.OnApplied(creatureB, ctx.Hooks);

        // Both fire on the same registry Fire call.
        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        Assert.Equal(1, power.GetFireCount(creatureA));
        Assert.Equal(1, power.GetFireCount(creatureB));
    }

    [Fact]
    public void Removing_one_creature_does_not_affect_others()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creatureA = new global::Sts2Headless.Domain.Combat.CreatureId(10u);
        global::Sts2Headless.Domain.Combat.CreatureId creatureB = new global::Sts2Headless.Domain.Combat.CreatureId(20u);

        power.OnApplied(creatureA, ctx.Hooks);
        power.OnApplied(creatureB, ctx.Hooks);

        // Remove A only.
        power.OnRemoved(creatureA, ctx.Hooks);

        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        Assert.Equal(0, power.GetFireCount(creatureA)); // removed — silent
        Assert.Equal(1, power.GetFireCount(creatureB)); // still active
    }

    // ---------------------------------------------------------------------------
    // Tests: re-attach cycle
    // ---------------------------------------------------------------------------

    [Fact]
    public void Remove_then_re_apply_leaves_exactly_one_active_subscription()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);
        power.OnRemoved(creature, ctx.Hooks);
        power.OnApplied(creature, ctx.Hooks); // re-attach

        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        // Exactly one — not two (first subscription was released).
        Assert.Equal(1, power.GetFireCount(creature));
    }

    [Fact]
    public void Re_attach_after_remove_can_be_removed_again()
    {
        FakePower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);
        power.OnRemoved(creature, ctx.Hooks);
        power.OnApplied(creature, ctx.Hooks);
        power.OnRemoved(creature, ctx.Hooks); // second remove must not throw

        ctx.Hooks.Fire(HookType.AfterDeath, new HookContext(ctx));

        Assert.Equal(0, power.GetFireCount(creature));
    }

    // ---------------------------------------------------------------------------
    // Tests: boolean-aggregation (mutable-flag convention for ShouldStopCombatFromEnding)
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShouldStopCombatFromEnding_mutable_flag_convention_works()
    {
        // The boolean-aggregation convention: caller allocates a shared mutable
        // container; handlers set it; caller reads after Fire.
        // This is the pattern Q1.C CheckCombatEnd will follow in production.
        DeferCombatEndPower power = new();
        ExecutionContext ctx = NewCtx();
        global::Sts2Headless.Domain.Combat.CreatureId creature = new global::Sts2Headless.Domain.Combat.CreatureId(1u);

        power.OnApplied(creature, ctx.Hooks);

        Assert.False(power.DeferFlag[0]); // not yet fired

        ctx.Hooks.Fire(HookType.ShouldStopCombatFromEnding, new HookContext(ctx));

        Assert.True(power.DeferFlag[0]); // handler mutated the flag
    }

    [Fact]
    public void ShouldStopCombatFromEnding_multiple_subscribers_all_fire_and_flag_is_or_aggregated()
    {
        // Two powers on two creatures; both subscribe. One sets flag; the other
        // does too. Demonstrates OR-aggregation via the mutable-flag container.
        DeferCombatEndPower powerA = new();
        DeferCombatEndPower powerB = new();
        ExecutionContext ctx = NewCtx();

        powerA.OnApplied(new global::Sts2Headless.Domain.Combat.CreatureId(1u), ctx.Hooks);
        powerB.OnApplied(new global::Sts2Headless.Domain.Combat.CreatureId(2u), ctx.Hooks);

        ctx.Hooks.Fire(HookType.ShouldStopCombatFromEnding, new HookContext(ctx));

        Assert.True(powerA.DeferFlag[0]);
        Assert.True(powerB.DeferFlag[0]);
    }

    [Fact]
    public void ShouldStopCombatFromEnding_no_subscribers_flag_stays_false()
    {
        DeferCombatEndPower power = new();
        // Never call OnApplied — no active subscription.
        ExecutionContext ctx = NewCtx();

        ctx.Hooks.Fire(HookType.ShouldStopCombatFromEnding, new HookContext(ctx));

        Assert.False(power.DeferFlag[0]);
    }
}
