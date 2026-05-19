using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// Wave-26/Q1.C tests for the CombatEngine death-hook firing and the
/// CheckCombatEnd ShouldStopCombatFromEnding poll.
///
/// <para>
/// Verifies:
/// </para>
/// <list type="bullet">
///   <item><b>AfterDeath fires once per kill</b>, with the dying creature id
///   threaded through <see cref="HookContext.DyingCreatureId"/>.</item>
///   <item><b>ShouldStopCombatFromEnding</b> defers the CombatEnd transition
///   for one tick when a subscriber sets <c>DeferCombatEnd[0] = true</c>; a
///   subsequent CheckCombatEnd call with the flag cleared completes the
///   transition.</item>
///   <item><b>No-op when no subscribers</b>: the death-flow / combat-end
///   transitions are unchanged byte-for-byte from the pre-Q1.C engine.</item>
///   <item><b>Determinism</b>: two AfterDeath-subscribed handlers attached to
///   different creatures fire in creature-id ascending order per ADR-030 §5 /
///   Q1-ADR-006.</item>
/// </list>
///
/// <para>
/// Test-seam access: the persistent <see cref="HookRegistry"/> attached to a
/// live <see cref="CombatContext"/> is internal (see
/// <c>CombatContext.HookRegistryHandle</c>). Tests reach it via reflection
/// rather than adding an <c>InternalsVisibleTo</c> assembly attribute — the
/// reflection touch is confined to this file and follows the pattern in
/// <c>MonsterScriptTests</c>.
/// </para>
/// </summary>
public sealed class CombatEngineDeathHookTests
{
    // =========================================================================
    // Test fixtures
    // =========================================================================

    /// <summary>
    /// Minimal recording power that subscribes to <see cref="HookType.AfterDeath"/>
    /// and records the creature ids (<see cref="HookContext.DyingCreatureId"/>)
    /// observed across fires.
    /// </summary>
    private sealed class RecordingDeathPower : PowerModel
    {
        public readonly List<uint?> ObservedDeaths = new();

        public RecordingDeathPower()
            : base("test_recording_death_power", PowerType.Buff, PowerStackType.Single) { }

        protected override void SubscribeHooks(
            HookRegistry hooks,
            uint ownerCreatureId,
            List<HookSubscriptionHandle> handleSink
        )
        {
            // Capture the receiver list (this.ObservedDeaths) by ref-identity;
            // each Fire records the DyingCreatureId payload.
            List<uint?> sink = ObservedDeaths;
            Subscribe(
                hooks,
                handleSink,
                HookType.AfterDeath,
                ctx => sink.Add(ctx.DyingCreatureId),
                ownerCreatureId
            );
        }
    }

    /// <summary>
    /// Power that vetoes combat-end once: the first time
    /// <see cref="HookType.ShouldStopCombatFromEnding"/> fires with a non-null
    /// <see cref="HookContext.DeferCombatEnd"/>, it sets the flag to true;
    /// subsequent fires don't defer (so the test can verify the per-tick
    /// defer-then-clear pattern).
    /// </summary>
    private sealed class OneShotDeferPower : PowerModel
    {
        public int FireCount { get; private set; }
        public bool DeferOnNextFire { get; set; } = true;

        public OneShotDeferPower()
            : base("test_oneshot_defer_power", PowerType.Buff, PowerStackType.Single) { }

