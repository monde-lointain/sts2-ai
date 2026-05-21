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

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Stream-B-T4: pin the calc-damage / calc-block formula evaluator. Each test
/// builds a real <see cref="CombatContext"/>, mutates the relevant aggregate
/// (e.g., attacks-played-this-turn), then plays the formula card and asserts
/// the enemy / player ends up with the upstream-defined damage / block.
/// </summary>
public sealed class CalcDamageCardTests
{
    private static (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) StartBasicCombat(uint seed)
    {
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
            new(110u, Finisher.CanonicalId, 0, null),
            new(111u, Murder.CanonicalId, 0, null),
            new(112u, Mirage.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet($"seed-{seed}"),
            new LogicalClock()
        );
        return (ctx, CombatEngine.FirstEnemyId);
    }

    // ===== CalcDamageAction direct unit test =====

    [Fact]
    public void CalcDamageAction_records_into_observer()
    {
        var obs = ListActionObserver.Create(out List<IAction> log);
        ExecutionContext ec = new(
            new LogicalClock(),
            new Rng(0u),
            new HookRegistry(),
            new ActionQueue(),
            obs
        );
        new CalcDamageAction(6, "attacks_played_this_turn", new global::Sts2Headless.Domain.Combat.CreatureId(1u)).Execute(ec);
        Assert.Single(log);
        CalcDamageAction a = Assert.IsType<CalcDamageAction>(log[0]);
        Assert.Equal(6, a.BaseDamage);
        Assert.Equal("attacks_played_this_turn", a.MultiplierKey);
        Assert.Equal((global::Sts2Headless.Domain.Combat.CreatureId?)new global::Sts2Headless.Domain.Combat.CreatureId(1u), a.Target);
    }

    // ===== Finisher =====

