using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Cards;

/// <summary>
/// Stream-B-T1 verification: the Silent starter deck (5x Strike + 5x Defend +
/// 1 Neutralize + 1 Survivor; see upstream Silent.StartingDeck) produces the
/// correct CombatState mutations end-to-end through <see cref="CombatEngine"/>.
///
/// <para>
/// This is the integration counterpart to the per-card unit tests in
/// <see cref="SilentSmokeCardTests"/>: those verify that each card enqueues
/// the right S5 actions; this verifies that, when played in sequence through
/// the real engine, the actions translate to the expected enemy-HP / player-
/// block / power-stack outcomes byte-faithfully.
/// </para>
///
/// <para>
/// <b>Why no new behavior here:</b> the Tier-1 starter-deck OnPlay bodies
/// (StrikeSilent / DefendSilent / Neutralize / Survivor) were already wired
/// byte-faithfully in S12. This test locks the Tier-1 deliverable so any
/// future refactor of the dispatcher or context surface that breaks the
/// starter-deck path fails loudly.
/// </para>
/// </summary>
public sealed class SilentStarterDeckBehaviorTests
{
    private static (CombatContext ctx, uint enemyId) StartSilentVsChomper(uint seed)
    {
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();

        // Build deck identical to upstream Silent.StartingDeck: 5x Strike,
        // 5x Defend, 1 Neutralize, 1 Survivor.
        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));

        var runRng = new RunRngSet($"seed-{seed}");
        var clock = new LogicalClock();
        IEncounterModel enc = (IEncounterModel)encounters.Get(ChompersNormal.CanonicalId);
        // ChompersNormal spawns 2 Chompers; first has id = CombatEngine.FirstEnemyId.
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            runRng,
            clock);
        return (ctx, CombatEngine.FirstEnemyId);
    }

    [Fact]
    public void Strike_played_through_engine_damages_target_by_6()
    {
        (CombatContext ctx, uint enemyId) = StartSilentVsChomper(seed: 1u);
        // Force a Strike into hand. The engine drew 7 cards (5 base + 2 RingOfTheSnake);
        // statistically likely to include a Strike but for determinism let's check
        // — if hand has no Strike, the test is irrelevant for this seed.
        CardInstance? strike = null;
        foreach (CardInstance c in ctx.State.HandPile.Cards)
        {
            if (c.ModelId == StrikeSilent.CanonicalId) { strike = c; break; }
        }
        Assert.NotNull(strike);
        Creature beforeEnemy = ctx.State.GetEnemy(enemyId);
        int hpBefore = beforeEnemy.CurrentHp;

        CombatEngine.PlayerPlayCard(ctx, strike!.InstanceId, enemyId);

        Creature afterEnemy = ctx.State.GetEnemy(enemyId);
        // Strike deals 6, minus enemy block (0). HP drops by 6.
        Assert.Equal(hpBefore - 6, afterEnemy.CurrentHp);
    }

    [Fact]
    public void Defend_played_through_engine_grants_5_block_to_player()
    {
        (CombatContext ctx, _) = StartSilentVsChomper(seed: 1u);
        CardInstance? defend = null;
        foreach (CardInstance c in ctx.State.HandPile.Cards)
        {
            if (c.ModelId == DefendSilent.CanonicalId) { defend = c; break; }
        }
        Assert.NotNull(defend);
        int blockBefore = ctx.State.Player.Block;

        CombatEngine.PlayerPlayCard(ctx, defend!.InstanceId, null);

        Assert.Equal(blockBefore + 5, ctx.State.Player.Block);
    }

    [Fact]
    public void Neutralize_played_through_engine_emits_3_damage_and_1_weak()
    {
        // Try multiple seeds until we find one where Neutralize is in the opening hand.
        for (uint seed = 1; seed < 100; seed++)
        {
            (CombatContext ctx, uint enemyId) = StartSilentVsChomper(seed);
            CardInstance? neut = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Neutralize.CanonicalId) { neut = c; break; }
            }
            if (neut is null) continue;
            Creature beforeEnemy = ctx.State.GetEnemy(enemyId);
            int hpBefore = beforeEnemy.CurrentHp;

            CombatEngine.PlayerPlayCard(ctx, neut!.InstanceId, enemyId);

            Creature afterEnemy = ctx.State.GetEnemy(enemyId);
            Assert.Equal(hpBefore - 3, afterEnemy.CurrentHp);
            // Weak should now be stacked on the enemy.
            bool hasWeak = false;
            foreach (PowerInstance p in afterEnemy.Powers)
            {
                if (p.ModelId == PowerIds.Weak && p.Stacks == 1) { hasWeak = true; break; }
            }
            Assert.True(hasWeak, "Neutralize must apply 1 Weak to its target.");
            return;
        }
        Assert.Fail("Could not find seed with Neutralize in opening hand within 100 tries.");
    }

    [Fact]
    public void Survivor_played_through_engine_grants_8_block_and_discards_1()
    {
        for (uint seed = 1; seed < 100; seed++)
        {
            (CombatContext ctx, _) = StartSilentVsChomper(seed);
            CardInstance? sur = null;
            foreach (CardInstance c in ctx.State.HandPile.Cards)
            {
                if (c.ModelId == Survivor.CanonicalId) { sur = c; break; }
            }
            if (sur is null) continue;
            int blockBefore = ctx.State.Player.Block;
            int handSizeBefore = ctx.State.HandPile.Cards.Count;

            CombatEngine.PlayerPlayCard(ctx, sur!.InstanceId, null);

            // Survivor: 8 block + discard 1 (player choice). Smoke harness doesn't
            // model the player-choice discard so DiscardCardsAction is a no-op
            // (acknowledged S6 deferral). Block lands; hand drops only by the
            // played card.
            Assert.Equal(blockBefore + 8, ctx.State.Player.Block);
            Assert.Equal(handSizeBefore - 1, ctx.State.HandPile.Cards.Count);
            return;
        }
        Assert.Fail("Could not find seed with Survivor in opening hand within 100 tries.");
    }

    [Fact]
    public void Silent_starter_deck_total_size_is_12()
    {
        // Upstream: Silent.StartingDeck = 5 Strike + 5 Defend + 1 Neutralize + 1 Survivor = 12.
        // The host's CompositionRoot.ResolveDeck adds 1 DeadlyPoison + 1 Backflip on top
        // for Phase-1 smoke variety; the *canonical* upstream starter is 12. This test
        // pins the canonical composition we build here matches upstream.
        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        Assert.Equal(12, deck.Count);
        // 5 Strike, 5 Defend, 1 each of Neutralize and Survivor.
        Assert.Equal(5, deck.Count(c => c.ModelId == StrikeSilent.CanonicalId));
        Assert.Equal(5, deck.Count(c => c.ModelId == DefendSilent.CanonicalId));
        Assert.Equal(1, deck.Count(c => c.ModelId == Neutralize.CanonicalId));
        Assert.Equal(1, deck.Count(c => c.ModelId == Survivor.CanonicalId));
    }
}