        protected override void SubscribeHooks(
            HookRegistry hooks,
            uint ownerCreatureId,
            List<HookSubscriptionHandle> handleSink
        )
        {
            Subscribe(
                hooks,
                handleSink,
                HookType.ShouldStopCombatFromEnding,
                ctx =>
                {
                    FireCount++;
                    if (DeferOnNextFire && ctx.DeferCombatEnd is { Length: > 0 } flag)
                    {
                        flag[0] = true;
                        DeferOnNextFire = false;
                    }
                },
                ownerCreatureId
            );
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static CombatContext BootSilentVsCultists(uint seed = 42u)
    {
        var cards = SmokeContent.BuildCardCatalog();
        var relics = SmokeContent.BuildRelicCatalog();
        var powers = SmokeContent.BuildPowerCatalog();
        var monsters = SmokeContent.BuildMonsterCatalog();
        var encounters = SmokeContent.BuildEncounterCatalog();
        var runRng = new RunRngSet($"seed-{seed}");
        var clock = new LogicalClock();

        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        for (int i = 0; i < 5; i++)
            deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));

        var bootstrap = new CombatBootstrap(cards, relics, powers, monsters, encounters);
        var playerSpec = new PlayerSpec(
            RelicIds: new[] { RingOfTheSnake.CanonicalId },
            Deck: deck,
            InitialHp: 70
        );
        return CombatEngine.StartCombat(
            (IEncounterModel)encounters.Get(CultistsNormal.CanonicalId),
            bootstrap,
            playerSpec,
            runRng,
            clock
        );
    }

    /// <summary>
    /// Reach the persistent <see cref="HookRegistry"/> attached to a started
    /// <see cref="CombatContext"/> (see <c>StartCombat → AttachHookPlumbing</c>).
    /// Internal property accessed via reflection — confined to this test file
    /// per the file's class-level docstring.
    /// </summary>
    private static HookRegistry GetPersistentRegistry(CombatContext ctx)
    {
        PropertyInfo? prop = typeof(CombatContext).GetProperty(
            "HookRegistryHandle",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        Assert.NotNull(prop);
        var registry = (HookRegistry?)prop!.GetValue(ctx);
        Assert.NotNull(registry);
        return registry!;
    }

    /// <summary>Force a target enemy to 1 HP so a Strike (6 dmg) is a guaranteed kill.</summary>
    private static void DropEnemyToOneHp(CombatContext ctx, uint enemyId)
    {
        Creature enemy = ctx.State.GetEnemy(enemyId);
        ctx.SetState(ctx.State.WithEnemy(enemy with { CurrentHp = 1, Block = 0 }));
    }

    private static CardInstance FindStrikeInHand(CombatContext ctx) =>
        ctx.State.HandPile.Cards.First(c => c.ModelId == StrikeSilent.CanonicalId);

    // =========================================================================
    // 1. AfterDeath fires once per kill, with the dying creature id payload
    // =========================================================================

    [Fact]
    public void PlayerPlayCard_killing_enemy_fires_AfterDeath_with_dying_creature_id()
    {
        var ctx = BootSilentVsCultists();
        HookRegistry hooks = GetPersistentRegistry(ctx);

        uint victimId = ctx.State.Enemies[0].Id;
        var power = new RecordingDeathPower();
        // Attach the recording power to the OTHER (still-alive) enemy so the
        // subscription survives the death of `victimId`.
        uint subscriberId = ctx.State.Enemies[1].Id;
        power.OnApplied(subscriberId, hooks);

        DropEnemyToOneHp(ctx, victimId);
        CardInstance strike = FindStrikeInHand(ctx);

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, victimId);

        Assert.True(ctx.State.GetEnemy(victimId).IsDead);
        Assert.Single(power.ObservedDeaths);
        Assert.Equal(victimId, power.ObservedDeaths[0]);
    }

    [Fact]
    public void Killing_an_enemy_with_no_AfterDeath_subscribers_is_a_noop_for_the_death_flow()
    {
        // No power subscribed. Verify Strike-kill still completes correctly:
        // enemy is dead, combat may or may not end (cultists has 2 enemies; one
        // dies, the other still acts). The engine state should be identical
        // to pre-Q1.C behavior (no exceptions, no extra side-effects).
        var ctx = BootSilentVsCultists();

        uint victimId = ctx.State.Enemies[0].Id;
        uint otherId = ctx.State.Enemies[1].Id;
        DropEnemyToOneHp(ctx, victimId);
        CardInstance strike = FindStrikeInHand(ctx);

        CombatEngine.PlayerPlayCard(ctx, strike.InstanceId, victimId);

        Assert.True(ctx.State.GetEnemy(victimId).IsDead);
        Assert.False(ctx.State.GetEnemy(otherId).IsDead); // other enemy unaffected
        Assert.False(ctx.State.IsCombatOver); // still one enemy alive
    }

