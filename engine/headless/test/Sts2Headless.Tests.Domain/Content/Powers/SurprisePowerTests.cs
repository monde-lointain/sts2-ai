using System.Collections.Generic;
using System.Linq;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Powers;

/// <summary>
/// Runtime integration tests for SurprisePower (Wave-26/Q1.D).
///
/// <para>
/// Verifies that when GremlinMerc dies in a Phase-1 combat, SurprisePower's
/// AfterDeath hook fires and adds exactly one SneakyGremlin + one FatGremlin
/// to the enemy list, and that combat does NOT end at that moment (the
/// ShouldStopCombatFromEnding veto is active until the spawn lands).
/// </para>
/// </summary>
public sealed class SurprisePowerTests
{
    // =========================================================================
    // Boot helpers
    // =========================================================================

    private static CombatBootstrap BuildBootstrap() =>
        new(
            Phase1Content.BuildCardCatalog(),
            Phase1Content.BuildRelicCatalog(),
            Phase1Content.BuildPowerCatalog(),
            Phase1Content.BuildMonsterCatalog(),
            Phase1Content.BuildEncounterCatalog()
        );

    private static CombatContext BootGremlinMercNormal(string seed = "gremlin-merc-seed")
    {
        CombatBootstrap bootstrap = BuildBootstrap();
        // Minimal deck: enough Strikes to kill GremlinMerc quickly.
        var deck = new List<CardInstance>();
        uint cid = 100u;
        for (int i = 0; i < 10; i++)
            deck.Add(new CardInstance(cid++, StrikeSilent.CanonicalId, 0, null));

        IEncounterModel enc = bootstrap.Encounters.Get(GremlinMercNormal.CanonicalId);
        return CombatEngine.StartCombat(
            enc,
            bootstrap,
            new PlayerSpec(
                RelicIds: new[] { RingOfTheSnake.CanonicalId },
                Deck: deck,
                InitialHp: 70
            ),
            new RunRngSet(seed),
            new LogicalClock()
        );
    }

    // =========================================================================
    // 1. GremlinMerc starts with SurprisePower + ThieveryPower
    // =========================================================================

    [Fact]
    public void GremlinMerc_starts_combat_with_SurprisePower_and_ThieveryPower_stamped()
    {
        CombatContext ctx = BootGremlinMercNormal();
        Creature gremlin = ctx.State.Enemies.Single();
        Assert.Equal("GremlinMerc", gremlin.Name);
        Assert.Contains(gremlin.Powers, p => p.ModelId == PowerIds.Surprise);
        Assert.Contains(gremlin.Powers, p => p.ModelId == PowerIds.Thievery);
    }

    // =========================================================================
    // 2. Killing GremlinMerc spawns SneakyGremlin + FatGremlin
    // =========================================================================

    [Fact]
    public void Killing_GremlinMerc_spawns_SneakyGremlin_and_FatGremlin()
    {
        CombatContext ctx = BootGremlinMercNormal();
        uint gremlinId = ctx.State.Enemies.Single().Id;

        // Drop GremlinMerc to 1 HP so a single Strike kills it.
        Creature gremlin = ctx.State.GetEnemy(gremlinId);
        ctx.SetState(ctx.State.WithEnemy(gremlin with { CurrentHp = 1, Block = 0 }));

        // Find a Strike in hand and play it targeting the gremlin.
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, targetEnemyId: gremlinId);

