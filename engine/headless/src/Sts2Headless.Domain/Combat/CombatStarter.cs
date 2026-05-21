using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Determinism;
using DomainExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Combat setup: spawns enemies, builds the initial player state, wires relic
/// and spawn-power hooks, fires startup hook sequence, then opens the first
/// player turn. Owns everything that was <c>CombatEngine.StartCombat</c> and its
/// 7 private helpers.
/// </summary>
internal static class CombatStarter
{
    /// <summary>
    /// Set up the combat and return a fully-wired <see cref="CombatContext"/>
    /// ready for the first player action. Identical semantics to the former
    /// <c>CombatEngine.StartCombat</c>.
    /// </summary>
    internal static CombatContext Start(
        IEncounterModel encounter,
        CombatBootstrap catalogs,
        PlayerSpec player,
        RunRngSet runRng,
        IClock clock,
        int totalFloor = 0
    )
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(clock);

        ImmutableList<Creature> enemies = SpawnEnemies(
            (EncounterModel)encounter,
            catalogs.Monsters,
            catalogs.Powers,
            runRng,
            totalFloor
        );
        Creature playerCreature = BuildPlayerCreature(player.InitialHp, player.MaxHp);
        CardPile drawPile = BuildInitialDrawPile(player.Deck, runRng.Shuffle);

        var initial = new CombatState(
            TurnCounter: 0,
            Phase: CombatPhase.CombatStart,
            Player: playerCreature,
            Enemies: enemies,
            Energy: 0,
            BaseEnergyPerTurn: player.BaseEnergyPerTurn,
            HandDrawSize: player.BaseHandDrawCount,
            DrawPile: drawPile,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: runRng.GetCounter(RunRngType.Shuffle),
            MonsterRngCounter: 0
        );

        // Wave A: build plumbing first so CombatContext is fully wired at ctor.
        // The Rng the plumbing uses is runRng.Shuffle — same bucket the context
        // exposes via ctx.Rng; both wire through the same IRngSource object.
        var hookRegistry = new HookRegistry();
        var actionQueue = new ActionQueue();
        var execCtx = new DomainExecutionContext(clock, runRng.Shuffle, hookRegistry, actionQueue);
        var plumbing = new HookPlumbing(hookRegistry, actionQueue, execCtx);