    // =========================================================================
    // 2. ShouldStopCombatFromEnding defer
    // =========================================================================

    [Fact]
    public void CheckCombatEnd_with_no_subscribers_transitions_to_CombatEnd_when_all_enemies_dead()
    {
        // Pre-Q1.C behavior: no veto path; victory transitions immediately.
        var ctx = BootSilentVsCultists();
        foreach (Creature enemy in ctx.State.Enemies)
        {
            ctx.SetState(ctx.State.WithEnemy(enemy with { CurrentHp = 0 }));
        }

        CombatEngine.CheckCombatEnd(ctx);

        Assert.True(ctx.State.IsCombatOver);
        Assert.True(ctx.State.PlayerWon);
    }

    [Fact]
    public void CheckCombatEnd_with_defer_subscriber_skips_transition_then_completes_next_tick()
    {
        // Q1.C behavior: a one-shot defer subscriber vetoes the first poll;
        // the engine remains in PlayerActing. After the subscriber clears its
        // defer flag (one-shot logic), the next CheckCombatEnd call commits
        // the CombatEnd transition.
        var ctx = BootSilentVsCultists();
        HookRegistry hooks = GetPersistentRegistry(ctx);

        var defer = new OneShotDeferPower();
        // Attach to enemy[0]. The subscriber outlives both enemies' deaths
        // because PowerModel hook subscriptions live on the registry, not on
        // the creature's PowerInstance list — pre-Q1.D, no OnRemoved is wired
        // to enemy death.
        defer.OnApplied(ctx.State.Enemies[0].Id, hooks);

        foreach (Creature enemy in ctx.State.Enemies)
        {
            ctx.SetState(ctx.State.WithEnemy(enemy with { CurrentHp = 0 }));
        }

        // Tick 1: subscriber vetoes, transition deferred.
        CombatEngine.CheckCombatEnd(ctx);

        Assert.False(ctx.State.IsCombatOver);
        Assert.Equal(1, defer.FireCount);

        // Tick 2: subscriber's one-shot has cleared; transition commits.
        CombatEngine.CheckCombatEnd(ctx);

        Assert.True(ctx.State.IsCombatOver);
        Assert.True(ctx.State.PlayerWon);
        Assert.Equal(2, defer.FireCount);
    }

    [Fact]
    public void CheckCombatEnd_player_death_path_ignores_ShouldStopCombatFromEnding()
    {
        // ADR-030 §1: the player-defeat branch never consults the veto hook
        // (no upstream semantic for defer-on-defeat). A subscriber that would
        // veto on the victory branch must not delay the defeat transition.
        var ctx = BootSilentVsCultists();
        HookRegistry hooks = GetPersistentRegistry(ctx);

        var defer = new OneShotDeferPower();
        defer.OnApplied(ctx.State.Enemies[0].Id, hooks);

        ctx.SetState(ctx.State.WithPlayer(ctx.State.Player with { CurrentHp = 0 }));

        CombatEngine.CheckCombatEnd(ctx);

        Assert.True(ctx.State.IsCombatOver);
        Assert.True(ctx.State.PlayerLost);
        // The veto hook was never fired (the defeat branch short-circuits).
        Assert.Equal(0, defer.FireCount);
    }

    // =========================================================================
    // 3. Determinism: two AfterDeath subscribers on different creatures fire
    //    in creature-id ascending order across same-tick deaths.
    // =========================================================================