        // After the kill, SurprisePower's AfterDeath should have fired and added
        // SneakyGremlin + FatGremlin to the enemy list.
        IReadOnlyList<Creature> enemies = ctx.State.Enemies;
        Assert.Contains(enemies, e => e.Name == SneakyGremlin.CanonicalId && e.IsAlive);
        Assert.Contains(enemies, e => e.Name == FatGremlin.CanonicalId && e.IsAlive);
    }

    [Fact]
    public void Spawned_gremlins_have_valid_hp_and_correct_initial_intent()
    {
        CombatContext ctx = BootGremlinMercNormal("gremlin-spawn-hp-seed");
        uint gremlinId = ctx.State.Enemies.Single().Id;

        Creature gremlin = ctx.State.GetEnemy(gremlinId);
        ctx.SetState(ctx.State.WithEnemy(gremlin with { CurrentHp = 1, Block = 0 }));

        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, targetEnemyId: gremlinId);

        Creature sneaky = ctx.State.Enemies.Single(e => e.Name == SneakyGremlin.CanonicalId);
        Creature fat = ctx.State.Enemies.Single(e => e.Name == FatGremlin.CanonicalId);

        // HP must be in the A0 envelope.
        Assert.InRange(sneaky.CurrentHp, SneakyGremlin.MinHp, SneakyGremlin.MaxHp);
        Assert.InRange(fat.CurrentHp, FatGremlin.MinHp, FatGremlin.MaxHp);

        // Initial intent: SPAWNED_MOVE maps IntentKind.Stun → MonsterIntentKind.Unknown
        // (no Stun entry in MonsterIntent.FromContentIntent; falls through to None).
        Assert.NotNull(sneaky.Intent);
        Assert.NotNull(fat.Intent);
        Assert.Equal(MonsterIntentKind.Unknown, sneaky.Intent!.Kind);
        Assert.Equal(MonsterIntentKind.Unknown, fat.Intent!.Kind);
    }

    [Fact]
    public void Spawned_gremlins_have_unique_ids_greater_than_dead_GremlinMerc()
    {
        CombatContext ctx = BootGremlinMercNormal("gremlin-id-seed");
        uint gremlinId = ctx.State.Enemies.Single().Id;

        Creature gremlin = ctx.State.GetEnemy(gremlinId);
        ctx.SetState(ctx.State.WithEnemy(gremlin with { CurrentHp = 1, Block = 0 }));

        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, targetEnemyId: gremlinId);

        Creature sneaky = ctx.State.Enemies.Single(e => e.Name == SneakyGremlin.CanonicalId);
        Creature fat = ctx.State.Enemies.Single(e => e.Name == FatGremlin.CanonicalId);

        // Ids must be distinct and greater than the dead GremlinMerc's id.
        Assert.NotEqual(sneaky.Id, fat.Id);
        Assert.True(
            sneaky.Id > gremlinId,
            $"SneakyGremlin id {sneaky.Id} <= GremlinMerc id {gremlinId}"
        );
        Assert.True(
            fat.Id > gremlinId,
            $"FatGremlin id {fat.Id} <= GremlinMerc id {gremlinId}"
        );
    }

    // =========================================================================
    // 3. Combat does NOT end when only GremlinMerc is dead (spawn pending)
    // =========================================================================

    [Fact]
    public void Combat_does_not_end_immediately_when_GremlinMerc_killed()
    {
        CombatContext ctx = BootGremlinMercNormal("gremlin-no-end-seed");
        uint gremlinId = ctx.State.Enemies.Single().Id;

        Creature gremlin = ctx.State.GetEnemy(gremlinId);
        ctx.SetState(ctx.State.WithEnemy(gremlin with { CurrentHp = 1, Block = 0 }));

        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, targetEnemyId: gremlinId);

        // The spawn must have landed; the combat should still be going.
        Assert.False(
            ctx.State.IsCombatOver,
            "Combat ended prematurely — ShouldStopCombatFromEnding veto did not prevent early end."
        );
        Assert.Equal(CombatPhase.PlayerActing, ctx.State.Phase);
    }

    // =========================================================================
    // 4. SurprisePower metadata
    // =========================================================================

    [Fact]
    public void SurprisePower_has_correct_id_type_and_stack_type()
    {
        SurprisePower sp = new();
        Assert.Equal(PowerIds.Surprise, sp.Id);
        Assert.Equal("SurprisePower", sp.Id);
        Assert.Equal(PowerType.Buff, sp.Type);
        Assert.Equal(PowerStackType.Single, sp.StackType);
    }
}
