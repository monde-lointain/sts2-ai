using System.Collections.Generic;
using System.Linq;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Byte-exact value checks for every smoke-set card. Each test plays the card,
/// drains the action queue with a <see cref="ListActionObserver"/> attached, and
/// verifies the recorded actions match upstream values. Cross-reference upstream
/// file paths per card (see each card's class doc-comment).
///
/// <para>
/// What we verify per card:
/// </para>
/// <list type="bullet">
///   <item>Canonical cost / type / rarity / target match upstream's base() args.</item>
///   <item>OnPlay enqueues the right ordered effect actions with the right base values.</item>
///   <item>Upgrade delta constants match upstream's OnUpgrade body verbatim (asserted
///         directly against <c>BaseX</c> / <c>UpgradeDelta</c> consts — the model is
///         immutable post-P2b, so upgraded play-behavior is exercised by combat-engine
///         tests once upgraded-card play wires up).</item>
///   <item>Tags (where present) match upstream's CanonicalTags.</item>
/// </list>
/// </summary>
public class SilentSmokeCardTests
{
    /// <summary>
    /// Play <paramref name="card"/> against a fresh context and return the actions it
    /// enqueued (in execution order). Uses <see cref="ListActionObserver"/> to capture
    /// during Drain — no peeking into <see cref="ActionQueue"/> internals.
    /// </summary>
    private static IReadOnlyList<IAction> Play(CardModel card, string? target = null)
    {
        var obs = ListActionObserver.Create(out List<IAction> log);
        ExecutionContext ctx = new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue(), obs);
        card.OnPlay(ctx, target);
        ctx.Queue.Drain(ctx);
        return log;
    }

    // ===== StrikeSilent (upstream: src/Core/Models/Cards/StrikeSilent.cs) =====

    [Fact]
    public void StrikeSilent_canonical_properties()
    {
        StrikeSilent c = new();
        Assert.Equal("StrikeSilent", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Attack, c.Type);
        Assert.Equal(CardRarity.Basic, c.Rarity);
        Assert.Equal(TargetType.AnyEnemy, c.Target);
        Assert.Contains(CardTag.Strike, c.Tags);
        Assert.Equal(6, c.Damage);
    }

    [Fact]
    public void StrikeSilent_OnPlay_enqueues_6_damage_at_target()
    {
        StrikeSilent c = new();
        IReadOnlyList<IAction> actions = Play(c, target: "m0");
        IAction single = Assert.Single(actions);
        DealDamageAction dmg = Assert.IsType<DealDamageAction>(single);
        Assert.Equal(6, dmg.Amount);
        Assert.Equal("m0", dmg.Target);
    }

    [Fact]
    public void StrikeSilent_upgrade_delta_is_3()
    {
        Assert.Equal(6, StrikeSilent.BaseDamage);
        Assert.Equal(3, StrikeSilent.UpgradeDelta);
    }

    // ===== DefendSilent (upstream: src/Core/Models/Cards/DefendSilent.cs) =====

    [Fact]
    public void DefendSilent_canonical_properties()
    {
        DefendSilent c = new();
        Assert.Equal("DefendSilent", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Basic, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Contains(CardTag.Defend, c.Tags);
        Assert.Equal(5, c.Block);
    }

    [Fact]
    public void DefendSilent_OnPlay_enqueues_5_block()
    {
        DefendSilent c = new();
        GainBlockAction blk = Assert.IsType<GainBlockAction>(Play(c).Single());
        Assert.Equal(5, blk.Amount);
    }

    [Fact]
    public void DefendSilent_upgrade_delta_is_3()
    {
        Assert.Equal(5, DefendSilent.BaseBlock);
        Assert.Equal(3, DefendSilent.UpgradeDelta);
    }

    // ===== Neutralize (upstream: src/Core/Models/Cards/Neutralize.cs) =====

    [Fact]
    public void Neutralize_canonical_properties()
    {
        Neutralize c = new();
        Assert.Equal("Neutralize", c.Id);
        Assert.Equal(0, c.Cost);
        Assert.Equal(CardType.Attack, c.Type);
        Assert.Equal(CardRarity.Basic, c.Rarity);
        Assert.Equal(TargetType.AnyEnemy, c.Target);
        Assert.Equal(3, c.Damage);
        Assert.Equal(1, c.Weak);
    }

    [Fact]
    public void Neutralize_OnPlay_enqueues_3_damage_then_1_weak()
    {
        Neutralize c = new();
        IReadOnlyList<IAction> actions = Play(c, target: "m0");
        Assert.Equal(2, actions.Count);
        DealDamageAction dmg = Assert.IsType<DealDamageAction>(actions[0]);
        Assert.Equal(3, dmg.Amount);
        Assert.Equal("m0", dmg.Target);
        ApplyPowerAction wk = Assert.IsType<ApplyPowerAction>(actions[1]);
        Assert.Equal(PowerIds.Weak, wk.PowerId);
        Assert.Equal(1, wk.Amount);
        Assert.Equal("m0", wk.Target);
    }

    [Fact]
    public void Neutralize_upgrade_deltas_are_1_and_1()
    {
        Assert.Equal(3, Neutralize.BaseDamage);
        Assert.Equal(1, Neutralize.BaseWeak);
        Assert.Equal(1, Neutralize.UpgradeDeltaDamage);
        Assert.Equal(1, Neutralize.UpgradeDeltaWeak);
    }

    // ===== Survivor (upstream: src/Core/Models/Cards/Survivor.cs) =====

    [Fact]
    public void Survivor_canonical_properties()
    {
        Survivor c = new();
        Assert.Equal("Survivor", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Basic, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Equal(8, c.Block);
    }

    [Fact]
    public void Survivor_OnPlay_enqueues_8_block_then_discard_1()
    {
        Survivor c = new();
        IReadOnlyList<IAction> actions = Play(c);
        Assert.Equal(2, actions.Count);
        GainBlockAction blk = Assert.IsType<GainBlockAction>(actions[0]);
        Assert.Equal(8, blk.Amount);
        DiscardCardsAction disc = Assert.IsType<DiscardCardsAction>(actions[1]);
        Assert.Equal(1, disc.Count);
    }

    [Fact]
    public void Survivor_upgrade_delta_is_3()
    {
        Assert.Equal(8, Survivor.BaseBlock);
        Assert.Equal(3, Survivor.UpgradeDelta);
    }

    // ===== Slice (upstream: src/Core/Models/Cards/Slice.cs) =====

    [Fact]
    public void Slice_canonical_properties_and_OnPlay()
    {
        Slice c = new();
        Assert.Equal("Slice", c.Id);
        Assert.Equal(0, c.Cost);
        Assert.Equal(CardType.Attack, c.Type);
        Assert.Equal(CardRarity.Common, c.Rarity);
        Assert.Equal(TargetType.AnyEnemy, c.Target);
        Assert.Equal(6, c.Damage);
        DealDamageAction dmg = Assert.IsType<DealDamageAction>(Play(c, target: "m0").Single());
        Assert.Equal(6, dmg.Amount);
    }

    [Fact]
    public void Slice_upgrade_delta_is_3()
    {
        Assert.Equal(6, Slice.BaseDamage);
        Assert.Equal(3, Slice.UpgradeDelta);
    }

    // ===== DeadlyPoison (upstream: src/Core/Models/Cards/DeadlyPoison.cs) =====

    [Fact]
    public void DeadlyPoison_canonical_properties_and_OnPlay()
    {
        DeadlyPoison c = new();
        Assert.Equal("DeadlyPoison", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Common, c.Rarity);
        Assert.Equal(TargetType.AnyEnemy, c.Target);
        Assert.Equal(5, c.Poison);
        ApplyPowerAction p = Assert.IsType<ApplyPowerAction>(Play(c, target: "m0").Single());
        Assert.Equal(PowerIds.Poison, p.PowerId);
        Assert.Equal(5, p.Amount);
        Assert.Equal("m0", p.Target);
    }

    [Fact]
    public void DeadlyPoison_upgrade_delta_is_2()
    {
        Assert.Equal(5, DeadlyPoison.BasePoison);
        Assert.Equal(2, DeadlyPoison.UpgradeDelta);
    }

    // ===== Backflip (upstream: src/Core/Models/Cards/Backflip.cs) =====

    [Fact]
    public void Backflip_canonical_properties_and_OnPlay()
    {
        Backflip c = new();
        Assert.Equal("Backflip", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Common, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Equal(5, c.Block);
        Assert.Equal(2, c.Cards);
        IReadOnlyList<IAction> actions = Play(c);
        Assert.Equal(2, actions.Count);
        Assert.Equal(5, Assert.IsType<GainBlockAction>(actions[0]).Amount);
        Assert.Equal(2, Assert.IsType<DrawCardsAction>(actions[1]).Count);
    }

    [Fact]
    public void Backflip_upgrade_delta_block_is_3_cards_unchanged()
    {
        Assert.Equal(5, Backflip.BaseBlock);
        Assert.Equal(2, Backflip.BaseCards);
        Assert.Equal(3, Backflip.UpgradeDelta);
    }

    // ===== Acrobatics (upstream: src/Core/Models/Cards/Acrobatics.cs) =====

    [Fact]
    public void Acrobatics_canonical_properties_and_OnPlay()
    {
        Acrobatics c = new();
        Assert.Equal("Acrobatics", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Uncommon, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Equal(3, c.Cards);
        IReadOnlyList<IAction> actions = Play(c);
        Assert.Equal(2, actions.Count);
        Assert.Equal(3, Assert.IsType<DrawCardsAction>(actions[0]).Count);
        Assert.Equal(1, Assert.IsType<DiscardCardsAction>(actions[1]).Count);
    }

    [Fact]
    public void Acrobatics_upgrade_delta_is_1_card()
    {
        Assert.Equal(3, Acrobatics.BaseCards);
        Assert.Equal(1, Acrobatics.UpgradeDelta);
    }

    // ===== DodgeAndRoll (upstream: src/Core/Models/Cards/DodgeAndRoll.cs) =====

    [Fact]
    public void DodgeAndRoll_canonical_properties_and_OnPlay()
    {
        DodgeAndRoll c = new();
        Assert.Equal("DodgeAndRoll", c.Id);
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Common, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Equal(4, c.Block);
        // Phase-1 deviation: BlockNextTurnPower deferred to S12 — only the 4 block lands.
        GainBlockAction blk = Assert.IsType<GainBlockAction>(Play(c).Single());
        Assert.Equal(4, blk.Amount);
    }

    [Fact]
    public void DodgeAndRoll_upgrade_delta_is_2_block()
    {
        Assert.Equal(4, DodgeAndRoll.BaseBlock);
        Assert.Equal(2, DodgeAndRoll.UpgradeDelta);
    }
}
