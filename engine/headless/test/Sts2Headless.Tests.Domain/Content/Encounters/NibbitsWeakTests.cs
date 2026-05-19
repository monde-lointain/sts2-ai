using System.Collections.Generic;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

/// <summary>
/// Tests for NibbitsWeak encounter (Wave-24/K.q1). Includes an integration test
/// that spawns via CombatEngine at seed 42 and verifies 1 Nibbit starting BUTT_MOVE.
/// </summary>
public class NibbitsWeakTests
{
    [Fact]
    public void NibbitsWeak_canonical_properties()
    {
        NibbitsWeak e = new();
        Assert.Equal("NibbitsWeak", e.Id);
        string monsterId = Assert.Single(e.MonsterIds);
        Assert.Equal(Nibbit.CanonicalId, monsterId);
    }

    [Fact]
    public void NibbitsWeak_GenerateMonstersWithMoves_returns_null_override()
    {
        NibbitsWeak e = new();
        var rng = new Rng(seed: 1u);
        var result = e.GenerateMonstersWithMoves(rng);
        Assert.Single(result);
        Assert.Equal(Nibbit.CanonicalId, result[0].MonsterId);
        Assert.Null(result[0].InitialMoveIdOverride);
    }

    /// <summary>
    /// Integration test: spawn NibbitsWeak via CombatEngine at seed 42.
    /// Asserts: 1 Creature, Name == "Nibbit", Intent.MoveId == BUTT_MOVE.
    /// </summary>
    [Fact]
    public void NibbitsWeak_seed42_spawns_1_Nibbit_with_BUTT_MOVE()
    {
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();

        var deck = new List<CardInstance>
        {
            new(100u, StrikeSilent.CanonicalId, 0, null),
            new(101u, DefendSilent.CanonicalId, 0, null),
            new(102u, StrikeSilent.CanonicalId, 0, null),
            new(103u, DefendSilent.CanonicalId, 0, null),
            new(104u, StrikeSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(NibbitsWeak.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet("seed-42"),
            new LogicalClock()
        );

        Assert.Single(ctx.State.Enemies);
        Creature nibbit = ctx.State.Enemies[0];
        Assert.Equal("Nibbit", nibbit.Name);
        Assert.NotNull(nibbit.Intent);
        Assert.Equal(Nibbit.ButtMoveId, nibbit.Intent!.MoveId);
    }
}
