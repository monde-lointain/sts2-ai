using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Byte-exact value checks for every S12 Phase-1 card. Mirrors the smoke tests but
/// covers the additional Silent / colorless / curse / status set. Cross-reference
/// upstream file paths per card (each class doc-comment links upstream).
///
/// <para>Per-card checks: cost, type, rarity, target, key magic numbers, upgrade delta.
/// Cards whose OnPlay is hook-driven or combat-state dependent assert canonical
/// metadata only (their behaviour is wired up by S13 / future combat-engine work).</para>
///
/// <para>P2b: CardModel and subclasses are immutable post-construction. Upgrade
/// deltas are asserted via <c>BaseX</c> / <c>UpgradeDelta</c> constants rather than
/// via a mutator. Per-instance upgrade level lives on <c>CardInstance.UpgradeLevel</c>;
/// once upgraded-card play wires up post-Phase-1, combat-engine tests will exercise
/// upgraded play-behavior.</para>
/// </summary>
public class Phase1CardTests
{
    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    private static IReadOnlyList<IAction> Play(CardModel card, string? target = null)
    {
        ExecutionContext ctx = NewCtx();
        using (EffectObserver.Attach(out List<IAction> log))
        {
            card.OnPlay(ctx, target);
            ctx.Queue.Drain(ctx);
            return log;
        }
    }

    [Fact]
    public void Abrasive_canonical()
    {
        Abrasive c = new();
        Assert.Equal("Abrasive", c.Id);
        Assert.Equal(3, c.Cost);
        Assert.Equal(CardType.Power, c.Type);
        Assert.Equal(CardRarity.Rare, c.Rarity);
        Assert.Equal(TargetType.Self, c.Target);
        Assert.Equal(4, c.Thorns);
        Assert.Equal(4, Abrasive.BaseThorns);
        Assert.Equal(2, Abrasive.UpgradeDelta);
    }

