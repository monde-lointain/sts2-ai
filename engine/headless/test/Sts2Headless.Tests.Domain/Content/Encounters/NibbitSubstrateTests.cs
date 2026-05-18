using System.Collections.Generic;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

/// <summary>
/// Wave-24/K.q1 substrate unit tests:
/// (a) GenerateMonstersWithMoves default returns null overrides identical to
///     legacy GenerateMonsters.
/// (b) Nibbit HISS_MOVE applies PowerIds.Strength stacks=2 (NOT Ritual).
/// (c) Nibbit SLICE_MOVE applies attack damage AND +5 self-block in same turn.
/// </summary>
public class NibbitSubstrateTests
{
    private static CombatContext BuildContext(string encounterId, string seed = "seed-42")
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
        IEncounterModel enc = (IEncounterModel)encounters.Get(encounterId);
        return CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet(seed),
            new LogicalClock()
        );
    }

    /// <summary>(a) GenerateMonstersWithMoves default wraps GenerateMonsters with null overrides.</summary>
    [Fact]
    public void GenerateMonstersWithMoves_default_returns_null_overrides_matching_GenerateMonsters()
    {
        // CultistsNormal does not override GenerateMonstersWithMoves — uses default.
        var enc = new CultistsNormal();
        var rng = new Rng(seed: 1u);
        IReadOnlyList<string> monsterIds = enc.GenerateMonsters(rng);
        var rng2 = new Rng(seed: 1u);
        var withMoves = enc.GenerateMonstersWithMoves(rng2);

        Assert.Equal(monsterIds.Count, withMoves.Count);
        for (int i = 0; i < monsterIds.Count; i++)
        {
            Assert.Equal(monsterIds[i], withMoves[i].MonsterId);
            Assert.Null(withMoves[i].InitialMoveIdOverride);
        }
    }

    /// <summary>(b) Nibbit HISS_MOVE applies Strength:2 (not Ritual) to self.</summary>
    [Fact]
    public void Nibbit_HISS_MOVE_applies_Strength_2_stacks_not_Ritual()
    {
        // Start NibbitsNormal: slot 1 starts HISS_MOVE.
        CombatContext ctx = BuildContext(NibbitsNormal.CanonicalId);

        // Slot 1 is the HISS starter.
        Creature hissNibbit = ctx.State.Enemies[1];
        Assert.Equal(Nibbit.HissMoveId, hissNibbit.Intent!.MoveId);

        // No powers on spawn.
        Assert.Empty(hissNibbit.Powers);

        // Run: end player turn, then enemy turn (both Nibbits act).
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        // The HISS Nibbit (slot 1, id = FirstEnemyId + 1) should now carry Strength:2.
        Creature hissAfter = ctx.State.Enemies[1];
        bool hasStrength = false;
        bool hasRitual = false;
        for (int i = 0; i < hissAfter.Powers.Count; i++)
        {
            if (hissAfter.Powers[i].ModelId == PowerIds.Strength && hissAfter.Powers[i].Stacks == 2)
                hasStrength = true;
            if (hissAfter.Powers[i].ModelId == PowerIds.Ritual)
                hasRitual = true;
        }
        Assert.True(hasStrength, "Nibbit HISS should apply Strength:2");
        Assert.False(hasRitual, "Nibbit HISS must NOT apply Ritual");
    }

    /// <summary>(c) Nibbit SLICE_MOVE deals damage AND gains +5 self-block in same turn.</summary>
    [Fact]
    public void Nibbit_SLICE_MOVE_deals_damage_and_gains_5_self_block()
    {
        // Start NibbitsNormal: slot 0 starts SLICE_MOVE.
        CombatContext ctx = BuildContext(NibbitsNormal.CanonicalId);

        Creature sliceNibbit = ctx.State.Enemies[0];
        Assert.Equal(Nibbit.SliceMoveId, sliceNibbit.Intent!.MoveId);
        Assert.Equal(0, sliceNibbit.Block);

        int playerHpBefore = ctx.State.Player.CurrentHp;

        // Run enemy turn.
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        // Slot-0 Nibbit should now have 5 block (SLICE self-block).
        Creature sliceAfter = ctx.State.Enemies[0];
        Assert.Equal(5, sliceAfter.Block);

        // Player should have taken damage from SLICE (6) AND HISS Nibbit's move.
        // HISS Nibbit applies a Buff (no player damage), so only 6 raw from SLICE.
        // No Strength on either Nibbit at this point (HISS fires Buff after SLICE fires Attack).
        // Player HP should be reduced by 6 (Nibbit.SliceDamage).
        Assert.Equal(playerHpBefore - Nibbit.SliceDamage, ctx.State.Player.CurrentHp);
    }
}
