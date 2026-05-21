using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T2 tests for <see cref="CombatState"/>: roundtrip via with-expressions,
/// enemy lookup, victory/defeat queries.
/// </summary>
public sealed class CombatStateTests
{
    private static Creature MakePlayer(int hp = 70) =>
        new(global::Sts2Headless.Domain.Combat.CreatureId.Player, "Silent", hp, 70, 0, ImmutableList<PowerInstance>.Empty, null, true);

    private static Creature MakeEnemy(global::Sts2Headless.Domain.Combat.CreatureId id, string name, int hp) =>
        new(id, name, hp, hp, 0, ImmutableList<PowerInstance>.Empty, MonsterIntent.None, false);

    private static CombatState MakeState(Creature? player = null, params Creature[] enemies)
    {
        return new CombatState(
            TurnCounter: 0,
            Phase: CombatPhase.CombatStart,
            Player: player ?? MakePlayer(),
            Enemies: ImmutableList.CreateRange(enemies),
            Energy: 0,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.Empty,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 0,
            MonsterRngCounter: 0
        );
    }

    [Fact]
    public void With_Roundtrip_Preserves_Identity()
    {
        var s1 = MakeState();
        var s2 = s1 with { TurnCounter = 5 };

        Assert.Equal(0, s1.TurnCounter);
        Assert.Equal(5, s2.TurnCounter);
        // Equal-valued state and original are NOT equal because TurnCounter differs.
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void Two_Equal_States_Are_Structurally_Equal()
    {
        var s1 = MakeState();
        var s2 = MakeState();
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void FindEnemy_Returns_Match_Or_Null()
    {
        var calc = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(1u), "CalcifiedCultist", 38);
        var damp = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(2u), "DampCultist", 51);
        var s = MakeState(null, calc, damp);

        Assert.Equal(calc, s.FindEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(1u)));
        Assert.Equal(damp, s.FindEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(2u)));
        Assert.Null(s.FindEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(99u)));
    }

    [Fact]
    public void GetEnemy_Throws_When_Missing()
    {
        var s = MakeState();
        Assert.Throws<InvalidOperationException>(() => s.GetEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(99u)));
    }

    [Fact]
    public void WithPlayer_Replaces_Player_Only()
    {
        var s = MakeState();
        var damaged = s.Player with { CurrentHp = 50 };
        var s2 = s.WithPlayer(damaged);

        Assert.Equal(70, s.Player.CurrentHp);
        Assert.Equal(50, s2.Player.CurrentHp);
    }

    [Fact]
    public void WithEnemy_Replaces_Single_Enemy_Preserving_Order()
    {
        var calc = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(1u), "CalcifiedCultist", 38);
        var damp = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(2u), "DampCultist", 51);
        var s = MakeState(null, calc, damp);

        var calcDamaged = calc with { CurrentHp = 30 };
        var s2 = s.WithEnemy(calcDamaged);

        Assert.Equal(2, s2.Enemies.Count);
        Assert.Equal(30, s2.Enemies[0].CurrentHp);
        Assert.Equal(51, s2.Enemies[1].CurrentHp);
        // Order preserved.
        Assert.Equal(1u, s2.Enemies[0].Id.Value);
        Assert.Equal(2u, s2.Enemies[1].Id.Value);
    }

    [Fact]
    public void WithPlayer_Throws_If_Given_Non_Player()
    {
        var s = MakeState();
        var monster = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(99u), "monster", 10);
        Assert.Throws<ArgumentException>(() => s.WithPlayer(monster));
    }

    [Fact]
    public void WithEnemy_Throws_If_Given_Player()
    {
        var s = MakeState();
        Assert.Throws<ArgumentException>(() => s.WithEnemy(s.Player));
    }

    [Fact]
    public void IsCombatOver_True_Only_At_CombatEnd_Phase()
    {
        var s = MakeState();
        Assert.False(s.IsCombatOver);
        var ended = s with { Phase = CombatPhase.CombatEnd };
        Assert.True(ended.IsCombatOver);
    }

    [Fact]
    public void PlayerWon_True_When_All_Enemies_Dead_And_Phase_End()
    {
        var calc = MakeEnemy(new global::Sts2Headless.Domain.Combat.CreatureId(1u), "CalcifiedCultist", 38);
        var dead = calc with { CurrentHp = 0 };
        var s = MakeState(null, dead);
        var ended = s with { Phase = CombatPhase.CombatEnd };

        Assert.True(ended.PlayerWon);
        Assert.False(ended.PlayerLost);
    }

    [Fact]
    public void PlayerLost_True_When_Player_Dead_And_Phase_End()
    {
        var player = MakePlayer(0);
        var s = MakeState(player);
        var ended = s with { Phase = CombatPhase.CombatEnd };

        Assert.True(ended.PlayerLost);
        Assert.False(ended.PlayerWon);
    }
}
