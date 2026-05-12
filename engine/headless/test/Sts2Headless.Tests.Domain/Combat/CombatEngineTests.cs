using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T4 tests for <see cref="CombatEngine"/>. Verifies StartCombat sets up
/// state correctly with relic hooks applied; StartPlayerTurn refills energy
/// and draws; PlayerPlayCard validates and applies effects; EndPlayerTurn +
/// EnemyTurn run the turn cycle; CheckCombatEnd detects victory/defeat.
/// </summary>
public sealed class CombatEngineTests
{
    private static CombatContext BootSilentVsCultists(
        IReadOnlyList<string>? relicIds = null,
        IReadOnlyList<CardInstance>? deck = null,
        uint seed = 42u,
        int initialPlayerHp = 70)
    {
        var cards = SmokeContent.BuildCardCatalog();
        var relics = SmokeContent.BuildRelicCatalog();
        var powers = SmokeContent.BuildPowerCatalog();
        var monsters = SmokeContent.BuildMonsterCatalog();
        var encounters = SmokeContent.BuildEncounterCatalog();
        // B.1-alpha-T2 (RC-3): the engine takes a full RunRngSet. Tests
        // construct it from a per-seed string so the bucket plumbing under
        // test is exercised. The string seed mirrors upstream's
        // CompositionRoot ($"seed-{N}" pattern from B.1-alpha-T1).
        var runRng = new RunRngSet($"seed-{seed}");
        var clock = new LogicalClock();

        // Default deck: 5x Strike + 5x Defend with sequential instance ids.
        deck ??= BuildBasicDeck();
        relicIds ??= new[] { RingOfTheSnake.CanonicalId };

        var bootstrap = new CombatBootstrap(cards, relics, powers, monsters, encounters);
        var playerSpec = new PlayerSpec(
            RelicIds: relicIds,
            Deck: deck,
            InitialHp: initialPlayerHp);
        return CombatEngine.StartCombat(
            (IEncounterModel)encounters.Get(CultistsNormal.CanonicalId),
            bootstrap,
            playerSpec,
            runRng,
            clock);
    }