        var ctx = new CombatContext(
            initial,
            runRng,
            clock,
            catalogs.Cards,
            catalogs.Relics,
            catalogs.Powers,
            catalogs.Monsters,
            catalogs.Encounters,
            plumbing
        );

        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: CombatEngine.PlayerId,
            PrimaryTargetId: null,
            SourceCreatureId: CombatEngine.PlayerId
        );

        WireUpRelics(player.RelicIds, catalogs.Relics, execCtx);
        // Wave-26/Q1.D OnApplied bridge for spawn-time powers: SpawnEnemies adds
        // PowerInstances directly onto Creatures (bypassing ApplyPower) so the
        // standard OnApplied call in CombatContext.ApplyPower never fires for them.
        // We fire it here after the hookRegistry is live but before BeforeCombatStart,
        // matching upstream's AfterAddedToRoom order (powers applied → hooks wired →
        // combat-start hooks fire). No-op for powers without hook subscriptions.
        WireUpSpawnPowerHooks(ctx, catalogs.Powers, hookRegistry);
        FireStartupHooks(ctx, plumbing, dispatch);
        OpenFirstPlayerTurn(ctx, plumbing, dispatch, player.BaseEnergyPerTurn);

        return ctx;
    }

    /// <summary>
    /// Spawn enemies for the encounter, rolling HP from the Niche bucket and
    /// stamping spawn-time powers. Monster list derived from
    /// <c>encounter.GenerateMonsters(runRng.ForEncounter(totalFloor, encounter.Id))</c>
    /// so RNG-driven encounters (SmallSlimes, MediumSlimes) pick variants per-seed.
    /// Ids assigned sequentially starting at <see cref="CombatEngine.FirstEnemyId"/>.
    /// </summary>
    private static ImmutableList<Creature> SpawnEnemies(
        EncounterModel encounter,
        MonsterCatalog monsters,
        PowerCatalog powers,
        RunRngSet runRng,
        int totalFloor
    )
    {
        // B.1-ε Wave 14: derive per-encounter Rng and call GenerateMonstersWithMoves so
        // RNG-driven encounters (SmallSlimes, MediumSlimes) produce the correct
        // seed-specific variant list instead of the static sentinel MonsterIds.
        // EncounterRngKey may differ from Id (e.g. SmallSlimes → "SLIMES_WEAK")
        // to match upstream's slugified type name used in the seed formula.
        // Wave-24/K.q1: use GenerateMonstersWithMoves (additive; wraps GenerateMonsters
        // for legacy encounters) so per-slot initial-move overrides are honoured.
        Rng encounterRng = runRng.ForEncounter(totalFloor, encounter.EncounterRngKey);
        IReadOnlyList<(string MonsterId, string? InitialMoveIdOverride)> spawnList =
            encounter.GenerateMonstersWithMoves(encounterRng);

        var enemies = ImmutableList.CreateBuilder<Creature>();
        uint nextEnemyId = CombatEngine.FirstEnemyId;
        foreach ((string monsterId, string? initialMoveIdOverride) in spawnList)
        {
            var monsterModel = (MonsterModel)monsters.Get(monsterId);
            int hp = monsterModel.RollInitialHp(runRng.Niche);
            // B.1-gamma-T3: stamp spawn-time powers (CurlUp, HardToKill, Plating,
            // SuckPower, etc. — upstream's AfterAddedToRoom PowerCmd.Apply calls).
            // Unknown power ids fail-soft: skipped without aborting spawn.
            ImmutableList<PowerInstance> spawnPowerList = ImmutableList<PowerInstance>.Empty;
            for (int i = 0; i < monsterModel.SpawnPowers.Length; i++)
            {
                MonsterSpawnPower sp = monsterModel.SpawnPowers[i];
                if (!powers.TryGet(sp.PowerId, out _))
                    continue;
                spawnPowerList = spawnPowerList.Add(
                    new PowerInstance(
                        ModelId: sp.PowerId,
                        Stacks: sp.Stacks,
                        SourceCreatureId: nextEnemyId,
                        JustApplied: false
                    )
                );
            }
            enemies.Add(
                new Creature(
                    Id: nextEnemyId,
                    Name: monsterId,
                    CurrentHp: hp,
                    MaxHp: hp,
                    Block: 0,
                    Powers: spawnPowerList,
                    // Stream-B-T3: stamp the initial move-id so multi-state monsters
                    // (Chomper et al.) start their per-creature cursor cleanly.
                    // Q1-ADR-014: honour per-slot override when non-null (NibbitsNormal).
                    // Wave-38/B: pass the full MonsterMove so AppliesPowers+SelfBlockGain are read.
                    Intent: MonsterIntent.FromContentIntent(
                        monsterModel.GetMove(
                            initialMoveIdOverride ?? monsterModel.InitialMoveId
                        ),
                        initialMoveIdOverride ?? monsterModel.InitialMoveId
                    ),
                    IsPlayer: false
                )
            );
            nextEnemyId++;
        }
        return enemies.ToImmutable();
    }

    /// <summary>
    /// Build the player creature record (Silent, default A0 HP).
    /// </summary>
    private static Creature BuildPlayerCreature(int initialHp, int maxHp) =>
        new Creature(
            Id: CombatEngine.PlayerId,
            Name: "Silent",
            CurrentHp: initialHp,
            MaxHp: maxHp,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true
        );

    /// <summary>
    /// Shuffle the deck (routing through the Shuffle bucket) and build the
    /// initial draw pile.
    /// </summary>
    // CA1859: IRngSource has multiple impls (Rng + test fakes); interface is
    // load-bearing for determinism harness substitution. Suppress.
#pragma warning disable CA1859
    private static CardPile BuildInitialDrawPile(
        IReadOnlyList<CardInstance> deck,
        IRngSource shuffleRng
    )
