using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T5 tests for <see cref="CombatContext"/>: state-mutation surface used by
/// content code (cards, relics). Each test seeds a small CombatState, applies
/// one mutation, and asserts the new state field changed as expected.
/// </summary>
public sealed class CombatContextTests
{
    // === Test fixtures ====================================================

    private static Creature PlayerWith(
        int hp = 70,
        int block = 0,
        ImmutableList<PowerInstance>? powers = null
    ) =>
        new(
            Id: 0u,
            Name: "Silent",
            CurrentHp: hp,
            MaxHp: 70,
            Block: block,
            Powers: powers ?? ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true
        );

    private static Creature EnemyWith(
        uint id,
        int hp = 38,
        int block = 0,
        ImmutableList<PowerInstance>? powers = null
    ) =>
        new(
            Id: id,
            Name: "Cultist",
            CurrentHp: hp,
            MaxHp: hp,
            Block: block,
            Powers: powers ?? ImmutableList<PowerInstance>.Empty,
            Intent: MonsterIntent.None,
            IsPlayer: false
        );

    private static CombatContext NewContext(CombatState state, RunRngSet? runRng = null)
    {
        var rng = runRng ?? new RunRngSet("seed-0");
        var clock = new LogicalClock();
        return new CombatContext(
            initialState: state,
            runRng: rng,
            clock: clock,
            cards: SmokeContent.BuildCardCatalog(),
            relics: SmokeContent.BuildRelicCatalog(),
            powers: SmokeContent.BuildPowerCatalog(),
            monsters: SmokeContent.BuildMonsterCatalog(),
            encounters: SmokeContent.BuildEncounterCatalog(),
            plumbing: HookPlumbing.Empty(clock, rng.Shuffle)
        );
    }

    private static CombatState NewState(Creature? player = null, params Creature[] enemies) =>
        new(
            TurnCounter: 0,
            Phase: CombatPhase.CombatStart,
            Player: player ?? PlayerWith(),
            Enemies: ImmutableList.CreateRange(enemies),
            Energy: 3,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.Empty,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 0,
            MonsterRngCounter: 0
        );

    // === DealDamage =======================================================

    [Fact]
    public void DealDamage_Reduces_Block_First_Then_Hp()
    {
        var enemy = EnemyWith(1u, hp: 30, block: 5);
        var ctx = NewContext(NewState(null, enemy));
        ctx.DealDamage(1u, amount: 8, sourceId: 0u);

        var updated = ctx.State.GetEnemy(1u);
        Assert.Equal(0, updated.Block);
        Assert.Equal(27, updated.CurrentHp); // 30 - (8-5)
    }

    [Fact]
    public void DealDamage_All_Absorbed_By_Block_Leaves_Hp_Untouched()
    {
        var enemy = EnemyWith(1u, hp: 30, block: 10);
        var ctx = NewContext(NewState(null, enemy));
        ctx.DealDamage(1u, amount: 5, sourceId: 0u);

        var updated = ctx.State.GetEnemy(1u);
        Assert.Equal(5, updated.Block);
        Assert.Equal(30, updated.CurrentHp);
    }

    [Fact]
    public void DealDamage_Cannot_Reduce_Hp_Below_Zero()
    {
        var enemy = EnemyWith(1u, hp: 3, block: 0);
        var ctx = NewContext(NewState(null, enemy));
        ctx.DealDamage(1u, amount: 100, sourceId: 0u);

        Assert.Equal(0, ctx.State.GetEnemy(1u).CurrentHp);
    }

    [Fact]
    public void DealDamage_Zero_Or_Negative_Is_NoOp()
    {
        var enemy = EnemyWith(1u, hp: 30, block: 5);
        var ctx = NewContext(NewState(null, enemy));
        ctx.DealDamage(1u, 0, 0u);
        ctx.DealDamage(1u, -5, 0u);

        Assert.Equal(5, ctx.State.GetEnemy(1u).Block);
        Assert.Equal(30, ctx.State.GetEnemy(1u).CurrentHp);
    }

    // === GainBlock ========================================================

    [Fact]
    public void GainBlock_Adds_To_Existing_Block()
    {
        var ctx = NewContext(NewState());
        ctx.GainBlock(0u, 5);
        ctx.GainBlock(0u, 3);
        Assert.Equal(8, ctx.State.Player.Block);
    }

    // === ApplyPower =======================================================

    [Fact]
    public void ApplyPower_Counter_Stacks_Add_To_Existing()
    {
        var ctx = NewContext(NewState());
        ctx.ApplyPower(0u, PowerIds.Strength, 1, sourceId: 0u);
        ctx.ApplyPower(0u, PowerIds.Strength, 2, sourceId: 0u);

        var p = ctx.State.Player.Powers.Single(x => x.ModelId == PowerIds.Strength);
        Assert.Equal(3, p.Stacks);
    }

    [Fact]
    public void ApplyPower_Adds_New_Instance_When_Absent()
    {
        var ctx = NewContext(NewState());
        ctx.ApplyPower(0u, PowerIds.Poison, 5, sourceId: 1u);

        var p = ctx.State.Player.Powers.Single();
        Assert.Equal(PowerIds.Poison, p.ModelId);
        Assert.Equal(5, p.Stacks);
        Assert.Equal(1u, p.SourceCreatureId);
        Assert.True(p.JustApplied);
    }