    private static IReadOnlyList<CardInstance> BuildBasicDeck()
    {
        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++)
        {
            deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        }
        for (int i = 0; i < 5; i++)
        {
            deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        }
        return deck;
    }

    // === StartCombat ======================================================

    [Fact]
    public void StartCombat_Initializes_Phase_To_PlayerActing()
    {
        var ctx = BootSilentVsCultists();
        Assert.Equal(CombatPhase.PlayerActing, ctx.State.Phase);
    }

    [Fact]
    public void StartCombat_TurnCounter_Is_One()
    {
        var ctx = BootSilentVsCultists();
        Assert.Equal(1, ctx.State.TurnCounter);
    }

    [Fact]
    public void StartCombat_Energy_Is_Base()
    {
        var ctx = BootSilentVsCultists();
        Assert.Equal(CombatEngine.BaseEnergyPerTurnSilent, ctx.State.Energy);
    }

    [Fact]
    public void StartCombat_RingOfTheSnake_Adds_Two_To_Hand_Draw_Size()
    {
        var ctx = BootSilentVsCultists(relicIds: new[] { RingOfTheSnake.CanonicalId });
        // base 5 + 2 from RingOfTheSnake = 7
        Assert.Equal(7, ctx.State.HandDrawSize);
        Assert.Equal(7, ctx.State.HandPile.Count);
    }

    [Fact]
    public void StartCombat_Anchor_Gives_Ten_Block()
    {
        var ctx = BootSilentVsCultists(relicIds: new[] { Anchor.CanonicalId });
        Assert.Equal(Anchor.BlockAtStart, ctx.State.Player.Block);
    }

    [Fact]
    public void StartCombat_Vajra_Gives_One_Strength()
    {
        var ctx = BootSilentVsCultists(relicIds: new[] { Vajra.CanonicalId });
        var strength = ctx.State.Player.Powers.SingleOrDefault(p => p.ModelId == PowerIds.Strength);
        Assert.NotNull(strength);
        Assert.Equal(1, strength!.Stacks);
    }

    [Fact]
    public void StartCombat_BloodVial_Heals_Two()
    {
        var ctx = BootSilentVsCultists(
            relicIds: new[] { BloodVial.CanonicalId },
            initialPlayerHp: 65);
        Assert.Equal(67, ctx.State.Player.CurrentHp);
    }

    [Fact]
    public void StartCombat_No_Relic_Has_Default_Hand_Draw_Of_Five()
    {
        var ctx = BootSilentVsCultists(relicIds: Array.Empty<string>());
        Assert.Equal(5, ctx.State.HandDrawSize);
        Assert.Equal(5, ctx.State.HandPile.Count);
    }

    [Fact]
    public void StartCombat_Spawns_Two_Enemies_For_CultistsNormal()
    {
        var ctx = BootSilentVsCultists();
        Assert.Equal(2, ctx.State.Enemies.Count);
        Assert.Equal(CalcifiedCultist.CanonicalId, ctx.State.Enemies[0].Name);
        Assert.Equal(DampCultist.CanonicalId, ctx.State.Enemies[1].Name);
    }

    [Fact]
    public void StartCombat_Enemy_HP_In_Range()
    {
        var ctx = BootSilentVsCultists();
        Creature calc = ctx.State.Enemies[0];
        Creature damp = ctx.State.Enemies[1];

        Assert.InRange(calc.CurrentHp, CalcifiedCultist.MinHp, CalcifiedCultist.MaxHp);
        Assert.InRange(damp.CurrentHp, DampCultist.MinHp, DampCultist.MaxHp);
    }

    [Fact]
    public void StartCombat_Enemy_Initial_Intent_Is_Buff_For_Cultists()
    {
        var ctx = BootSilentVsCultists();
        foreach (Creature enemy in ctx.State.Enemies)
        {
            Assert.NotNull(enemy.Intent);
            Assert.Equal(MonsterIntentKind.Buff, enemy.Intent!.Kind);
        }
    }

    /// <summary>
    /// B.1-alpha-T2 (RC-3): StartCombat must route enemy HP rolls through the
    /// <c>.Niche</c> bucket and the initial deck shuffle through the
    /// <c>.Shuffle</c> bucket — matching upstream
    /// <c>CombatState.CreateCreature</c> (line 133:
    /// <c>creature.SetUniqueMonsterHpValue(creaturesOnSide, RunState.Rng.Niche)</c>)
    /// and <c>CombatManager.SetUpCombat</c> (line 188:
    /// <c>player2.PopulateCombatState(player2.RunState.Rng.Shuffle, state)</c>).
    /// </summary>
    [Fact]
    public void StartCombat_HP_Rolls_Consume_Niche_Bucket()
    {
        var cards = SmokeContent.BuildCardCatalog();
        var relics = SmokeContent.BuildRelicCatalog();
        var powers = SmokeContent.BuildPowerCatalog();
        var monsters = SmokeContent.BuildMonsterCatalog();
        var encounters = SmokeContent.BuildEncounterCatalog();
        var runRng = new RunRngSet("seed-42");
        var clock = new LogicalClock();

        int nicheBefore = runRng.GetCounter(RunRngType.Niche);
        int shuffleBefore = runRng.GetCounter(RunRngType.Shuffle);

        CombatEngine.StartCombat(
            (IEncounterModel)encounters.Get(CultistsNormal.CanonicalId),
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: BuildBasicDeck()),
            runRng,
            clock);

        int nicheAfter = runRng.GetCounter(RunRngType.Niche);
        int shuffleAfter = runRng.GetCounter(RunRngType.Shuffle);

        // 2 cultists -> 2 HP rolls -> Niche advances by at least 2 (each
        // RollInitialHp performs one NextInt call). The exact count is left
        // loose: upstream's SetUniqueMonsterHpValue can fall back to NextInt
        // OR NextItem, and either advances the Niche counter; what we pin is
        // that the Niche bucket DID advance and the Shuffle bucket DID NOT
        // (i.e. they're independent streams).
        Assert.True(nicheAfter - nicheBefore >= 2,
            $"Niche bucket should advance for HP rolls; advanced by {nicheAfter - nicheBefore}.");
        Assert.True(shuffleAfter - shuffleBefore >= 1,
            $"Shuffle bucket should advance for deck shuffle; advanced by {shuffleAfter - shuffleBefore}.");
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): SetUniqueMonsterHpValue. Spawning two same-type
    /// monsters whose HP envelope has >= 2 distinct values must produce
    /// distinct HPs (no two same-type creatures share HpMaxValue when the
    /// range allows uniqueness). ChompersNormal has 2 Chompers with envelope
    /// [60..64] (5 distinct values), so uniqueness is guaranteed.
    /// </summary>
    [Fact]
    public void StartCombat_Spawning_Two_Same_Type_Monsters_Produces_Distinct_HPs()
    {
        var cards = Phase1Content.BuildCardCatalog();
        var relics = Phase1Content.BuildRelicCatalog();
        var powers = Phase1Content.BuildPowerCatalog();
        var monsters = Phase1Content.BuildMonsterCatalog();
        var encounters = Phase1Content.BuildEncounterCatalog();
        var runRng = new RunRngSet("seed-42");
        var clock = new LogicalClock();

        var ctx = CombatEngine.StartCombat(
            (IEncounterModel)encounters.Get(ChompersNormal.CanonicalId),
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: BuildBasicDeck()),
            runRng,
            clock);

        Assert.Equal(2, ctx.State.Enemies.Count);
        Creature c1 = ctx.State.Enemies[0];
        Creature c2 = ctx.State.Enemies[1];
        Assert.Equal(Chomper.CanonicalId, c1.Name);
        Assert.Equal(Chomper.CanonicalId, c2.Name);
        Assert.InRange(c1.MaxHp, Chomper.MinHp, Chomper.MaxHp);
        Assert.InRange(c2.MaxHp, Chomper.MinHp, Chomper.MaxHp);
        Assert.NotEqual(c1.MaxHp, c2.MaxHp);
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): MonsterModel.RollUniqueInitialHp algorithm.
    /// When the HP envelope is fully taken, fall back to a plain rng.NextInt
    /// (matches upstream's <c>if (hpRange.Count == 0)</c> branch).
    /// </summary>
    [Fact]
    public void RollUniqueInitialHp_Falls_Back_To_NextInt_When_Range_Exhausted()
    {
        // BowlbugEgg has envelope [21..22] = 2 distinct values. Pre-take both.
        var egg = new BowlbugEgg();
        var rng = new Rng(0u);
        int[] taken = new[] { 21, 22 };
        int hp = egg.RollUniqueInitialHp(rng, taken);
        // Fallback path: NextInt(21, 23) → must be 21 or 22.
        Assert.InRange(hp, BowlbugEgg.MinHp, BowlbugEgg.MaxHp);
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): determinism — same seed + same taken set must
    /// roll the same HP across processes / platforms. Same-seed two-monster
    /// spawn must yield the same (HP1, HP2) tuple every time.
    /// </summary>
    [Fact]
    public void RollUniqueInitialHp_Is_Deterministic_For_Same_Seed_And_Taken_Set()
    {
        var egg1 = new BowlbugEgg();
        var egg2 = new BowlbugEgg();
        var rng1 = new Rng(99u);
        var rng2 = new Rng(99u);
        Assert.Equal(
            egg1.RollUniqueInitialHp(rng1, new[] { 21 }),
            egg2.RollUniqueInitialHp(rng2, new[] { 21 }));
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): same-type uniqueness is a per-type partition.
    /// A taken set from a DIFFERENT-type creature should NOT exclude HPs.
    /// (CombatEngine.StartCombat enforces this by filtering takenHps by
    /// monsterId / Name; this test pins MonsterModel's contract: the method
    /// treats all entries in the taken set as the SAME type's HPs.)
    /// </summary>
    [Fact]
    public void RollUniqueInitialHp_Skips_Values_In_Taken_Set()
    {
        // Force the egg to skip 22 via taken={22}. Envelope is [21..22], so
        // the only legal pick is 21.
        var egg = new BowlbugEgg();
        int hp = egg.RollUniqueInitialHp(new Rng(0u), new[] { 22 });
        Assert.Equal(21, hp);
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): the algorithm consumes EXACTLY ONE rng tick per
    /// call (matching upstream's <c>rng.NextItem(hashSet)</c> single advance).
    /// Pinning the counter delta protects against accidental over-consumption
    /// when ExceptWith / ToHashSet leak to RNG (they don't — verified here).
    /// </summary>
    [Fact]
    public void RollUniqueInitialHp_Advances_Rng_Counter_By_Exactly_One()
    {
        var egg = new BowlbugEgg();
        var rng = new Rng(123u);
        int before = rng.Counter;
        _ = egg.RollUniqueInitialHp(rng, new[] { 21 });
        Assert.Equal(before + 1, rng.Counter);
    }

    /// <summary>
    /// B.1-beta-T3 (RC-4): HashSet&lt;int&gt; insertion-order determinism for
    /// small sets. Upstream's <c>rng.NextItem(hashSet)</c> relies on the
    /// set's iteration order being stable. .NET 9's HashSet&lt;int&gt; uses
    /// insertion order for sets &lt;= ~16 elements; HP envelopes are tiny
    /// (max 6 distinct values for the widest Phase-1 monster). Pinning the
    /// order across multiple constructions catches a future runtime change.
    /// </summary>
    [Fact]
    public void HashSet_int_insertion_order_is_deterministic_for_small_sets()
    {
        var s1 = new HashSet<int> { 3, 1, 4, 1, 5, 9, 2, 6 };
        var s2 = new HashSet<int> { 3, 1, 4, 1, 5, 9, 2, 6 };
        Assert.Equal(s1.ToArray(), s2.ToArray());
        // Order is the unique-insertion order: 3,1,4,5,9,2,6.
        Assert.Equal(new[] { 3, 1, 4, 5, 9, 2, 6 }, s1.ToArray());
    }

    /// <summary>
    /// B.1-alpha-T2 (RC-3): the two buckets advance independently — consuming
    /// from <c>.Niche</c> must NOT advance <c>.Shuffle</c> (and vice versa).
    /// This is the operational guarantee of <see cref="RunRngSet"/>.
    /// </summary>
    [Fact]
    public void RunRngSet_Niche_And_Shuffle_Buckets_Are_Independent()
    {
        var runRng = new RunRngSet("seed-42");
        int shuffleBefore = runRng.GetCounter(RunRngType.Shuffle);
        int nicheBefore = runRng.GetCounter(RunRngType.Niche);

        // Consume from Niche only.
        _ = runRng.Niche.NextInt(0, 100);

        Assert.Equal(nicheBefore + 1, runRng.GetCounter(RunRngType.Niche));
        Assert.Equal(shuffleBefore, runRng.GetCounter(RunRngType.Shuffle));

        // Consume from Shuffle only.
        _ = runRng.Shuffle.NextInt(0, 100);
        Assert.Equal(nicheBefore + 1, runRng.GetCounter(RunRngType.Niche));
        Assert.Equal(shuffleBefore + 1, runRng.GetCounter(RunRngType.Shuffle));
    }

    /// <summary>
    /// B.1-alpha-T2 (RC-3): the combat context surfaces the full RunRngSet so
    /// content code can pick its own bucket; the convenience <c>Rng</c>
    /// property routes to <c>.Shuffle</c> (the most common in-combat default —
    /// matches upstream's deck-reshuffle path in
    /// <c>CardPileCmd.Shuffle</c> line 795:
    /// <c>list.StableShuffle(player.RunState.Rng.Shuffle)</c>).
    /// </summary>
    [Fact]
    public void CombatContext_Exposes_RunRng_And_Routes_Default_To_Shuffle_Bucket()
    {
        var ctx = BootSilentVsCultists();
        Assert.NotNull(ctx.RunRng);
        // The convenience Rng port routes to the Shuffle bucket (upstream's
        // deck-reshuffle default).
        Assert.Same(ctx.RunRng.Shuffle, ctx.Rng);
    }

    [Fact]
    public void StartCombat_Same_Seed_Same_State()
    {
        var a = BootSilentVsCultists(seed: 123u);
        var b = BootSilentVsCultists(seed: 123u);
        // Same draw pile order, same enemy HP rolls, same hand contents.
        // Note: record Equals walks ImmutableList by reference, so direct
        // record equality fails even when contents match. We compare the
        // observable state fields explicitly (this is the smoke-test pattern
        // that S6-T7 generalises to a canonical-hash comparison).
        Assert.Equal(a.State.TurnCounter, b.State.TurnCounter);
        Assert.Equal(a.State.Player.CurrentHp, b.State.Player.CurrentHp);
        Assert.Equal(a.State.Player.Block, b.State.Player.Block);
        Assert.Equal(a.State.Enemies.Count, b.State.Enemies.Count);
        for (int i = 0; i < a.State.Enemies.Count; i++)
        {
            Assert.Equal(a.State.Enemies[i].CurrentHp, b.State.Enemies[i].CurrentHp);
        }
        Assert.Equal(
            a.State.HandPile.Cards.Select(c => c.InstanceId),
            b.State.HandPile.Cards.Select(c => c.InstanceId));
    }

    [Fact]
    public void StartCombat_Different_Seeds_Different_Enemy_Hp_Or_Hand_Order()
    {
        var a = BootSilentVsCultists(seed: 1u);
        var b = BootSilentVsCultists(seed: 2u);
        // Different seeds yield different draws or different enemy HP rolls.
        bool enemyHpDiffers =
            a.State.Enemies[0].CurrentHp != b.State.Enemies[0].CurrentHp ||
            a.State.Enemies[1].CurrentHp != b.State.Enemies[1].CurrentHp;
        bool handOrderDiffers = !a.State.HandPile.Cards
            .Select(c => c.InstanceId)
            .SequenceEqual(b.State.HandPile.Cards.Select(c => c.InstanceId));
        Assert.True(enemyHpDiffers || handOrderDiffers,
            "Two different seeds produced identical observable state; that's suspicious.");
    }

    // === PlayerPlayCard ===================================================

    [Fact]
    public void PlayerPlayCard_Strike_Deals_Damage_To_Enemy()
    {
        var ctx = BootSilentVsCultists();
        // Find a Strike in hand.
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        uint enemyId = ctx.State.Enemies[0].Id;
        int hpBefore = ctx.State.Enemies[0].CurrentHp;

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, enemyId);

        int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
        Assert.Equal(hpBefore - 6, hpAfter); // base strike = 6 damage
    }

    [Fact]
    public void PlayerPlayCard_Defend_Gives_Block_To_Player()
    {
        var ctx = BootSilentVsCultists();
        CardInstance defend = ctx.State.HandPile.Cards.First(c => c.ModelId == DefendSilent.CanonicalId);
        int blockBefore = ctx.State.Player.Block;

        CombatEngine.PlayerPlayCard(ctx, defend.InstanceId, targetEnemyId: null);

        Assert.Equal(blockBefore + 5, ctx.State.Player.Block);
    }

    [Fact]
    public void PlayerPlayCard_Consumes_Energy()
    {
        var ctx = BootSilentVsCultists();
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        int energyBefore = ctx.State.Energy;

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, ctx.State.Enemies[0].Id);

        Assert.Equal(energyBefore - 1, ctx.State.Energy);
    }

    [Fact]
    public void PlayerPlayCard_Moves_Card_To_Discard()
    {
        var ctx = BootSilentVsCultists();
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        int handBefore = ctx.State.HandPile.Count;

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, ctx.State.Enemies[0].Id);

        Assert.Equal(handBefore - 1, ctx.State.HandPile.Count);
        Assert.Contains(ctx.State.DiscardPile.Cards, c => c.InstanceId == strike.InstanceId);
        Assert.DoesNotContain(ctx.State.HandPile.Cards, c => c.InstanceId == strike.InstanceId);
    }

    [Fact]
    public void PlayerPlayCard_Throws_If_Card_Not_In_Hand()
    {
        var ctx = BootSilentVsCultists();
        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.PlayerPlayCard(ctx, cardInstanceId: 9999u, targetEnemyId: 1u));
    }

    [Fact]
    public void PlayerPlayCard_Throws_If_Insufficient_Energy()
    {
        var ctx = BootSilentVsCultists();
        // Set energy to zero.
        ctx.SetState(ctx.State with { Energy = 0 });
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, ctx.State.Enemies[0].Id));
    }

    [Fact]
    public void PlayerPlayCard_Strike_With_Vajra_Strength_Deals_Plus_One()
    {
        var ctx = BootSilentVsCultists(relicIds: new[] { Vajra.CanonicalId });
        CardInstance strike = ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);
        uint enemyId = ctx.State.Enemies[0].Id;
        int hpBefore = ctx.State.GetEnemy(enemyId).CurrentHp;

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, enemyId);

        int hpAfter = ctx.State.GetEnemy(enemyId).CurrentHp;
        Assert.Equal(hpBefore - 7, hpAfter); // 6 base + 1 from Strength
    }

    // === EndPlayerTurn ====================================================

    [Fact]
    public void EndPlayerTurn_Discards_Hand()
    {
        var ctx = BootSilentVsCultists();
        int handBefore = ctx.State.HandPile.Count;
        CombatEngine.EndPlayerTurn(ctx);

        Assert.Equal(0, ctx.State.HandPile.Count);
        Assert.Equal(handBefore, ctx.State.DiscardPile.Count);
    }

    [Fact]
    public void EndPlayerTurn_Transitions_To_EnemyTurnStart()
    {
        var ctx = BootSilentVsCultists();
        CombatEngine.EndPlayerTurn(ctx);
        Assert.Equal(CombatPhase.EnemyTurnStart, ctx.State.Phase);
    }

    // === EnemyTurn ========================================================

    [Fact]
    public void EnemyTurn_Resolves_Incantation_Applying_Ritual_To_Calcified()
    {
        var ctx = BootSilentVsCultists();
        // First turn: cultists' intent is Buff (INCANTATION).
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        var calc = ctx.State.Enemies[0];
        var ritual = calc.Powers.SingleOrDefault(p => p.ModelId == PowerIds.Ritual);
        Assert.NotNull(ritual);
        Assert.Equal(CalcifiedCultist.IncantationRitualStacks, ritual!.Stacks);
    }

    [Fact]
    public void EnemyTurn_Resolves_Incantation_Applying_Ritual_To_Damp()
    {
        var ctx = BootSilentVsCultists();
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        var damp = ctx.State.Enemies[1];
        var ritual = damp.Powers.SingleOrDefault(p => p.ModelId == PowerIds.Ritual);
        Assert.NotNull(ritual);
        Assert.Equal(DampCultist.IncantationRitualStacks, ritual!.Stacks);
    }

    [Fact]
    public void EnemyTurn_Buff_Intent_Does_Not_Damage_Player()
    {
        var ctx = BootSilentVsCultists();
        int hpBefore = ctx.State.Player.CurrentHp;

        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        Assert.Equal(hpBefore, ctx.State.Player.CurrentHp);
    }

    [Fact]
    public void EnemyTurn_Advances_Intent_To_Attack_After_Buff()
    {
        var ctx = BootSilentVsCultists();
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        // After INCANTATION → next intent is DARK_STRIKE (Attack).
        foreach (Creature enemy in ctx.State.Enemies)
        {
            Assert.Equal(MonsterIntentKind.Attack, enemy.Intent!.Kind);
        }
    }

    // === StartPlayerTurn ==================================================

    [Fact]
    public void StartPlayerTurn_Increments_Counter_And_Refills_Energy()
    {
        var ctx = BootSilentVsCultists();
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);
        // Now in EnemyTurnEnd; StartPlayerTurn moves us to turn 2.
        CombatEngine.StartPlayerTurn(ctx);

        Assert.Equal(2, ctx.State.TurnCounter);
        Assert.Equal(CombatPhase.PlayerActing, ctx.State.Phase);
        Assert.Equal(CombatEngine.BaseEnergyPerTurnSilent, ctx.State.Energy);
    }

    [Fact]
    public void StartPlayerTurn_Resets_Player_Block()
    {
        var ctx = BootSilentVsCultists();
        // Give the player some block from a Defend.
        ctx.GainBlock(CombatEngine.PlayerId, 5);
        Assert.Equal(5, ctx.State.Player.Block);

        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);
        CombatEngine.StartPlayerTurn(ctx);

        Assert.Equal(0, ctx.State.Player.Block);
    }

    // === CheckCombatEnd ===================================================

    [Fact]
    public void CheckCombatEnd_Detects_Victory_When_All_Enemies_Dead()
    {
        var ctx = BootSilentVsCultists();
        // Kill both enemies.
        foreach (Creature enemy in ctx.State.Enemies)
        {
            ctx.SetState(ctx.State.WithEnemy(enemy with { CurrentHp = 0 }));
        }
        CombatEngine.CheckCombatEnd(ctx);

        Assert.True(ctx.State.IsCombatOver);
        Assert.True(ctx.State.PlayerWon);
        Assert.False(ctx.State.PlayerLost);
    }

    [Fact]
    public void CheckCombatEnd_Detects_Defeat_When_Player_Dead()
    {
        var ctx = BootSilentVsCultists();
        ctx.SetState(ctx.State.WithPlayer(ctx.State.Player with { CurrentHp = 0 }));
        CombatEngine.CheckCombatEnd(ctx);

        Assert.True(ctx.State.IsCombatOver);
        Assert.True(ctx.State.PlayerLost);
        Assert.False(ctx.State.PlayerWon);
    }

    [Fact]
    public void CheckCombatEnd_NoOp_When_Both_Alive()
    {
        var ctx = BootSilentVsCultists();
        CombatEngine.CheckCombatEnd(ctx);
        Assert.False(ctx.State.IsCombatOver);
    }
}