#pragma warning restore CA1859
    {
        var drawList = new List<CardInstance>(deck);
        shuffleRng.Shuffle(drawList);
        return CardPile.OfRange(drawList);
    }

    /// <summary>
    /// Subscribe each relic's hook handlers. Hook handlers consume from the
    /// shared kernel (Shuffle bucket today; RC-3 may revisit per-hook routing
    /// if a relic's behavior needs a dedicated bucket).
    /// </summary>
    private static void WireUpRelics(
        IReadOnlyList<string> relicIds,
        RelicCatalog relics,
        DomainExecutionContext execCtx
    )
    {
        foreach (string relicId in relicIds)
        {
            var relicModel = (RelicModel)relics.Get(relicId);
            relicModel.OnAdded(execCtx);
        }
    }

    /// <summary>
    /// Wave-26/Q1.D: fire <see cref="PowerModel.OnApplied"/> for every spawn-time
    /// power on every initial enemy. <c>SpawnEnemies</c> constructs
    /// <see cref="PowerInstance"/> records directly (bypassing
    /// <see cref="CombatContext.ApplyPower"/>) so the standard OnApplied bridge in
    /// ApplyPower never fires. This method closes that gap by calling OnApplied +
    /// <see cref="ICombatAwarePowerModel.OnAppliedWithContext"/> for each
    /// (creature, power) pair after the hook registry is live.
    ///
    /// <para>
    /// Call site: <see cref="Start"/>, after <see cref="WireUpRelics"/> and
    /// before <c>FireStartupHooks</c>, so spawn-power subscriptions (e.g.,
    /// SurprisePower's AfterDeath hook) are active during BeforeCombatStart.
    /// </para>
    /// </summary>
    private static void WireUpSpawnPowerHooks(
        CombatContext ctx,
        PowerCatalog powers,
        HookRegistry hookRegistry
    )
    {
        foreach (Creature enemy in ctx.State.Enemies)
        {
            foreach (PowerInstance pi in enemy.Powers)
            {
                // Unknown power ids were skipped in SpawnEnemies; only registered ids
                // survive into the Powers list.
                if (!powers.TryGet(pi.ModelId, out IPowerModel? raw))
                    continue;
                var model = (PowerModel)raw!;
                model.OnApplied(enemy.Id, hookRegistry);
                if (model is ICombatAwarePowerModel cam)
                    cam.OnAppliedWithContext(enemy.Id, hookRegistry, ctx);
            }
        }
    }

    /// <summary>
    /// Fire the BeforeCombatStart and ModifyHandDraw hooks. The former is for
    /// Anchor / Vajra-style relics; the latter is for RingOfTheSnake /
    /// BagOfPreparation (+2 each on round 1).
    ///
    /// <para>
    /// <b>Known Trap #2:</b> ModifyHandDraw fires at StartCombat ONLY — not on
    /// turn 2+. Relics like RingOfTheSnake / BagOfPreparation add +2 to
    /// hand-draw on round 1 only. <see cref="TurnRunner.StartPlayerTurn"/>
    /// deliberately does NOT fire this hook.
    /// </para>
    /// </summary>
    private static void FireStartupHooks(
        CombatContext ctx,
        HookPlumbing plumbing,
        EffectDispatcher.DispatchContext dispatch
    )
    {
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.BeforeCombatStart, new HookContext(execCtxObs))
        );
        // RingOfTheSnake / BagOfPreparation add +2 each on round 1.
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.ModifyHandDraw, new HookContext(execCtxObs))
        );
    }

    /// <summary>
    /// Transition into the first player turn: bump turn counter, refill energy,
    /// fire per-turn-start hooks (BloodVial, Akabeko, MercuryHourglass, etc.),
    /// fire AfterRoomEntered (OddlySmoothStone, Vajra, Pantograph, DataDisk),
    /// then draw the initial hand and enter PlayerActing.
    ///
    /// <para>
    /// Hook fire order (byte-identical to original):
    /// AfterSideTurnStart → AfterPlayerTurnStart → AfterPlayerTurnStartLate →
    /// AfterRoomEntered → DrawCards → Phase=PlayerActing.
    /// </para>
    /// </summary>
    private static void OpenFirstPlayerTurn(
        CombatContext ctx,
        HookPlumbing plumbing,
        EffectDispatcher.DispatchContext dispatch,
        int baseEnergyPerTurn
    )
    {
        ctx.SetState(
            ctx.State with
            {
                TurnCounter = 1,
                Phase = CombatPhase.PlayerTurnStart,
                Energy = baseEnergyPerTurn,
            }
        );
        // BloodVial fires AfterPlayerTurnStartLate on turn 1 too. After-side-turn
        // and after-player-turn-start fire too (Akabeko, MercuryHourglass) — these
        // were deferred in Stream-B-T2.
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.AfterSideTurnStart, new HookContext(execCtxObs))
        );
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.AfterPlayerTurnStart, new HookContext(execCtxObs))
        );
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.AfterPlayerTurnStartLate, new HookContext(execCtxObs))
        );
        // AfterRoomEntered fires once on combat-room entry — upstream uses this
        // for OddlySmoothStone / Vajra / Pantograph / DataDisk-style relics.
        HookFireSession.Run(
            plumbing,
            dispatch,
            ctx,
            execCtxObs => plumbing.Hooks.Fire(HookType.AfterRoomEntered, new HookContext(execCtxObs))
        );
        ctx.DrawCards(ctx.State.HandDrawSize);
        ctx.SetState(ctx.State with { Phase = CombatPhase.PlayerActing });
    }
}