    [Fact]
    public void ApplyPower_Marks_JustApplied()
    {
        var ctx = NewContext(NewState());
        ctx.ApplyPower(0u, PowerIds.Ritual, 2, sourceId: 1u);
        Assert.True(ctx.State.Player.Powers.Single().JustApplied);
    }

    // === Heal =============================================================

    [Fact]
    public void Heal_Clamps_At_MaxHp()
    {
        var player = PlayerWith(hp: 65);
        var ctx = NewContext(NewState(player));
        ctx.Heal(0u, 100);
        Assert.Equal(70, ctx.State.Player.CurrentHp);
    }

    [Fact]
    public void Heal_BloodVial_Two_Heals_Two()
    {
        var player = PlayerWith(hp: 65);
        var ctx = NewContext(NewState(player));
        ctx.Heal(0u, 2);
        Assert.Equal(67, ctx.State.Player.CurrentHp);
    }

    // === ModifyHandDrawSize ===============================================

    [Fact]
    public void ModifyHandDrawSize_Adds_Delta()
    {
        var ctx = NewContext(NewState());
        Assert.Equal(5, ctx.State.HandDrawSize);
        ctx.ModifyHandDrawSize(2);
        Assert.Equal(7, ctx.State.HandDrawSize);
    }

    [Fact]
    public void ModifyHandDrawSize_Clamps_At_Zero()
    {
        var ctx = NewContext(NewState());
        ctx.ModifyHandDrawSize(-100);
        Assert.Equal(0, ctx.State.HandDrawSize);
    }

    // === DrawCards ========================================================

    [Fact]
    public void DrawCards_Moves_Top_Of_Draw_To_Hand()
    {
        var draw = CardPile.OfRange(
            new[]
            {
                new CardInstance(1u, "StrikeSilent", 0, null),
                new CardInstance(2u, "DefendSilent", 0, null),
            }
        );
        var state = NewState() with { DrawPile = draw };
        var ctx = NewContext(state);
        ctx.DrawCards(1);

        Assert.Equal(1, ctx.State.DrawPile.Count);
        Assert.Equal(1, ctx.State.HandPile.Count);
        Assert.Equal(1u, ctx.State.HandPile.Cards[0].InstanceId);
    }

    [Fact]
    public void DrawCards_Reshuffles_Discard_When_Draw_Empty()
    {
        var runRng = new RunRngSet("seed-42");
        var discard = CardPile.OfRange(
            new[]
            {
                new CardInstance(1u, "StrikeSilent", 0, null),
                new CardInstance(2u, "DefendSilent", 0, null),
                new CardInstance(3u, "StrikeSilent", 0, null),
            }
        );
        var state = NewState() with { DiscardPile = discard };
        var ctx = NewContext(state, runRng);
        ctx.DrawCards(3);

        Assert.Equal(0, ctx.State.DrawPile.Count);
        Assert.Equal(0, ctx.State.DiscardPile.Count);
        Assert.Equal(3, ctx.State.HandPile.Count);
        // All three instance ids must be in hand (some order).
        Assert.Equal(
            new[] { 1u, 2u, 3u }.OrderBy(x => x),
            ctx.State.HandPile.Cards.Select(c => c.InstanceId).OrderBy(x => x)
        );
    }

    [Fact]
    public void DrawCards_Both_Piles_Empty_Stops_Early()
    {
        var ctx = NewContext(NewState());
        ctx.DrawCards(5);
        Assert.Equal(0, ctx.State.HandPile.Count);
    }

    // === DiscardHand ======================================================

    [Fact]
    public void DiscardHand_Moves_All_Hand_To_Discard()
    {
        var hand = CardPile.OfRange(
            new[]
            {
                new CardInstance(1u, "StrikeSilent", 0, null),
                new CardInstance(2u, "DefendSilent", 0, null),
            }
        );
        var state = NewState() with { HandPile = hand };
        var ctx = NewContext(state);
        ctx.DiscardHand();

        Assert.Equal(0, ctx.State.HandPile.Count);
        Assert.Equal(2, ctx.State.DiscardPile.Count);
    }

    // === IncreaseEnergy ===================================================

    [Fact]
    public void IncreaseEnergy_Adds_To_Current()
    {
        var ctx = NewContext(NewState());
        Assert.Equal(3, ctx.State.Energy);
        ctx.IncreaseEnergy(2);
        Assert.Equal(5, ctx.State.Energy);
    }

    // === SetState =========================================================

    [Fact]
    public void SetState_Replaces_State_Wholesale()
    {
        var ctx = NewContext(NewState());
        var newState = NewState() with { TurnCounter = 99, Phase = CombatPhase.EnemyTurnEnd };
        ctx.SetState(newState);
        Assert.Equal(99, ctx.State.TurnCounter);
        Assert.Equal(CombatPhase.EnemyTurnEnd, ctx.State.Phase);
    }
}
