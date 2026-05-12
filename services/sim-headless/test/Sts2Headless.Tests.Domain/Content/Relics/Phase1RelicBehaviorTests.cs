using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Relics;

/// <summary>
/// Stream-B-T2: behavior tests for the Phase-1 relic SubscribeHooks bodies
/// extended in this stream. Each test installs the relic, fires its hook
/// type, drains the action queue with an attached <see cref="EffectObserver"/>,
/// and verifies the captured action shape matches upstream.
/// </summary>
public sealed class Phase1RelicBehaviorTests
{
    private static ExecutionContext NewCtx()
        => new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    private static IReadOnlyList<IAction> FireAndCollect(RelicModel relic, HookType hookType)
    {
        ExecutionContext ctx = NewCtx();
        relic.OnAdded(ctx);
        using (EffectObserver.Attach(out List<IAction> log))
        {
            ctx.Hooks.Fire(hookType, new HookContext(ctx));
            ctx.Queue.Drain(ctx);
            return log;
        }
    }

    // ===== BronzeScales (upstream src/Core/Models/Relics/BronzeScales.cs) =====

    [Fact]
    public void BronzeScales_OnBeforeCombatStart_enqueues_3_thorns_to_self()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(new BronzeScales(), HookType.BeforeCombatStart);
        Assert.Single(actions);
        ApplyPowerAction p = Assert.IsType<ApplyPowerAction>(actions[0]);
        Assert.Equal(PowerIds.Thorns, p.PowerId);
        Assert.Equal(BronzeScales.ThornsAmount, p.Amount);
        Assert.Null(p.Target); // self
    }

    // ===== BagOfMarbles (upstream src/Core/Models/Relics/BagOfMarbles.cs) =====

    [Fact]
    public void BagOfMarbles_OnBeforeCombatStart_fans_1_vulnerable_to_all_enemies()
    {
        IReadOnlyList<IAction> actions = FireAndCollect(new BagOfMarbles(), HookType.BeforeCombatStart);
        Assert.Single(actions);
        ApplyPowerToAllEnemiesAction p = Assert.IsType<ApplyPowerToAllEnemiesAction>(actions[0]);
        Assert.Equal(PowerIds.Vulnerable, p.PowerId);
        Assert.Equal(BagOfMarbles.VulnerableStacks, p.Amount);
    }

    // ===== Integration: BagOfMarbles fan-out through CombatEngine =====

    [Fact]
    public void BagOfMarbles_through_CombatEngine_stamps_Vulnerable_on_every_enemy()
    {
        // Use an encounter with multiple enemies so the fan-out is observable.
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();

        var deck = new List<CardInstance>
        {
            new(100u, StrikeSilent.CanonicalId, 0, null),
            new(101u, StrikeSilent.CanonicalId, 0, null),
            new(102u, StrikeSilent.CanonicalId, 0, null),
            new(103u, DefendSilent.CanonicalId, 0, null),
            new(104u, DefendSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(ChompersNormal.CanonicalId);
        // Both Chompers should end up with 1 Vulnerable.
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { BagOfMarbles.CanonicalId }, Deck: deck),
            new RunRngSet("seed-42"),
            new LogicalClock());

        Assert.Equal(2, ctx.State.Enemies.Count);
        foreach (Creature e in ctx.State.Enemies)
        {
            bool hasVuln = false;
            foreach (PowerInstance p in e.Powers)
            {
                if (p.ModelId == PowerIds.Vulnerable && p.Stacks == 1) { hasVuln = true; break; }
            }
            Assert.True(hasVuln, $"Enemy {e.Id} ({e.Name}) must have 1 Vulnerable after BagOfMarbles fan-out.");
        }
    }

    [Fact]
    public void BronzeScales_through_CombatEngine_stamps_Thorns_on_player()
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
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { BronzeScales.CanonicalId }, Deck: deck),
            new RunRngSet("seed-42"),
            new LogicalClock());

        bool hasThorns = false;
        foreach (PowerInstance p in ctx.State.Player.Powers)
        {
            if (p.ModelId == PowerIds.Thorns && p.Stacks == BronzeScales.ThornsAmount)
            { hasThorns = true; break; }
        }
        Assert.True(hasThorns, $"Player must have {BronzeScales.ThornsAmount} Thorns after BronzeScales hook.");
    }

    // ===== B.1-gamma-T4: ports of deferred SubscribeHooks =====

    private static CombatContext StartBasicCombatWith(string relicId, string deckSeed = "seed-42")
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
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        return CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { relicId }, Deck: deck),
            new RunRngSet(deckSeed),
            new LogicalClock());
    }

    private static int PowerStacks(Creature c, string powerId)
    {
        foreach (PowerInstance p in c.Powers)
        {
            if (p.ModelId == powerId) return p.Stacks;
        }
        return 0;
    }

    [Fact]
    public void Akabeko_stamps_Vigor_on_combat_start()
    {
        CombatContext ctx = StartBasicCombatWith(Akabeko.CanonicalId);
        Assert.Equal(Akabeko.VigorAmount, PowerStacks(ctx.State.Player, PowerIds.Vigor));
    }

    [Fact]
    public void OddlySmoothStone_stamps_Dexterity_on_combat_start()
    {
        CombatContext ctx = StartBasicCombatWith(OddlySmoothStone.CanonicalId);
        Assert.Equal(OddlySmoothStone.DexterityAmount,
            PowerStacks(ctx.State.Player, PowerIds.Dexterity));
    }

    [Fact]
    public void DataDisk_stamps_Focus_on_combat_start()
    {
        CombatContext ctx = StartBasicCombatWith(DataDisk.CanonicalId);
        Assert.Equal(DataDisk.FocusAmount,
            PowerStacks(ctx.State.Player, PowerIds.Focus));
    }

    [Fact]
    public void Pantograph_heals_player_on_combat_start()
    {
        CombatContext ctx = StartBasicCombatWith(Pantograph.CanonicalId);
        // Player starts at full HP (70/70); Heal is capped at MaxHp, so visible
        // effect is "still at 70". To make the heal visible, start the player
        // damaged. Simulate by directly setting initialPlayerHp lower.
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();
        var deck = new List<CardInstance>
        {
            new(100u, StrikeSilent.CanonicalId, 0, null),
            new(101u, DefendSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(
                RelicIds: new[] { Pantograph.CanonicalId },
                Deck: deck,
                InitialHp: 30,
                MaxHp: 70),
            new RunRngSet("seed-pant"),
            new LogicalClock());
        // 30 + 25 heal = 55 (capped at 70).
        Assert.Equal(55, ctx.State.Player.CurrentHp);
    }

    [Fact]
    public void MercuryHourglass_deals_3_AoE_on_player_turn_start()
    {
        // MercuryHourglass fires AfterPlayerTurnStart. StartCombat fires that
        // hook at round 1 start.
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();
        var deck = new List<CardInstance>
        {
            new(100u, StrikeSilent.CanonicalId, 0, null),
            new(101u, DefendSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(ChompersNormal.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { MercuryHourglass.CanonicalId }, Deck: deck),
            new RunRngSet("seed-mh"),
            new LogicalClock());
        // Both Chompers should have taken 3 damage.
        foreach (Creature e in ctx.State.Enemies)
        {
            Assert.Equal(e.MaxHp - MercuryHourglass.DamageToAll, e.CurrentHp);
        }
    }

    [Fact]
    public void Orichalcum_grants_block_at_turn_end_when_no_block()
    {
        // Run a full player turn without gaining any block, then EndPlayerTurn.
        // Orichalcum should grant 6 block before the turn-end transition.
        CombatContext ctx = StartBasicCombatWith(Orichalcum.CanonicalId);
        Assert.Equal(0, ctx.State.Player.Block);
        CombatEngine.EndPlayerTurn(ctx);
        // Phase has transitioned to EnemyTurnStart; the conditional hook fired
        // BEFORE that transition. Player should now have 6 block.
        Assert.Equal(Orichalcum.BlockOnTurnEndIfZero, ctx.State.Player.Block);
    }

    [Fact]
    public void Orichalcum_does_not_grant_block_when_block_present()
    {
        // Pre-gain block by playing a Defend, then EndPlayerTurn — Orichalcum
        // gate predicate should see non-zero block and skip the grant.
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();
        var deck = new List<CardInstance>
        {
            new(100u, DefendSilent.CanonicalId, 0, null),
            new(101u, DefendSilent.CanonicalId, 0, null),
            new(102u, DefendSilent.CanonicalId, 0, null),
            new(103u, DefendSilent.CanonicalId, 0, null),
            new(104u, DefendSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { Orichalcum.CanonicalId }, Deck: deck),
            new RunRngSet("seed-ori-block"),
            new LogicalClock());
        // Play a Defend to gain some block (5 base).
        uint defendId = ctx.State.HandPile.Cards.First(c => c.ModelId == DefendSilent.CanonicalId).InstanceId;
        CombatEngine.PlayerPlayCard(ctx, defendId, null);
        int blockBeforeEnd = ctx.State.Player.Block;
        Assert.True(blockBeforeEnd > 0, "Player should have block after a Defend.");
        CombatEngine.EndPlayerTurn(ctx);
        // Block did not increase by Orichalcum's amount (Orichalcum's predicate
        // saw non-zero block and skipped).
        Assert.Equal(blockBeforeEnd, ctx.State.Player.Block);
    }
}
