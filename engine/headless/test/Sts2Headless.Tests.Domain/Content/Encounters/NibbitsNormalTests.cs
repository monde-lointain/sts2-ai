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
/// Tests for NibbitsNormal encounter (Wave-24/K.q1). Includes an integration test
/// that spawns via CombatEngine at seed 42 and verifies 2 Nibbits with per-slot
/// move overrides: slot 0 = SLICE_MOVE (front), slot 1 = HISS_MOVE (back).
/// </summary>
public class NibbitsNormalTests
{
    [Fact]
    public void NibbitsNormal_canonical_properties()
    {
        NibbitsNormal e = new();
        Assert.Equal("NibbitsNormal", e.Id);
        Assert.Equal(2, e.MonsterIds.Count);
        Assert.Equal(Nibbit.CanonicalId, e.MonsterIds[0]);
        Assert.Equal(Nibbit.CanonicalId, e.MonsterIds[1]);
    }

    [Fact]
    public void NibbitsNormal_GenerateMonstersWithMoves_returns_fixed_overrides()
    {
        NibbitsNormal e = new();
        var rng = new Rng(seed: 1u);
        var result = e.GenerateMonstersWithMoves(rng);
        Assert.Equal(2, result.Count);
        Assert.Equal(Nibbit.CanonicalId, result[0].MonsterId);
        Assert.Equal(Nibbit.SliceMoveId, result[0].InitialMoveIdOverride);
        Assert.Equal(Nibbit.CanonicalId, result[1].MonsterId);
        Assert.Equal(Nibbit.HissMoveId, result[1].InitialMoveIdOverride);
    }

    [Fact]
    public void NibbitsNormal_GenerateMonstersWithMoves_does_not_tick_rng()
    {
        NibbitsNormal e = new();
        var rng = new Rng(seed: 42u);
        int counterBefore = rng.Counter;
        _ = e.GenerateMonstersWithMoves(rng);
        Assert.Equal(counterBefore, rng.Counter);
    }

    /// <summary>
    /// Integration test: spawn NibbitsNormal via CombatEngine at seed 42.
    /// Asserts: 2 Creatures; both Name == "Nibbit";
    /// slot 0 Intent.MoveId == SLICE_MOVE; slot 1 Intent.MoveId == HISS_MOVE.
    /// </summary>
    [Fact]
    public void NibbitsNormal_seed42_spawns_2_Nibbits_with_SLICE_then_HISS()
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
        IEncounterModel enc = (IEncounterModel)encounters.Get(NibbitsNormal.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet("seed-42"),
            new LogicalClock()
        );

        Assert.Equal(2, ctx.State.Enemies.Count);

        Creature slot0 = ctx.State.Enemies[0];
        Assert.Equal("Nibbit", slot0.Name);
        Assert.NotNull(slot0.Intent);
        Assert.Equal(Nibbit.SliceMoveId, slot0.Intent!.MoveId);

        Creature slot1 = ctx.State.Enemies[1];
        Assert.Equal("Nibbit", slot1.Name);
        Assert.NotNull(slot1.Intent);
        Assert.Equal(Nibbit.HissMoveId, slot1.Intent!.MoveId);
    }
}