    [Fact]
    public void Finisher_with_zero_prior_attacks_deals_zero_damage()
    {
        // Force a Finisher to be the first attack played: target enemy takes 0 damage.
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) = StartBasicCombat(seed);
            CardInstance? finisher = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Finisher.CanonicalId)
                {
                    finisher = c;
                    break;
                }
            }
            if (finisher is null)
                continue;
            // Make sure no prior attacks this turn.
            Assert.Equal(0, ctx.State.Trail.AttacksPlayedThisTurn);
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, finisher!.InstanceId, enemyId);
            int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
            Assert.Equal(hpBefore, hpAfter); // 0 attacks × 6 = 0 damage
            // But Finisher itself is now counted.
            Assert.Equal(1, ctx.State.Trail.AttacksPlayedThisTurn);
            return;
        }
        Assert.Fail("Could not find seed where Finisher is in opening hand.");
    }

    [Fact]
    public void Finisher_after_two_strikes_deals_12_damage()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) = StartBasicCombat(seed);
            CardInstance? finisher = null;
            var strikes = new List<CardInstance>();
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Finisher.CanonicalId)
                    finisher = c;
                else if (c.ModelId == StrikeSilent.CanonicalId)
                    strikes.Add(c);
            }
            if (finisher is null || strikes.Count < 2)
                continue;

            // Play 2 strikes (2 attacks played); then Finisher.
            CombatEngine.PlayerPlayCard(ctx, strikes[0].InstanceId, enemyId);
            CombatEngine.PlayerPlayCard(ctx, strikes[1].InstanceId, enemyId);
            Assert.Equal(2, ctx.State.Trail.AttacksPlayedThisTurn);
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, finisher!.InstanceId, enemyId);
            int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
            // Finisher: 6 × 2 = 12 damage.
            Assert.Equal(hpBefore - 12, hpAfter);
            Assert.Equal(3, ctx.State.Trail.AttacksPlayedThisTurn);
            return;
        }
        Assert.Fail("Could not find seed with Finisher + 2 Strikes in opening hand.");
    }

    // ===== Murder =====

    [Fact]
    public void Murder_damage_scales_with_cards_drawn_this_combat()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) = StartBasicCombat(seed);
            CardInstance? murder = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Murder.CanonicalId)
                {
                    murder = c;
                    break;
                }
            }
            if (murder is null)
                continue;
            int drawnSoFar = ctx.State.Trail.CardsDrawnThisCombat;
            Assert.True(drawnSoFar > 0, "Initial hand draw should have happened.");
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, murder!.InstanceId, enemyId);
            int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
            // Murder: 1 × cardsDrawn raw damage.
            Assert.Equal(hpBefore - drawnSoFar, hpAfter);
            return;
        }
        Assert.Fail("Could not find seed with Murder in opening hand.");
    }

    // ===== Mirage =====

    [Fact]
    public void Mirage_block_equals_total_enemy_poison()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) = StartBasicCombat(seed);
            CardInstance? mirage = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Mirage.CanonicalId)
                {
                    mirage = c;
                    break;
                }
            }
            if (mirage is null)
                continue;
            // Manually stamp 5 Poison on the lone enemy.
            ctx.ApplyPower(enemyId, PowerIds.Poison, 5, enemyId);
            int blockBefore = ctx.State.Player.Block;
            CombatEngine.PlayerPlayCard(ctx, mirage!.InstanceId, null);
            int blockAfter = ctx.State.Player.Block;
            // Mirage: 0 base + 5 poison total = 5 block.
            Assert.Equal(blockBefore + 5, blockAfter);
            return;
        }
        Assert.Fail("Could not find seed with Mirage in opening hand.");
    }

    // ===== CombatState aggregates =====

    [Fact]
    public void CardsDrawnThisCombat_increments_per_draw()
    {
        (CombatContext ctx, _) = StartBasicCombat(seed: 42u);
        // Initial hand draw at combat start brings HandPile.Cards.Count cards
        // — the counter should match.
        Assert.Equal(ctx.State.HandPile.Cards.Count, ctx.State.Trail.CardsDrawnThisCombat);

        int drawnSoFar = ctx.State.Trail.CardsDrawnThisCombat;
        // Draw 1 — should always succeed because draw pile has the rest of the deck.
        ctx.DrawCards(1);
        Assert.Equal(drawnSoFar + 1, ctx.State.Trail.CardsDrawnThisCombat);
    }

    [Fact]
    public void AttacksPlayedThisTurn_resets_at_StartPlayerTurn()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId) = StartBasicCombat(seed);
            CardInstance? strike = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == StrikeSilent.CanonicalId)
                {
                    strike = c;
                    break;
                }
            }
            if (strike is null)
                continue;
            CombatEngine.PlayerPlayCard(ctx, strike!.InstanceId, enemyId);
            Assert.Equal(1, ctx.State.Trail.AttacksPlayedThisTurn);
            // End turn, enemy turn, then a new player turn.
            CombatEngine.EndPlayerTurn(ctx);
            CombatEngine.EnemyTurn(ctx);
            if (ctx.State.IsCombatOver)
                return; // ignore if combat ended
            CombatEngine.StartPlayerTurn(ctx);
            Assert.Equal(0, ctx.State.Trail.AttacksPlayedThisTurn);
            return;
        }
        Assert.Fail("Could not find seed with StrikeSilent for AttacksPlayedThisTurn test.");
    }

    // ===== B.1-gamma-T5: X-cost cards =====

    private static (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance xCardOrNull) StartCombatWithXCard(
        uint seed,
        string cardId
    )
    {
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
            new(120u, cardId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(FuzzyWurmCrawlerSolo.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet($"xc-{seed}"),
            new LogicalClock()
        );
        CardInstance? found = null;
        foreach (CardInstance c in ctx.State.HandPile.Cards)
        {
            if (c.ModelId == cardId)
            {
                found = c;
                break;
            }
        }
        return (ctx, CombatEngine.FirstEnemyId, found!);
    }

    [Fact]
    public void Skewer_consumes_all_energy_and_deals_8_per_unit_spent()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance? skewer) = StartCombatWithXCard(
                seed,
                Skewer.CanonicalId
            );
            if (skewer is null)
                continue;
            int energyBefore = ctx.State.Energy; // 3 for Silent
            Assert.Equal(3, energyBefore);
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, skewer!.InstanceId, enemyId);
            // X = 3: deal 8 dmg x 3 = 24.
            Assert.Equal(0, ctx.State.Energy);
            Assert.Equal(3, ctx.State.Trail.LastSpentEnergy);
            int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
            Assert.Equal(hpBefore - (Skewer.BaseDamage * 3), hpAfter);
            return;
        }
        Assert.Fail("Could not find seed with Skewer in opening hand.");
    }

    [Fact]
    public void Skewer_at_zero_energy_deals_zero_damage()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance? skewer) = StartCombatWithXCard(
                seed,
                Skewer.CanonicalId
            );
            if (skewer is null)
                continue;
            // Burn all 3 energy first via strikes.
            var strikes = new List<CardInstance>();
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == StrikeSilent.CanonicalId)
                    strikes.Add(c);
            }
            if (strikes.Count < 3)
                continue;
            for (int i = 0; i < 3; i++)
            {
                CombatEngine.PlayerPlayCard(ctx, strikes[i].InstanceId, enemyId);
            }
            Assert.Equal(0, ctx.State.Energy);
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, skewer!.InstanceId, enemyId);
            // Skewer at 0 energy: 0 hits, 0 damage.
            Assert.Equal(hpBefore, ctx.State.GetEnemy(enemyId).CurrentHp);
            return;
        }
        Assert.Fail("Could not find seed with Skewer + 3 Strikes in opening hand.");
    }

    [Fact]
    public void Malaise_applies_negative_strength_and_weak_equal_to_X()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance? malaise) = StartCombatWithXCard(
                seed,
                Malaise.CanonicalId
            );
            if (malaise is null)
                continue;
            CombatEngine.PlayerPlayCard(ctx, malaise!.InstanceId, enemyId);
            // Energy was 3. So Strength = -3, Weak = +3.
            Creature enemy = ctx.State.GetEnemy(enemyId);
            int strength = 0,
                weak = 0;
            foreach (PowerInstance p in enemy.Powers)
            {
                if (p.ModelId == PowerIds.Strength)
                    strength = p.Stacks;
                if (p.ModelId == PowerIds.Weak)
                    weak = p.Stacks;
            }
            Assert.Equal(-3, strength);
            Assert.Equal(3, weak);
            return;
        }
        Assert.Fail("Could not find seed with Malaise in opening hand.");
    }

    [Fact]
    public void KnifeTrap_at_zero_shivs_deals_zero_damage()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance? trap) = StartCombatWithXCard(
                seed,
                KnifeTrap.CanonicalId
            );
            if (trap is null)
                continue;
            // Smoke set has no Shiv generators, so ExhaustedShivCount stays at 0.
            Assert.Equal(0, ctx.State.Trail.ExhaustedShivCount);
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, trap!.InstanceId, enemyId);
            // 0 Shivs × 4 damage = 0; enemy HP unchanged.
            Assert.Equal(hpBefore, ctx.State.GetEnemy(enemyId).CurrentHp);
            return;
        }
        Assert.Fail("Could not find seed with KnifeTrap in opening hand.");
    }

    [Fact]
    public void KnifeTrap_scales_with_ExhaustedShivCount()
    {
        // Manually set ExhaustedShivCount on the state to simulate Shivs in exhaust.
        for (uint seed = 1; seed < 200; seed++)
        {
            (CombatContext ctx, global::Sts2Headless.Domain.Combat.CreatureId enemyId, CardInstance? trap) = StartCombatWithXCard(
                seed,
                KnifeTrap.CanonicalId
            );
            if (trap is null)
                continue;
            ctx.SetState(ctx.State with { Trail = ctx.State.Trail with { ExhaustedShivCount = 3 } });
            int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;
            CombatEngine.PlayerPlayCard(ctx, trap!.InstanceId, enemyId);
            // 3 Shivs × 4 damage = 12 raw; JawWorm has no Vulnerable / etc.
            int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
            Assert.Equal(hpBefore - 12, hpAfter);
            return;
        }
        Assert.Fail("Could not find seed with KnifeTrap in opening hand.");
    }
}
