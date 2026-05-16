using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T6 tests for <see cref="LegalActions"/>.
/// </summary>
public sealed class LegalActionsTests
{
    private static CardCatalog Cards => SmokeContent.BuildCardCatalog();

    private static Creature Player(int energy = 3) =>
        new(0u, "Silent", 70, 70, 0, ImmutableList<PowerInstance>.Empty, null, true);

    private static Creature EnemyAlive(uint id, string name = "CalcifiedCultist") =>
        new(id, name, 38, 38, 0, ImmutableList<PowerInstance>.Empty, MonsterIntent.None, false);

    private static Creature EnemyDead(uint id, string name = "CalcifiedCultist") =>
        new(id, name, 0, 38, 0, ImmutableList<PowerInstance>.Empty, MonsterIntent.None, false);

    private static CombatState State(
        int energy = 3,
        CombatPhase phase = CombatPhase.PlayerActing,
        IEnumerable<CardInstance>? hand = null,
        IEnumerable<Creature>? enemies = null
    )
    {
        return new CombatState(
            TurnCounter: 1,
            Phase: phase,
            Player: Player(),
            Enemies: ImmutableList.CreateRange(
                enemies ?? new[] { EnemyAlive(1u), EnemyAlive(2u, "DampCultist") }
            ),
            Energy: energy,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.Empty,
            HandPile: hand is null ? CardPile.Empty : CardPile.OfRange(hand),
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 0,
            MonsterRngCounter: 0
        );
    }

    [Fact]
    public void Empty_Hand_Returns_Only_EndTurn()
    {
        var s = State(hand: Array.Empty<CardInstance>());
        var actions = LegalActions.Enumerate(s, Cards);

        Assert.Single(actions);
        Assert.IsType<PlayerAction.EndTurn>(actions[0]);
    }

    [Fact]
    public void Single_Self_Target_Card_With_Energy_Plus_EndTurn()
    {
        var s = State(hand: new[] { new CardInstance(1u, DefendSilent.CanonicalId, 0, null) });
        var actions = LegalActions.Enumerate(s, Cards);

        Assert.Equal(2, actions.Length);
        Assert.Contains(
            actions,
            a =>
                a is PlayerAction.PlayCard pc && pc.CardInstanceId == 1u && pc.TargetEnemyId == null
        );
        Assert.Contains(actions, a => a is PlayerAction.EndTurn);
    }

    [Fact]
    public void AnyEnemy_Card_Yields_One_Action_Per_Living_Enemy()
    {
        var s = State(hand: new[] { new CardInstance(1u, StrikeSilent.CanonicalId, 0, null) });
        var actions = LegalActions.Enumerate(s, Cards);

        // 2 alive enemies → 2 PlayCard actions + EndTurn
        Assert.Equal(3, actions.Length);
        Assert.Equal(
            2,
            actions.OfType<PlayerAction.PlayCard>().Count(pc => pc.CardInstanceId == 1u)
        );
    }

    [Fact]
    public void Dead_Enemy_Not_Targetable()
    {
        var s = State(
            hand: new[] { new CardInstance(1u, StrikeSilent.CanonicalId, 0, null) },
            enemies: new[] { EnemyAlive(1u), EnemyDead(2u) }
        );
        var actions = LegalActions.Enumerate(s, Cards);

        var playCards = actions.OfType<PlayerAction.PlayCard>().ToList();
        Assert.Single(playCards);
        Assert.Equal(1u, playCards[0].TargetEnemyId);
    }

    [Fact]
    public void Card_Too_Expensive_Is_Excluded()
    {
        var s = State(
            energy: 0,
            hand: new[] { new CardInstance(1u, StrikeSilent.CanonicalId, 0, null) }
        );
        var actions = LegalActions.Enumerate(s, Cards);

        // No PlayCard actions — only EndTurn.
        Assert.Single(actions);
        Assert.IsType<PlayerAction.EndTurn>(actions[0]);
    }

    [Fact]
    public void Zero_Cost_Card_Always_Playable()
    {
        var s = State(
            energy: 0,
            hand: new[] { new CardInstance(1u, Neutralize.CanonicalId, 0, null) }
        ); // 0-energy attack
        var actions = LegalActions.Enumerate(s, Cards);

        // 2 enemies → 2 PlayCard actions for Neutralize + EndTurn
        Assert.Equal(3, actions.Length);
    }

    [Fact]
    public void EnemyTurn_Phase_Returns_Empty()
    {
        var s = State(phase: CombatPhase.EnemyActing);
        var actions = LegalActions.Enumerate(s, Cards);
        Assert.Empty(actions);
    }

    [Fact]
    public void CombatEnd_Phase_Returns_Empty()
    {
        var s = State(phase: CombatPhase.CombatEnd);
        var actions = LegalActions.Enumerate(s, Cards);
        Assert.Empty(actions);
    }

    [Fact]
    public void Cost_Override_Wins_Over_Model_Cost()
    {
        // Strike normally costs 1; override to 0 → playable with 0 energy.
        var s = State(
            energy: 0,
            hand: new[] { new CardInstance(1u, StrikeSilent.CanonicalId, 0, CostOverride: 0) }
        );
        var actions = LegalActions.Enumerate(s, Cards);
        Assert.Contains(actions, a => a is PlayerAction.PlayCard);
    }
}
