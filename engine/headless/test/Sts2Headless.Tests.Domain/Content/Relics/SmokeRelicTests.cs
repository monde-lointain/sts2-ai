using System.Collections.Generic;
using System.Linq;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Relics;

/// <summary>
/// Byte-exact value checks for every smoke-set relic. Each test installs the relic,
/// fires the hook the relic listens to, drains the resulting actions, and asserts
/// the recorded effect actions match upstream values. Cross-reference upstream file
/// paths in each relic's class doc-comment.
/// </summary>
public class SmokeRelicTests
{
    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    /// <summary>
    /// Install <paramref name="relic"/>, fire <paramref name="hookType"/>, drain the
    /// queue with an <see cref="EffectObserver"/> attached, and return the captured
    /// actions in execution order.
    /// </summary>
    private static IReadOnlyList<IAction> FireAndCollect(RelicModel relic, HookType hookType)
    {
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        using (EffectObserver.Attach(out List<IAction> log))
        {
            ctx.Hooks.Fire(hookType, new HookContext(ctx));
            ctx.Queue.Drain(ctx);
            return log;
        }
    }

    // ===== RingOfTheSnake (upstream: src/Core/Models/Relics/RingOfTheSnake.cs) =====

    [Fact]
    public void RingOfTheSnake_canonical_properties()
    {
        RingOfTheSnake r = new();
        Assert.Equal("RingOfTheSnake", r.Id);
        Assert.Equal("Ring of the Snake", r.Name);
        Assert.Equal(RelicRarity.Starter, r.Rarity);
        Assert.Equal(2, RingOfTheSnake.ExtraCards);
    }

    [Fact]
    public void RingOfTheSnake_OnBeforeHandDraw_enqueues_extra_2_cards()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(
            new RingOfTheSnake(),
            HookType.ModifyHandDraw
        );
        ExtraHandDrawAction extra = Assert.IsType<ExtraHandDrawAction>(actions.Single());
        Assert.Equal(2, extra.Extra);
    }

    // ===== Anchor (upstream: src/Core/Models/Relics/Anchor.cs) =====

    [Fact]
    public void Anchor_canonical_properties()
    {
        Anchor a = new();
        Assert.Equal("Anchor", a.Id);
        Assert.Equal("Anchor", a.Name);
        Assert.Equal(RelicRarity.Common, a.Rarity);
        Assert.Equal(10, Anchor.BlockAtStart);
    }

    [Fact]
    public void Anchor_OnBeforeCombatStart_enqueues_10_block()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(new Anchor(), HookType.BeforeCombatStart);
        GainBlockAction blk = Assert.IsType<GainBlockAction>(actions.Single());
        Assert.Equal(10, blk.Amount);
    }

    // ===== Vajra (upstream: src/Core/Models/Relics/Vajra.cs) =====

    [Fact]
    public void Vajra_canonical_properties()
    {
        Vajra v = new();
        Assert.Equal("Vajra", v.Id);
        Assert.Equal("Vajra", v.Name);
        Assert.Equal(RelicRarity.Common, v.Rarity);
        Assert.Equal(1, Vajra.StrengthAtStart);
    }

    [Fact]
    public void Vajra_OnBeforeCombatStart_enqueues_1_strength()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(new Vajra(), HookType.BeforeCombatStart);
        ApplyPowerAction p = Assert.IsType<ApplyPowerAction>(actions.Single());
        Assert.Equal(PowerIds.Strength, p.PowerId);
        Assert.Equal(1, p.Amount);
        Assert.Null(p.Target); // self
    }

    // ===== BagOfPreparation (upstream: src/Core/Models/Relics/BagOfPreparation.cs) =====

    [Fact]
    public void BagOfPreparation_canonical_properties()
    {
        BagOfPreparation b = new();
        Assert.Equal("BagOfPreparation", b.Id);
        Assert.Equal("Bag of Preparation", b.Name);
        Assert.Equal(RelicRarity.Common, b.Rarity);
        Assert.Equal(2, BagOfPreparation.ExtraCards);
    }

    [Fact]
    public void BagOfPreparation_OnBeforeHandDraw_enqueues_extra_2_cards()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(
            new BagOfPreparation(),
            HookType.ModifyHandDraw
        );
        ExtraHandDrawAction extra = Assert.IsType<ExtraHandDrawAction>(actions.Single());
        Assert.Equal(2, extra.Extra);
    }

    // ===== BloodVial (upstream: src/Core/Models/Relics/BloodVial.cs) =====

    [Fact]
    public void BloodVial_canonical_properties()
    {
        BloodVial b = new();
        Assert.Equal("BloodVial", b.Id);
        Assert.Equal("Blood Vial", b.Name);
        Assert.Equal(RelicRarity.Common, b.Rarity);
        Assert.Equal(2, BloodVial.HealAmount);
    }

    [Fact]
    public void BloodVial_OnPlayerTurnStartLate_enqueues_heal_2()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(
            new BloodVial(),
            HookType.AfterPlayerTurnStartLate
        );
        HealAction h = Assert.IsType<HealAction>(actions.Single());
        Assert.Equal(2, h.Amount);
    }

    // ===== Shared lifecycle =====

    [Fact]
    public void Relic_handler_unsubscribed_on_OnRemoved_does_not_fire()
    {
        ExecutionContext ctx = NewCtx();
        RingOfTheSnake r = new();
        r.OnAdded(ctx);
        r.OnRemoved(ctx);
        using (EffectObserver.Attach(out List<IAction> log))
        {
            ctx.Hooks.Fire(HookType.ModifyHandDraw, new HookContext(ctx));
            ctx.Queue.Drain(ctx);
            Assert.Empty(log);
        }
    }

    [Fact]
    public void Multiple_smoke_relics_can_subscribe_to_same_hook_without_conflict()
    {
        // RingOfTheSnake and BagOfPreparation both subscribe to BeforeHandDraw.
        ExecutionContext ctx = NewCtx();
        new RingOfTheSnake().OnAdded(ctx);
        new BagOfPreparation().OnAdded(ctx);
        using (EffectObserver.Attach(out List<IAction> log))
        {
            ctx.Hooks.Fire(HookType.ModifyHandDraw, new HookContext(ctx));
            ctx.Queue.Drain(ctx);
            // Both relics fire; both enqueue an ExtraHandDrawAction(2).
            Assert.Equal(2, log.Count);
            Assert.All(log, a => Assert.Equal(2, Assert.IsType<ExtraHandDrawAction>(a).Extra));
        }
    }
}