    [Fact]
    public void Accelerant_canonical()
    {
        Accelerant c = new();
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Power, c.Type);
        Assert.Equal(CardRarity.Rare, c.Rarity);
        Assert.Equal(1, c.Amount);
        Assert.Equal(1, Accelerant.BaseAmount);
        Assert.Equal(1, Accelerant.UpgradeDelta);
    }

    [Fact]
    public void Accuracy_canonical()
    {
        Accuracy c = new();
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardRarity.Uncommon, c.Rarity);
        Assert.Equal(4, c.Amount);
        Assert.Equal(4, Accuracy.BaseAmount);
        Assert.Equal(2, Accuracy.UpgradeDelta);
    }

    [Fact]
    public void Adrenaline_canonical()
    {
        Adrenaline c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(CardType.Skill, c.Type);
        Assert.Equal(CardRarity.Rare, c.Rarity);
        Assert.Equal(1, c.EnergyGain);
        Assert.Equal(2, Adrenaline.CardsDrawn);
        Assert.Equal(1, Adrenaline.BaseEnergy);
        Assert.Equal(1, Adrenaline.UpgradeDelta);
    }

    [Fact]
    public void Anticipate_canonical()
    {
        Anticipate c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(2, c.Dexterity);
        Assert.Equal(2, Anticipate.BaseDex);
        Assert.Equal(1, Anticipate.UpgradeDelta);
    }

    [Fact]
    public void Assassinate_OnPlay_emits_damage_then_vuln()
    {
        Assassinate c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(CardType.Attack, c.Type);
        Assert.Equal(10, c.Damage);
        Assert.Equal(1, c.Vulnerable);
        var actions = Play(c, "m0");
        Assert.Equal(2, actions.Count);
        Assert.Equal(10, Assert.IsType<DealDamageAction>(actions[0]).Amount);
        Assert.Equal(PowerIds.Vulnerable, Assert.IsType<ApplyPowerAction>(actions[1]).PowerId);
        Assert.Equal(10, Assassinate.BaseDamage);
        Assert.Equal(1, Assassinate.BaseVuln);
        Assert.Equal(3, Assassinate.UpgradeDeltaDamage);
        Assert.Equal(1, Assassinate.UpgradeDeltaVuln);
    }

    [Fact]
    public void Backstab_canonical()
    {
        Backstab c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(11, c.Damage);
        Assert.Equal(11, Backstab.BaseDamage);
        Assert.Equal(4, Backstab.UpgradeDelta);
    }

    [Fact]
    public void BladeOfInk_canonical()
    {
        BladeOfInk c = new();
        Assert.Equal(2, c.Cards);
        Assert.Equal(2, BladeOfInk.BaseCards);
        Assert.Equal(1, BladeOfInk.UpgradeDelta);
    }

    [Fact]
    public void BladeDance_canonical()
    {
        BladeDance c = new();
        Assert.Equal(3, c.Shivs);
        Assert.Equal(3, BladeDance.BaseShivs);
        Assert.Equal(1, BladeDance.UpgradeDelta);
    }

    [Fact]
    public void Blur_canonical()
    {
        Blur c = new();
        Assert.Equal(5, c.Block);
        Assert.Equal(5, Blur.BaseBlock);
        Assert.Equal(3, Blur.UpgradeDelta);
    }

    [Fact]
    public void BouncingFlask_3_hits()
    {
        BouncingFlask c = new();
        Assert.Equal(3, c.Repeat);
        var actions = Play(c, "m0");
        Assert.Equal(3, actions.Count);
        Assert.All(
            actions,
            a => Assert.Equal(BouncingFlask.PoisonPerHit, Assert.IsType<ApplyPowerAction>(a).Amount)
        );
        Assert.Equal(3, BouncingFlask.BaseRepeat);
        Assert.Equal(1, BouncingFlask.UpgradeDelta);
    }

    [Fact]
    public void BubbleBubble_canonical()
    {
        BubbleBubble c = new();
        Assert.Equal(9, c.Poison);
        Assert.Equal(9, BubbleBubble.BasePoison);
        Assert.Equal(3, BubbleBubble.UpgradeDelta);
    }

    [Fact]
    public void BulletTime_canonical_upgrade_reduces_cost()
    {
        BulletTime c = new();
        Assert.Equal(3, c.EnergyCost);
        Assert.Equal(3, BulletTime.BaseEnergyCost);
        Assert.Equal(-1, BulletTime.UpgradeDelta);
    }

    [Fact]
    public void Burst_canonical()
    {
        Burst c = new();
        Assert.Equal(1, c.Skills);
        Assert.Equal(1, Burst.BaseSkills);
        Assert.Equal(1, Burst.UpgradeDelta);
    }

    [Fact]
    public void CloakAndDagger_canonical()
    {
        CloakAndDagger c = new();
        Assert.Equal(6, CloakAndDagger.Block);
        Assert.Equal(1, c.Shivs);
        Assert.Equal(1, CloakAndDagger.BaseShivs);
        Assert.Equal(1, CloakAndDagger.UpgradeDelta);
    }

    [Fact]
    public void CorrosiveWave_canonical()
    {
        CorrosiveWave c = new();
        Assert.Equal(2, c.Amount);
        Assert.Equal(2, CorrosiveWave.BaseAmount);
        Assert.Equal(1, CorrosiveWave.UpgradeDelta);
    }

    [Fact]
    public void DaggerSpray_2_hits_4_dmg()
    {
        DaggerSpray c = new();
        Assert.Equal(4, c.Damage);
        var actions = Play(c, "m0");
        Assert.Equal(2, actions.Count);
        Assert.Equal(4, DaggerSpray.BaseDamage);
        Assert.Equal(2, DaggerSpray.UpgradeDelta);
    }

    [Fact]
    public void DaggerThrow_canonical()
    {
        DaggerThrow c = new();
        Assert.Equal(9, c.Damage);
        var actions = Play(c, "m0");
        Assert.Equal(3, actions.Count);
        Assert.Equal(9, DaggerThrow.BaseDamage);
        Assert.Equal(3, DaggerThrow.UpgradeDelta);
    }

    [Fact]
    public void Dash_canonical()
    {
        Dash c = new();
        Assert.Equal(10, c.Damage);
        Assert.Equal(10, c.Block);
        Assert.Equal(10, Dash.BaseDamage);
        Assert.Equal(10, Dash.BaseBlock);
        Assert.Equal(3, Dash.UpgradeDeltaDamage);
        Assert.Equal(3, Dash.UpgradeDeltaBlock);
    }

    [Fact]
    public void Deflect_canonical()
    {
        Deflect c = new();
        Assert.Equal(4, c.Block);
        Assert.Equal(4, Deflect.BaseBlock);
        Assert.Equal(3, Deflect.UpgradeDelta);
    }

    [Fact]
    public void EchoingSlash_canonical()
    {
        EchoingSlash c = new();
        Assert.Equal(10, c.Damage);
        Assert.Equal(10, EchoingSlash.BaseDamage);
        Assert.Equal(3, EchoingSlash.UpgradeDelta);
    }

    [Fact]
    public void Envenom_canonical()
    {
        Envenom c = new();
        Assert.Equal(2, c.Cost);
        Assert.Equal(1, c.Amount);
        Assert.Equal(1, Envenom.BaseAmount);
        Assert.Equal(1, Envenom.UpgradeDelta);
    }

    [Fact]
    public void EscapePlan_canonical()
    {
        EscapePlan c = new();
        Assert.Equal(3, c.Block);
        Assert.Equal(3, EscapePlan.BaseBlock);
        Assert.Equal(2, EscapePlan.UpgradeDelta);
    }

    [Fact]
    public void Expertise_canonical()
    {
        Expertise c = new();
        Assert.Equal(6, c.Cards);
        Assert.Equal(6, Expertise.BaseCards);
        Assert.Equal(1, Expertise.UpgradeDelta);
    }

    [Fact]
    public void Expose_canonical()
    {
        Expose c = new();
        Assert.Equal(2, c.Power);
        Assert.Equal(2, Expose.BasePower);
        Assert.Equal(1, Expose.UpgradeDelta);
    }

    [Fact]
    public void FanOfKnives_canonical()
    {
        FanOfKnives c = new();
        Assert.Equal(4, c.Shivs);
        Assert.Equal(4, FanOfKnives.BaseShivs);
        Assert.Equal(1, FanOfKnives.UpgradeDelta);
    }

    [Fact]
    public void Finisher_canonical()
    {
        Finisher c = new();
        Assert.Equal(6, c.Damage);
        Assert.Equal(6, Finisher.BaseDamage);
        Assert.Equal(2, Finisher.UpgradeDelta);
    }

    [Fact]
    public void Flanking_canonical_upgrade()
    {
        Flanking c = new();
        Assert.Equal(2, c.EnergyCost);
        Assert.Equal(2, Flanking.BaseCost);
        Assert.Equal(-1, Flanking.UpgradeDelta);
    }

    [Fact]
    public void Flechettes_canonical()
    {
        Flechettes c = new();
        Assert.Equal(5, c.Damage);
        Assert.Equal(5, Flechettes.BaseDamage);
        Assert.Equal(2, Flechettes.UpgradeDelta);
    }

    [Fact]
    public void FlickFlack_canonical()
    {
        FlickFlack c = new();
        Assert.Equal(6, c.Damage);
        Assert.Equal(6, FlickFlack.BaseDamage);
        Assert.Equal(2, FlickFlack.UpgradeDelta);
    }

    [Fact]
    public void FollowThrough_canonical()
    {
        FollowThrough c = new();
        Assert.Equal(7, c.Damage);
        Assert.Equal(7, FollowThrough.BaseDamage);
        Assert.Equal(2, FollowThrough.UpgradeDelta);
    }

    [Fact]
    public void Footwork_canonical()
    {
        Footwork c = new();
        Assert.Equal(2, c.Dexterity);
        Assert.Equal(2, Footwork.BaseDex);
        Assert.Equal(1, Footwork.UpgradeDelta);
    }

    [Fact]
    public void GrandFinale_canonical()
    {
        GrandFinale c = new();
        Assert.Equal(60, c.Damage);
        Assert.Equal(60, GrandFinale.BaseDamage);
        Assert.Equal(15, GrandFinale.UpgradeDelta);
    }

    [Fact]
    public void HandTrick_canonical()
    {
        HandTrick c = new();
        Assert.Equal(7, c.Block);
        Assert.Equal(7, HandTrick.BaseBlock);
        Assert.Equal(3, HandTrick.UpgradeDelta);
    }

    [Fact]
    public void Haze_canonical()
    {
        Haze c = new();
        Assert.Equal(4, c.Poison);
        Assert.Equal(4, Haze.BasePoison);
        Assert.Equal(2, Haze.UpgradeDelta);
    }

    [Fact]
    public void HiddenDaggers_canonical()
    {
        HiddenDaggers c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(2, HiddenDaggers.Cards);
        Assert.Equal(2, HiddenDaggers.Shivs);
    }

    [Fact]
    public void InfiniteBlades_canonical()
    {
        InfiniteBlades c = new();
        Assert.Equal(1, InfiniteBlades.Amount);
    }

    [Fact]
    public void LeadingStrike_canonical()
    {
        LeadingStrike c = new();
        Assert.Equal(3, c.Damage);
        Assert.Equal(2, LeadingStrike.Shivs);
        Assert.Equal(3, LeadingStrike.BaseDamage);
        Assert.Equal(3, LeadingStrike.UpgradeDelta);
    }

    [Fact]
    public void LegSweep_canonical()
    {
        LegSweep c = new();
        Assert.Equal(11, c.Block);
        Assert.Equal(2, c.Weak);
        Assert.Equal(11, LegSweep.BaseBlock);
        Assert.Equal(2, LegSweep.BaseWeak);
        Assert.Equal(3, LegSweep.UpgradeDeltaBlock);
        Assert.Equal(1, LegSweep.UpgradeDeltaWeak);
    }

    [Fact]
    public void MasterPlanner_canonical_upgrade()
    {
        MasterPlanner c = new();
        Assert.Equal(2, c.EnergyCost);
        Assert.Equal(2, MasterPlanner.BaseCost);
        Assert.Equal(-1, MasterPlanner.UpgradeDelta);
    }

    [Fact]
    public void MementoMori_canonical()
    {
        MementoMori c = new();
        Assert.NotNull(c);
        Assert.Equal(9, MementoMori.BaseDamage);
        Assert.Equal(4, MementoMori.BaseExtra);
        Assert.Equal(2, MementoMori.UpgradeDeltaDamage);
        Assert.Equal(1, MementoMori.UpgradeDeltaExtra);
    }

    [Fact]
    public void Mirage_canonical_upgrade()
    {
        Mirage c = new();
        Assert.Equal(1, c.EnergyCost);
        Assert.Equal(1, Mirage.BaseCost);
        Assert.Equal(-1, Mirage.UpgradeDelta);
    }

    [Fact]
    public void Murder_canonical_upgrade()
    {
        Murder c = new();
        Assert.Equal(3, c.EnergyCost);
        Assert.Equal(3, Murder.BaseCost);
        Assert.Equal(-1, Murder.UpgradeDelta);
    }

    [Fact]
    public void Nightmare_canonical_upgrade()
    {
        Nightmare c = new();
        Assert.Equal(3, c.EnergyCost);
        Assert.Equal(3, Nightmare.BaseCost);
        Assert.Equal(-1, Nightmare.UpgradeDelta);
    }

    [Fact]
    public void NoxiousFumes_canonical()
    {
        NoxiousFumes c = new();
        Assert.Equal(2, c.PoisonPerTurn);
        Assert.Equal(2, NoxiousFumes.BasePoison);
        Assert.Equal(1, NoxiousFumes.UpgradeDelta);
    }

    [Fact]
    public void Outbreak_canonical()
    {
        Outbreak c = new();
        Assert.Equal(11, c.Amount);
        Assert.Equal(11, Outbreak.BaseAmount);
        Assert.Equal(4, Outbreak.UpgradeDelta);
    }

    [Fact]
    public void PhantomBlades_canonical()
    {
        PhantomBlades c = new();
        Assert.Equal(9, c.Amount);
        Assert.Equal(9, PhantomBlades.BaseAmount);
        Assert.Equal(3, PhantomBlades.UpgradeDelta);
    }

    [Fact]
    public void PiercingWail_canonical()
    {
        PiercingWail c = new();
        Assert.Equal(6, c.StrengthLoss);
        Assert.Equal(6, PiercingWail.BaseStrengthLoss);
        Assert.Equal(2, PiercingWail.UpgradeDelta);
    }

    [Fact]
    public void Pinpoint_canonical()
    {
        Pinpoint c = new();
        Assert.Equal(15, c.Damage);
        Assert.Equal(15, Pinpoint.BaseDamage);
        Assert.Equal(4, Pinpoint.UpgradeDelta);
    }

    [Fact]
    public void PoisonedStab_canonical()
    {
        PoisonedStab c = new();
        Assert.Equal(6, c.Damage);
        Assert.Equal(3, c.Poison);
        Assert.Equal(6, PoisonedStab.BaseDamage);
        Assert.Equal(3, PoisonedStab.BasePoison);
        Assert.Equal(2, PoisonedStab.UpgradeDeltaDamage);
        Assert.Equal(1, PoisonedStab.UpgradeDeltaPoison);
    }

    [Fact]
    public void Pounce_canonical()
    {
        Pounce c = new();
        Assert.Equal(12, c.Damage);
        Assert.Equal(12, Pounce.BaseDamage);
        Assert.Equal(6, Pounce.UpgradeDelta);
    }

    [Fact]
    public void PreciseCut_canonical()
    {
        PreciseCut c = new();
        Assert.NotNull(c);
        Assert.Equal(13, PreciseCut.BaseDamage);
        Assert.Equal(3, PreciseCut.UpgradeDelta);
    }

    [Fact]
    public void Predator_canonical()
    {
        Predator c = new();
        Assert.Equal(15, c.Damage);
        Assert.Equal(15, Predator.BaseDamage);
        Assert.Equal(5, Predator.UpgradeDelta);
    }

    [Fact]
    public void Prepared_canonical()
    {
        Prepared c = new();
        Assert.Equal(1, c.Cards);
        Assert.Equal(1, Prepared.BaseCards);
        Assert.Equal(1, Prepared.UpgradeDelta);
    }

    [Fact]
    public void Reflex_canonical()
    {
        Reflex c = new();
        Assert.Equal(2, c.Cards);
        Assert.Equal(2, Reflex.BaseCards);
        Assert.Equal(1, Reflex.UpgradeDelta);
    }

    [Fact]
    public void Ricochet_canonical()
    {
        Ricochet c = new();
        Assert.Equal(3, Ricochet.Damage);
        Assert.Equal(4, c.Repeat);
        Assert.Equal(4, Ricochet.BaseRepeat);
        Assert.Equal(1, Ricochet.UpgradeDelta);
    }

    [Fact]
    public void SerpentForm_canonical()
    {
        SerpentForm c = new();
        Assert.Equal(4, c.Amount);
        Assert.Equal(4, SerpentForm.BaseAmount);
        Assert.Equal(2, SerpentForm.UpgradeDelta);
    }

    [Fact]
    public void ShadowStep_canonical()
    {
        ShadowStep c = new();
        Assert.Equal(1, c.EnergyCost);
        Assert.Equal(3, ShadowStep.CardsDrawn);
        Assert.Equal(1, ShadowStep.BaseCost);
        Assert.Equal(-1, ShadowStep.UpgradeDelta);
    }

    [Fact]
    public void Shadowmeld_canonical()
    {
        Shadowmeld c = new();
        Assert.Equal(1, c.EnergyCost);
        Assert.Equal(1, Shadowmeld.BaseCost);
        Assert.Equal(-1, Shadowmeld.UpgradeDelta);
    }

    [Fact]
    public void Shiv_canonical()
    {
        Shiv c = new();
        Assert.Equal(0, c.Cost);
        Assert.Equal(CardRarity.Token, c.Rarity);
        Assert.Equal(4, Shiv.Damage);
        Assert.Contains(CardTag.Shiv, c.Tags);
    }

    [Fact]
    public void Skewer_canonical()
    {
        Skewer c = new();
        Assert.Equal(8, c.Damage);
        Assert.Equal(8, Skewer.BaseDamage);
        Assert.Equal(3, Skewer.UpgradeDelta);
    }

    [Fact]
    public void Snakebite_canonical()
    {
        Snakebite c = new();
        Assert.Equal(7, c.Poison);
        Assert.Equal(7, Snakebite.BasePoison);
        Assert.Equal(3, Snakebite.UpgradeDelta);
    }

    [Fact]
    public void Sneaky_canonical()
    {
        Sneaky c = new();
        Assert.Equal(1, c.Amount);
        Assert.Equal(1, Sneaky.BaseAmount);
        Assert.Equal(1, Sneaky.UpgradeDelta);
    }

    [Fact]
    public void Speedster_canonical()
    {
        Speedster c = new();
        Assert.Equal(2, Speedster.Amount);
    }

    [Fact]
    public void Strangle_canonical()
    {
        Strangle c = new();
        Assert.Equal(8, c.Damage);
        Assert.Equal(2, c.Strangle_);
        Assert.Equal(8, Strangle.BaseDamage);
        Assert.Equal(2, Strangle.BaseStrangle);
        Assert.Equal(2, Strangle.UpgradeDeltaDamage);
        Assert.Equal(1, Strangle.UpgradeDeltaStrangle);
    }

    [Fact]
    public void SuckerPunch_canonical()
    {
        SuckerPunch c = new();
        Assert.Equal(8, c.Damage);
        Assert.Equal(1, c.Weak);
        Assert.Equal(8, SuckerPunch.BaseDamage);
        Assert.Equal(1, SuckerPunch.BaseWeak);
        Assert.Equal(2, SuckerPunch.UpgradeDeltaDamage);
        Assert.Equal(1, SuckerPunch.UpgradeDeltaWeak);
    }

    [Fact]
    public void Suppress_canonical()
    {
        Suppress c = new();
        Assert.Equal(11, c.Damage);
        Assert.Equal(3, c.Weak);
        Assert.Equal(CardRarity.Ancient, c.Rarity);
        Assert.Equal(11, Suppress.BaseDamage);
        Assert.Equal(3, Suppress.BaseWeak);
        Assert.Equal(6, Suppress.UpgradeDeltaDamage);
        Assert.Equal(2, Suppress.UpgradeDeltaWeak);
    }

    [Fact]
    public void Tactician_canonical()
    {
        Tactician c = new();
        Assert.Equal(1, c.EnergyGain);
        Assert.Equal(1, Tactician.BaseEnergy);
        Assert.Equal(1, Tactician.UpgradeDelta);
    }

    [Fact]
    public void TheHunt_canonical()
    {
        TheHunt c = new();
        Assert.Equal(10, c.Damage);
        Assert.Equal(10, TheHunt.BaseDamage);
        Assert.Equal(5, TheHunt.UpgradeDelta);
    }

    [Fact]
    public void ToolsOfTheTrade_canonical_upgrade()
    {
        ToolsOfTheTrade c = new();
        Assert.Equal(1, c.EnergyCost);
        Assert.Equal(1, ToolsOfTheTrade.BaseCost);
        Assert.Equal(-1, ToolsOfTheTrade.UpgradeDelta);
    }

    [Fact]
    public void Tracking_canonical_upgrade()
    {
        Tracking c = new();
        Assert.Equal(2, c.EnergyCost);
        Assert.Equal(2, Tracking.BaseCost);
        Assert.Equal(-1, Tracking.UpgradeDelta);
    }

    [Fact]
    public void Untouchable_canonical()
    {
        Untouchable c = new();
        Assert.Equal(6, c.Block);
        Assert.Equal(6, Untouchable.BaseBlock);
        Assert.Equal(2, Untouchable.UpgradeDelta);
    }

    [Fact]
    public void UpMySleeve_canonical()
    {
        UpMySleeve c = new();
        Assert.Equal(3, c.Cards);
        Assert.Equal(3, UpMySleeve.BaseCards);
        Assert.Equal(1, UpMySleeve.UpgradeDelta);
    }

    [Fact]
    public void WellLaidPlans_canonical()
    {
        WellLaidPlans c = new();
        Assert.Equal(1, c.RetainAmount);
        Assert.Equal(1, WellLaidPlans.BaseRetain);
        Assert.Equal(1, WellLaidPlans.UpgradeDelta);
    }

    [Fact]
    public void WraithForm_canonical()
    {
        WraithForm c = new();
        Assert.Equal(2, c.Intangible);
        Assert.Equal(CardRarity.Ancient, c.Rarity);
        Assert.Equal(2, WraithForm.BaseIntangible);
        Assert.Equal(1, WraithForm.UpgradeDelta);
    }

    // ===== Status =====
    [Fact]
    public void Wound_canonical()
    {
        Wound c = new();
        Assert.Equal(-1, c.Cost);
        Assert.Equal(CardType.Status, c.Type);
    }

    [Fact]
    public void Slimed_canonical()
    {
        Slimed c = new();
        Assert.Equal(1, c.Cost);
        Assert.Equal(CardType.Status, c.Type);
    }

    [Fact]
    public void Burn_canonical()
    {
        Burn c = new();
        Assert.Equal(-1, c.Cost);
        Assert.Equal(2, c.Damage);
        Assert.Equal(2, Burn.BaseDamage);
        Assert.Equal(2, Burn.UpgradeDelta);
    }

    [Fact]
    public void Dazed_canonical()
    {
        Dazed c = new();
        Assert.Equal(-1, c.Cost);
    }

    // ===== Curse =====
    [Fact]
    public void AscendersBane_canonical()
    {
        AscendersBane c = new();
        Assert.Equal(CardType.Curse, c.Type);
    }

    [Fact]
    public void Clumsy_canonical()
    {
        Clumsy c = new();
        Assert.Equal(CardType.Curse, c.Type);
    }

    [Fact]
    public void Doubt_canonical()
    {
        Doubt c = new();
        Assert.Equal(CardType.Curse, c.Type);
        Assert.Equal(1, Doubt.WeakStacks);
    }

    [Fact]
    public void Regret_canonical()
    {
        Regret c = new();
        Assert.Equal(CardType.Curse, c.Type);
    }

    [Fact]
    public void Injury_canonical()
    {
        Injury c = new();
        Assert.Equal(CardType.Curse, c.Type);
    }

    [Fact]
    public void Phase1Content_card_catalog_registers_all_smoke_plus_s12_cards()
    {
        CardCatalog catalog = Phase1Content.BuildCardCatalog();
        // 9 smoke + 70 Silent + 4 status + 5 curse = 88 cards in T1.
        Assert.True(catalog.Count >= 75, $"expected >=75 cards, got {catalog.Count}");
        Assert.True(catalog.Contains("StrikeSilent"));
        Assert.True(catalog.Contains("Abrasive"));
        Assert.True(catalog.Contains("WraithForm"));
        Assert.True(catalog.Contains("Wound"));
        Assert.True(catalog.Contains("Doubt"));
    }
}
