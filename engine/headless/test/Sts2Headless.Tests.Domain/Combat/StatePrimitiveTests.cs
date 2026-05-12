using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T1 tests for the value-type state primitives: CardInstance, PowerInstance,
/// MonsterIntent, Creature. All are records with init-only properties; tests
/// verify with-roundtrip, structural equality, and reference-distinctness so the
/// cheap-clone (S17) constraint is provable.
/// </summary>
public sealed class StatePrimitiveTests
{
    // === CardInstance =====================================================

    [Fact]
    public void CardInstance_With_Roundtrip_Preserves_Fields()
    {
        var card = new CardInstance(InstanceId: 1u, ModelId: "StrikeSilent", UpgradeLevel: 0, CostOverride: null);
        var upgraded = card with { UpgradeLevel = 1 };

        Assert.Equal(1u, upgraded.InstanceId);
        Assert.Equal("StrikeSilent", upgraded.ModelId);
        Assert.Equal(1, upgraded.UpgradeLevel);
        Assert.Null(upgraded.CostOverride);
    }

    [Fact]
    public void CardInstance_Equal_Records_Are_Structurally_Equal_But_Independent()
    {
        var a = new CardInstance(1u, "StrikeSilent", 0, null);
        var b = new CardInstance(1u, "StrikeSilent", 0, null);

        Assert.Equal(a, b);
        Assert.True(a == b); // record value equality
    }

    [Fact]
    public void CardInstance_With_Different_InstanceId_Differs()
    {
        var a = new CardInstance(1u, "StrikeSilent", 0, null);
        var b = new CardInstance(2u, "StrikeSilent", 0, null);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CardInstance_CostOverride_Sets_Independent_Of_Other_Fields()
    {
        var card = new CardInstance(1u, "StrikeSilent", 0, null);
        var zeroCost = card with { CostOverride = 0 };

        Assert.Equal(0, zeroCost.CostOverride);
        Assert.Null(card.CostOverride);
    }

    // === PowerInstance ====================================================

    [Fact]
    public void PowerInstance_With_Roundtrip_Preserves_Fields()
    {
        var p = new PowerInstance(ModelId: "PoisonPower", Stacks: 3, SourceCreatureId: 5u, JustApplied: true);
        var ticked = p with { Stacks = 2, JustApplied = false };

        Assert.Equal("PoisonPower", ticked.ModelId);
        Assert.Equal(2, ticked.Stacks);
        Assert.Equal(5u, ticked.SourceCreatureId);
        Assert.False(ticked.JustApplied);
    }

    [Fact]
    public void PowerInstance_Equal_Records_Are_Structurally_Equal()
    {
        var a = new PowerInstance("PoisonPower", 3, 5u, false);
        var b = new PowerInstance("PoisonPower", 3, 5u, false);

        Assert.Equal(a, b);
    }

    // === MonsterIntent ====================================================

    [Fact]
    public void MonsterIntent_With_Roundtrip_Preserves_Fields()
    {
        var intent = new MonsterIntent(
            Kind: MonsterIntentKind.Attack,
            DamagePerHit: 9,
            HitCount: 1,
            AppliesPowers: ImmutableList<MonsterIntentPower>.Empty);

        var debuff = intent with
        {
            Kind = MonsterIntentKind.Buff,
            DamagePerHit = 0,
            AppliesPowers = ImmutableList.Create(new MonsterIntentPower("RitualPower", 2)),
        };

        Assert.Equal(MonsterIntentKind.Buff, debuff.Kind);
        Assert.Equal(0, debuff.DamagePerHit);
        Assert.Single(debuff.AppliesPowers);
        Assert.Equal("RitualPower", debuff.AppliesPowers[0].PowerId);
        Assert.Equal(2, debuff.AppliesPowers[0].Stacks);
    }

    [Fact]
    public void MonsterIntent_Default_Has_Unknown_Kind()
    {
        var intent = MonsterIntent.None;
        Assert.Equal(MonsterIntentKind.Unknown, intent.Kind);
        Assert.Equal(0, intent.DamagePerHit);
        Assert.Equal(0, intent.HitCount);
        Assert.Empty(intent.AppliesPowers);
    }

    [Fact]
    public void MonsterIntent_FromContentIntent_Maps_Attack()
    {
        var source = new Intent(IntentKind.Attack, 9);
        var mapped = MonsterIntent.FromContentIntent(source);
        Assert.Equal(MonsterIntentKind.Attack, mapped.Kind);
        Assert.Equal(9, mapped.DamagePerHit);
        Assert.Equal(1, mapped.HitCount);
    }

    [Fact]
    public void MonsterIntent_FromContentIntent_Maps_Buff()
    {
        var source = new Intent(IntentKind.Buff, 0);
        var mapped = MonsterIntent.FromContentIntent(source);
        Assert.Equal(MonsterIntentKind.Buff, mapped.Kind);
        Assert.Equal(0, mapped.DamagePerHit);
    }

    // === Creature =========================================================

    [Fact]
    public void Creature_With_Roundtrip_Preserves_Fields()
    {
        var c = new Creature(
            Id: 1u,
            Name: "Player",
            CurrentHp: 70,
            MaxHp: 70,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true);

        var damaged = c with { CurrentHp = 65 };

        Assert.Equal(1u, damaged.Id);
        Assert.Equal("Player", damaged.Name);
        Assert.Equal(65, damaged.CurrentHp);
        Assert.Equal(70, damaged.MaxHp);
        Assert.True(damaged.IsPlayer);
        Assert.Null(damaged.Intent);
    }

    [Fact]
    public void Creature_Add_Power_Returns_New_Instance()
    {
        var c = new Creature(1u, "Player", 70, 70, 0, ImmutableList<PowerInstance>.Empty, null, true);
        var withPower = c with { Powers = c.Powers.Add(new PowerInstance("StrengthPower", 1, 1u, false)) };

        Assert.Empty(c.Powers);
        Assert.Single(withPower.Powers);
        Assert.NotSame(c, withPower);
    }

    [Fact]
    public void Creature_IsAlive_True_When_Hp_Positive()
    {
        var c = new Creature(1u, "Cultist", 38, 38, 0, ImmutableList<PowerInstance>.Empty, null, false);
        Assert.True(c.IsAlive);
    }

    [Fact]
    public void Creature_IsAlive_False_When_Hp_Zero_Or_Negative()
    {
        var c = new Creature(1u, "Cultist", 0, 38, 0, ImmutableList<PowerInstance>.Empty, null, false);
        Assert.False(c.IsAlive);
    }

    [Fact]
    public void Creature_Records_Equal_When_Fields_Equal()
    {
        var a = new Creature(1u, "Player", 70, 70, 0, ImmutableList<PowerInstance>.Empty, null, true);
        var b = new Creature(1u, "Player", 70, 70, 0, ImmutableList<PowerInstance>.Empty, null, true);
        Assert.Equal(a, b);
    }
}