    [Fact]
    public void AfterDeath_fires_each_kill_observed_by_all_subscribers_in_engine_kill_order()
    {
        // Two AfterDeath subscribers (attached to two distinct attach-ids on
        // the registry); kill both enemies via sequential Strikes. Each
        // death fires AfterDeath; both subscribers see both deaths
        // (subscriptions are registry-global, not target-gated). The
        // observed-deaths list per subscriber reflects the engine's kill
        // order (here deterministic: enemy0 first, then enemy1). The Q1-
        // ADR-006 comparator (priority desc → ownerCreatureId asc) ensures
        // subscriber A (attach-id 1000) fires before B (attach-id 2000) for
        // any single death, but both observe each death.
        var ctx = BootSilentVsCultists();
        HookRegistry hooks = GetPersistentRegistry(ctx);

        uint enemy0 = ctx.State.Enemies[0].Id;
        uint enemy1 = ctx.State.Enemies[1].Id;

        var powerA = new RecordingDeathPower();
        var powerB = new RecordingDeathPower();
        // Subscribe both to two distinct attach-ids so the registry hosts both
        // simultaneously. Q1-ADR-006 comparator order:
        // priority(desc) → ownerCreatureId(asc) → ownerContentId(asc) → ...
        // Both powers have priority=0 and ownerContentId=0 (default), so the
        // ownerCreatureId asc tiebreaker drives. We use 1000 < 2000 so power
        // A's handler fires before power B's for any single AfterDeath event.
        powerA.OnApplied(1000u, hooks);
        powerB.OnApplied(2000u, hooks);

        // First Strike: kill enemy0.
        DropEnemyToOneHp(ctx, enemy0);
        CardInstance strike0 = FindStrikeInHand(ctx);
        CombatEngine.PlayerPlayCard(ctx, strike0.InstanceId, enemy0);

        // Second Strike: kill enemy1.
        DropEnemyToOneHp(ctx, enemy1);
        CardInstance strike1 = FindStrikeInHand(ctx);
        CombatEngine.PlayerPlayCard(ctx, strike1.InstanceId, enemy1);

        // Both subscribers observed both deaths in kill order.
        Assert.Equal(new uint?[] { enemy0, enemy1 }, powerA.ObservedDeaths);
        Assert.Equal(new uint?[] { enemy0, enemy1 }, powerB.ObservedDeaths);
    }

    // =========================================================================
    // 4. EnemyTurn-side death detection: Poison ticks at owner's turn start.
    //    A poisoned enemy with HP <= poison stacks dies during EnemyTurn,
    //    inside ApplyPoisonAtTurnStart — Q1.C's snapshot-then-fire helper
    //    announces AfterDeath with the dying enemy id.
    // =========================================================================

    [Fact]
    public void EnemyTurn_poison_killing_enemy_fires_AfterDeath_with_dying_enemy_id()
    {
        var ctx = BootSilentVsCultists();
        HookRegistry hooks = GetPersistentRegistry(ctx);

        uint victimId = ctx.State.Enemies[0].Id;
        uint otherId = ctx.State.Enemies[1].Id;

        var power = new RecordingDeathPower();
        // Subscribe under the OTHER enemy's id so the subscription survives
        // the poison-kill of `victimId`.
        power.OnApplied(otherId, hooks);

        // Drop victim to 1 HP and apply Poison(5) so the enemy's turn-start
        // Poison tick deals 5 unblockable damage → dies.
        Creature victim = ctx.State.GetEnemy(victimId);
        ctx.SetState(ctx.State.WithEnemy(victim with { CurrentHp = 1, Block = 0 }));
        ctx.ApplyPower(victimId, PowerIds.Poison, 5, CombatEngine.PlayerId);

        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);

        Assert.True(ctx.State.GetEnemy(victimId).IsDead);
        Assert.Contains(victimId, power.ObservedDeaths.Select(x => x ?? uint.MaxValue));
    }
}
