using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Determinism;
using DomainExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Static turn-lifecycle driver for combat. Stateless — every method takes the
/// inputs it needs and returns the next <see cref="CombatState"/>. The engine
/// coordinates S4 (hook firing) + S5 (content models) + S6's
/// <see cref="ICombatContext"/> to translate content-side enqueued effects
/// into actual state mutations.
///
/// <para>
/// <b>Lifecycle (upstream <c>CombatManager</c> shape, simplified for Phase 1):</b>
/// </para>
/// <list type="number">
///   <item><see cref="StartCombat"/> — pre-combat hooks fire (Anchor, Vajra);
///         deck shuffled; round-1 hand-draw size modified by relic hooks
///         (RingOfTheSnake, BagOfPreparation); initial hand drawn. Sets phase
///         to <see cref="CombatPhase.PlayerActing"/> and turn counter to 1.</item>
///   <item><see cref="StartPlayerTurn"/> — energy refilled; per-turn-start
///         relic hooks (BloodVial) fire; hand drawn to hand-draw size; each
///         enemy's intent resolved for THIS turn. Note: round 1 already had
///         its hand drawn by <see cref="StartCombat"/>; this method only
///         draws on turn &gt;= 2.</item>
///   <item><see cref="PlayerPlayCard"/> — validates legality; consumes
///         energy; runs the card's OnPlay (which enqueues S5 effect
///         actions); drains the queue with translation to ICombatContext;
///         moves the card to discard (smoke set has no Exhaust cards).</item>
///   <item><see cref="EndPlayerTurn"/> — discards hand; ticks down
///         counter-debuff powers on the player; transitions phase to
///         <see cref="CombatPhase.EnemyActing"/>.</item>
///   <item><see cref="EnemyTurn"/> — Poison ticks first (damages owner, then
///         decrement). Each enemy resolves its current intent (Attack damages
///         player; Buff applies the configured power to self). After resolving
///         a Ritual-stack grant if applicable. Then each enemy advances its
///         move-state-machine for the next turn. Transitions back to
///         <see cref="CombatPhase.PlayerTurnStart"/> or
///         <see cref="CombatPhase.CombatEnd"/> via the combat-end check.</item>
///   <item><see cref="CheckCombatEnd"/> — sets phase to
///         <see cref="CombatPhase.CombatEnd"/> if player HP &lt;= 0 or all
///         enemies dead.</item>
/// </list>
///
/// <para>
/// <b>RNG protocol:</b> the engine consumes from <paramref name="rng"/> for
/// deck shuffles and enemy HP rolls. Each consumption advances the kernel's
/// counter, which is mirrored to <see cref="CombatState.PlayerRngCounter"/>
/// after the operation. Monster RNG is held separately (smoke set is
/// deterministic so monsters never consume).
/// </para>
/// </summary>
public static class CombatEngine
{
    /// <summary>Player creature id (always 0 in Phase 1 — single-player).</summary>
    public const uint PlayerId = 0u;

    /// <summary>First enemy id (allocated sequentially in spawn order).</summary>
    public const uint FirstEnemyId = 1u;

    /// <summary>Upstream <c>CombatManager.baseHandDrawCount</c>.</summary>
    public const int BaseHandDrawCount = 5;

    /// <summary>Upstream Silent base energy per turn.</summary>
    public const int BaseEnergyPerTurnSilent = 3;

    /// <summary>Upstream Silent max HP (Ascension 0).</summary>
    public const int BaseMaxHpSilent = 70;

    // === StartCombat ======================================================

    /// <summary>
    /// Set up the combat: spawn enemies (HP rolled from <c>runRng.Niche</c>),
    /// shuffle deck (consuming from <c>runRng.Shuffle</c>), fire
    /// BeforeCombatStart hooks (Anchor → +10 block, Vajra → +1 Strength),
    /// modify hand-draw size for round-1 relics (RingOfTheSnake /
    /// BagOfPreparation → +2 each), draw initial hand.
    ///
    /// <para>
    /// <b>B.1-alpha-T2 (RC-3):</b> takes a full <see cref="RunRngSet"/> and
    /// routes per-bucket — mirrors upstream's per-bucket consumption in
    /// <c>CombatManager.SetUpCombat</c> (line 188:
    /// <c>player2.PopulateCombatState(player2.RunState.Rng.Shuffle, state)</c>)
    /// and <c>CombatState.CreateCreature</c> (line 133:
    /// <c>creature.SetUniqueMonsterHpValue(creaturesOnSide, RunState.Rng.Niche)</c>).
    /// </para>
    /// </summary>
    /// <param name="encounter">The encounter to spawn (e.g., CultistsNormal).</param>
    /// <param name="catalogs">S3/S5 content catalogs (cards, relics, powers, monsters, encounters).</param>
    /// <param name="player">Per-combat player configuration (relics, deck, HP, energy, hand-draw).</param>
    /// <param name="runRng">Determinism-kernel run-scoped RNG fan-out. HP
    ///   rolls consume from <c>.Niche</c>; deck shuffles from
    ///   <c>.Shuffle</c>; in-combat reshuffles via <see cref="CombatContext.Rng"/>
    ///   (which routes back to <c>.Shuffle</c>) — matching upstream's per-
    ///   call-site bucket choice.</param>
    /// <param name="clock">Determinism kernel clock.</param>
    public static CombatContext StartCombat(
        IEncounterModel encounter,
        CombatBootstrap catalogs,
        PlayerSpec player,
        RunRngSet runRng,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(catalogs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(clock);

        ImmutableList<Creature> enemies = SpawnEnemies(encounter, catalogs.Monsters, catalogs.Powers, runRng);
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
            MonsterRngCounter: 0);

        var ctx = new CombatContext(initial, runRng, clock,
            catalogs.Cards, catalogs.Relics, catalogs.Powers, catalogs.Monsters, catalogs.Encounters);

        var hookRegistry = new HookRegistry();
        var actionQueue = new ActionQueue();
        var execCtx = new DomainExecutionContext(clock, ctx.Rng, hookRegistry, actionQueue);
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: PlayerId,
            PrimaryTargetId: null,
            SourceCreatureId: PlayerId);

        WireUpRelics(player.RelicIds, catalogs.Relics, execCtx);
        FireStartupHooks(hookRegistry, actionQueue, execCtx, ctx, dispatch);
        OpenFirstPlayerTurn(ctx, hookRegistry, actionQueue, execCtx, dispatch, player.BaseEnergyPerTurn);

        // Stash the hook plumbing onto the context so subsequent turn/phase
        // boundaries can fire hooks without reconstructing the wiring.
        ctx.AttachHookPlumbing(hookRegistry, actionQueue, execCtx);

        return ctx;
    }

    /// <summary>
    /// Spawn enemies for the encounter, rolling HP from the Niche bucket and
    /// stamping spawn-time powers. Order matches the encounter's MonsterIds;
    /// ids assigned sequentially starting at <see cref="FirstEnemyId"/>.
    /// </summary>
    private static ImmutableList<Creature> SpawnEnemies(
        IEncounterModel encounter,
        MonsterCatalog monsters,
        PowerCatalog powers,
        RunRngSet runRng)
    {
        var enemies = ImmutableList.CreateBuilder<Creature>();
        uint nextEnemyId = FirstEnemyId;
        foreach (string monsterId in encounter.MonsterIds)
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
                if (!powers.TryGet(sp.PowerId, out _)) continue;
                spawnPowerList = spawnPowerList.Add(new PowerInstance(
                    ModelId: sp.PowerId,
                    Stacks: sp.Stacks,
                    SourceCreatureId: nextEnemyId,
                    JustApplied: false));
            }
            enemies.Add(new Creature(
                Id: nextEnemyId,
                Name: monsterId,
                CurrentHp: hp,
                MaxHp: hp,
                Block: 0,
                Powers: spawnPowerList,
                // Stream-B-T3: stamp the initial move-id so multi-state monsters
                // (Chomper et al.) start their per-creature cursor cleanly.
                Intent: MonsterIntent.FromContentIntent(monsterModel.InitialIntent, monsterModel.InitialMoveId),
                IsPlayer: false));
            nextEnemyId++;
        }
        return enemies.ToImmutable();
    }

    /// <summary>
    /// Build the player creature record (Silent, default A0 HP).
    /// </summary>
    private static Creature BuildPlayerCreature(int initialHp, int maxHp) =>
        new Creature(
            Id: PlayerId,
            Name: "Silent",
            CurrentHp: initialHp,
            MaxHp: maxHp,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true);

    /// <summary>
    /// Shuffle the deck (routing through the Shuffle bucket) and build the
    /// initial draw pile.
    /// </summary>
    private static CardPile BuildInitialDrawPile(IReadOnlyList<CardInstance> deck, IRngSource shuffleRng)
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
        DomainExecutionContext execCtx)
    {
        foreach (string relicId in relicIds)
        {
            var relicModel = (RelicModel)relics.Get(relicId);
            relicModel.OnAdded(execCtx);
        }
    }

    /// <summary>
    /// Fire the BeforeCombatStart and ModifyHandDraw hooks. The former is for
    /// Anchor / Vajra-style relics; the latter is for RingOfTheSnake /
    /// BagOfPreparation (+2 each on round 1).
    /// </summary>
    private static void FireStartupHooks(
        HookRegistry hookRegistry,
        ActionQueue actionQueue,
        DomainExecutionContext execCtx,
        ICombatContext combatCtx,
        EffectDispatcher.DispatchContext dispatch)
    {
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.BeforeCombatStart, combatCtx, dispatch);
        // RingOfTheSnake / BagOfPreparation add +2 each on round 1.
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.ModifyHandDraw, combatCtx, dispatch);
    }

    /// <summary>
    /// Transition into the first player turn: bump turn counter, refill energy,
    /// fire per-turn-start hooks (BloodVial, Akabeko, MercuryHourglass, etc.),
    /// fire AfterRoomEntered (OddlySmoothStone, Vajra, Pantograph, DataDisk),
    /// then draw the initial hand and enter PlayerActing.
    /// </summary>
    private static void OpenFirstPlayerTurn(
        CombatContext ctx,
        HookRegistry hookRegistry,
        ActionQueue actionQueue,
        DomainExecutionContext execCtx,
        EffectDispatcher.DispatchContext dispatch,
        int baseEnergyPerTurn)
    {
        ctx.SetState(ctx.State with
        {
            TurnCounter = 1,
            Phase = CombatPhase.PlayerTurnStart,
            Energy = baseEnergyPerTurn,
        });
        // BloodVial fires AfterPlayerTurnStartLate on turn 1 too. After-side-turn
        // and after-player-turn-start fire too (Akabeko, MercuryHourglass) — these
        // were deferred in Stream-B-T2.
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.AfterSideTurnStart, ctx, dispatch);
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.AfterPlayerTurnStart, ctx, dispatch);
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.AfterPlayerTurnStartLate, ctx, dispatch);
        // AfterRoomEntered fires once on combat-room entry — upstream uses this
        // for OddlySmoothStone / Vajra / Pantograph / DataDisk-style relics.
        FireHookAndDrain(hookRegistry, actionQueue, execCtx, HookType.AfterRoomEntered, ctx, dispatch);
        ctx.DrawCards(ctx.State.HandDrawSize);
        ctx.SetState(ctx.State with { Phase = CombatPhase.PlayerActing });
    }

    // === StartPlayerTurn (turn 2+) ========================================

    /// <summary>
    /// Begin a new player turn (turn 2+). Refills energy, fires per-turn-start
    /// relics, draws hand, resolves enemy intents.
    /// </summary>
    public static void StartPlayerTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase == CombatPhase.CombatEnd) return;

        CombatState s = ctx.State;
        ctx.SetState(s with
        {
            TurnCounter = s.TurnCounter + 1,
            Phase = CombatPhase.PlayerTurnStart,
            Energy = s.BaseEnergyPerTurn,
            // Reset block at start of player turn (upstream: not-Barricade).
            Player = s.Player with { Block = 0 },
            // Stream-B-T4: reset per-turn attacks counter (Finisher etc.).
            AttacksPlayedThisTurn = 0,
        });

        // Note: BloodVial's hook is round-1 only in upstream. The smoke
        // ModifyHandDraw / AfterPlayerTurnStartLate hooks were already
        // fired at StartCombat; we deliberately don't fire them again on
        // turn 2+ because the smoke relics don't gate themselves on round.
        // S12 will introduce the round-1 guard.

        // B.1-gamma-T4: per-player-turn hooks (Akabeko Vigor, MercuryHourglass
        // 3-dmg AoE) fire every player turn — Akabeko is upstream-gated to
        // turn-1 by an internal `_alreadyApplied` flag; without that flag Q1's
        // hook subscription would re-apply Vigor each turn. The turn-1 guard
        // is captured inline at the relic's subscribe site (Phase1Relics.cs).
        FirePersistedHook(ctx, HookType.AfterSideTurnStart);
        FirePersistedHook(ctx, HookType.AfterPlayerTurnStart);

        ctx.DrawCards(ctx.State.HandDrawSize);

        // Resolve enemy intents for NEXT enemy turn — upstream's
        // PrepareForNextTurn. Each enemy advances its state machine and
        // assigns a fresh MonsterIntent to its Intent slot.
        ResolveEnemyIntents(ctx);

        ctx.SetState(ctx.State with { Phase = CombatPhase.PlayerActing });
    }

    // === PlayerPlayCard ===================================================

    /// <summary>
    /// Player plays a card from hand. Validates: card must be in hand,
    /// energy must be sufficient. Consumes energy, runs OnPlay, dispatches
    /// effects via <see cref="EffectDispatcher"/>, moves card to discard.
    /// Returns true on success; throws on illegal plays.
    /// </summary>
    /// <param name="ctx">Live combat context.</param>
    /// <param name="cardInstanceId">Id of the card in hand to play.</param>
    /// <param name="targetEnemyId">
    /// Id of the chosen target (null for self-target / no-target cards).
    /// </param>
    public static void PlayerPlayCard(CombatContext ctx, uint cardInstanceId, uint? targetEnemyId)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.PlayerActing)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard called in phase {ctx.State.Phase}; expected PlayerActing.");
        }

        CombatState s = ctx.State;
        // --- Locate card in hand ----------------------------------------
        CardInstance? cardInstance = null;
        for (int i = 0; i < s.HandPile.Cards.Count; i++)
        {
            if (s.HandPile.Cards[i].InstanceId == cardInstanceId)
            {
                cardInstance = s.HandPile.Cards[i];
                break;
            }
        }
        if (cardInstance is null)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard: card instance id={cardInstanceId} not in hand " +
                $"(hand has: {string.Join(",", s.HandPile.Cards.Select(c => c.InstanceId))}).");
        }

        // --- Resolve model + cost ----------------------------------------
        var cardModel = (CardModel)ctx.Cards.Get(cardInstance.ModelId);
        // B.1-gamma-T5: X-cost cards consume ALL the player's current energy.
        // Upstream's CardModel.ResolveEnergyXValue() reads exactly this value.
        int cost = cardModel.IsXCost
            ? s.Energy
            : (cardInstance.CostOverride ?? cardModel.Cost);
        if (s.Energy < cost)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard: insufficient energy (have {s.Energy}, need {cost} for {cardModel.Id}).");
        }

        // --- Validate target shape ---------------------------------------
        bool needsEnemy = cardModel.Target == TargetType.AnyEnemy ||
                          cardModel.Target == TargetType.AllEnemies ||
                          cardModel.Target == TargetType.RandomEnemy;
        if (needsEnemy && targetEnemyId is null)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard: card {cardModel.Id} requires an enemy target.");
        }
        if (needsEnemy && targetEnemyId.HasValue)
        {
            // Throws if not found.
            _ = s.GetEnemy(targetEnemyId.Value);
        }

        // --- Consume energy ----------------------------------------------
        // B.1-gamma-T5: also snapshot the spent energy for X-cost cards so the
        // card's OnPlay body can read it via CombatContext.AllRemainingEnergy()
        // / CombatState.LastSpentEnergy.
        ctx.SetState(ctx.State with
        {
            Energy = ctx.State.Energy - cost,
            LastSpentEnergy = cardModel.IsXCost ? cost : ctx.State.LastSpentEnergy,
        });

        // --- Move card from hand to discard ------------------------------
        // Done BEFORE OnPlay so cards that draw can refill from a clean hand.
        ctx.SetState(ctx.State with
        {
            HandPile = ctx.State.HandPile.Remove(cardInstanceId),
            DiscardPile = ctx.State.DiscardPile.Add(cardInstance),
        });

        // --- Invoke OnPlay; dispatch enqueued effects --------------------
        var hookRegistry = new HookRegistry();
        var actionQueue = new ActionQueue();
        var execCtx = new DomainExecutionContext(ctx.Clock, ctx.Rng, hookRegistry, actionQueue);
        string? targetString = targetEnemyId?.ToString();
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: PlayerId,
            PrimaryTargetId: targetEnemyId,
            SourceCreatureId: PlayerId);

        using (EffectObserver.Attach(out List<IAction> log))
        {
            cardModel.OnPlay(execCtx, targetString);
            actionQueue.Drain(execCtx);
            foreach (IAction action in log)
            {
                EffectDispatcher.Apply(action, ctx, dispatch);
            }
        }
        // Stream-B-T4: bump the per-turn attack counter AFTER OnPlay drains, so
        // calc-damage formulas (Finisher's "× attacks played THIS turn") see
        // the prior count — matching upstream's CardPlaysFinished semantics
        // (the entry for THIS card is recorded only after its play completes).
        int newAttacks = ctx.State.AttacksPlayedThisTurn
            + (cardModel.Type == CardType.Attack ? 1 : 0);
        ctx.SetState(ctx.State with
        {
            PlayerRngCounter = ctx.Rng.Counter,
            AttacksPlayedThisTurn = newAttacks,
        });

        // Check end on every mutation point in case the card killed an enemy.
        CheckCombatEnd(ctx);
    }

    // === EndPlayerTurn ====================================================

    /// <summary>
    /// End the player's turn. Discards hand, ticks down counter-debuff
    /// durations on the player (Vulnerable, Weak), transitions to
    /// EnemyActing. Does not include the enemy turn itself.
    /// </summary>
    public static void EndPlayerTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.PlayerActing)
        {
            throw new InvalidOperationException(
                $"EndPlayerTurn called in phase {ctx.State.Phase}; expected PlayerActing.");
        }
        ctx.SetState(ctx.State with { Phase = CombatPhase.PlayerTurnEnd });

        // B.1-gamma-T4: BeforeTurnEnd hook fires before hand discard — Orichalcum
        // checks "did player have block this turn?" before turn-end logic clears
        // anything. Hook subscribers see Player.Block as it stands at end-of-turn
        // (Orichalcum gates on Block == 0 here).
        FirePersistedHook(ctx, HookType.BeforeTurnEnd);

        // Discard hand.
        ctx.DiscardHand();

        // Tick down counter-debuff durations on the player.
        TickPowerDurations(ctx, ctx.State.Player);

        ctx.SetState(ctx.State with { Phase = CombatPhase.EnemyTurnStart });
    }

    // === EnemyTurn ========================================================

    /// <summary>
    /// Run the enemy turn. For each living enemy in spawn order, apply Poison
    /// (damage owner = stacks, then decrement); resolve its intent (Attack
    /// → damage to player; Buff → apply the configured power to self); apply
    /// Ritual-grant-Strength if relevant; advance the move-state-machine.
    /// Transitions phase back to PlayerActing (next player turn) or to
    /// CombatEnd.
    /// </summary>
    public static void EnemyTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.EnemyTurnStart)
        {
            throw new InvalidOperationException(
                $"EnemyTurn called in phase {ctx.State.Phase}; expected EnemyTurnStart.");
        }
        ctx.SetState(ctx.State with { Phase = CombatPhase.EnemyActing });

        // Each enemy acts in spawn order. We snapshot ids to avoid skipping
        // due to mid-loop list mutation; dead enemies do nothing.
        ImmutableArray<uint> enemyIds = ctx.State.Enemies.Select(e => e.Id).ToImmutableArray();
        foreach (uint id in enemyIds)
        {
            Creature? enemy = ctx.State.FindEnemy(id);
            if (enemy is null || enemy.IsDead) continue;

            // --- Reset block (enemies clear block at THEIR turn start) ---
            ctx.SetState(ctx.State.WithEnemy(enemy with { Block = 0 }));

            // --- Poison fires at owner's turn start: damage = stacks, then decrement ---
            ApplyPoisonAtTurnStart(ctx, id);
            CheckCombatEnd(ctx);
            if (ctx.State.IsCombatOver) return;
            // Re-fetch in case Poison killed the enemy.
            Creature? aliveEnemy = ctx.State.FindEnemy(id);
            if (aliveEnemy is null || aliveEnemy.IsDead) continue;

            // --- Resolve current intent ----------------------------------
            var enemyModel = (MonsterModel)ctx.Monsters.Get(aliveEnemy.Name);
            // Stream-B-T3: use the per-creature MoveId (recorded on the
            // creature's Intent during ResolveEnemyIntents) rather than the
            // shared model's InitialMoveId. Falls back to the model's
            // initial id when the creature's intent has no MoveId (legacy
            // single-state monsters that don't ship a per-creature cursor).
            string moveId = !string.IsNullOrEmpty(aliveEnemy.Intent?.MoveId)
                ? aliveEnemy.Intent!.MoveId
                : enemyModel.InitialMoveId;
            ResolveMove(ctx, aliveEnemy.Id, enemyModel, moveId);

            CheckCombatEnd(ctx);
            if (ctx.State.IsCombatOver) return;

            // --- Ritual-on-turn-end: grant Strength ----------------------
            ApplyRitualEndOfTurn(ctx, ctx.State.GetEnemy(id));

            // --- Advance per-creature state machine cursor --------------
            // The follow-up move-id lives on the catalog model; we read it
            // and stamp it onto the creature's intent so the NEXT
            // ResolveEnemyIntents call picks it up. We do NOT advance the
            // shared model — that would corrupt sibling enemies' cursors.
            // B.1-gamma-T2: AdvanceMoveId honors per-move branch resolvers
            // (RNG-branch / HP-threshold / power-gate) when present.
            Creature? postResolveEnemy = ctx.State.FindEnemy(id);
            if (postResolveEnemy is not null && !postResolveEnemy.IsDead)
            {
                MoveBranchContext branchCtx = MakeBranchContext(postResolveEnemy);
                string nextMoveId = enemyModel.AdvanceMoveId(moveId, branchCtx, ctx.RunRng);
                MonsterIntent priorIntent = postResolveEnemy.Intent ?? MonsterIntent.None;
                // We only need to update the MoveId; ResolveEnemyIntents will
                // rebuild Kind / DamagePerHit / HitCount from the new move.
                var advancedIntent = priorIntent with { MoveId = nextMoveId };
                ctx.SetState(ctx.State.WithEnemy(postResolveEnemy with { Intent = advancedIntent }));
            }
        }

        // --- Tick down enemy debuff durations ----------------------------
        foreach (uint id in enemyIds)
        {
            Creature? enemy = ctx.State.FindEnemy(id);
            if (enemy is null || enemy.IsDead) continue;
            TickPowerDurations(ctx, enemy);
            // Reset JustApplied flag now that we've passed end-of-owner-turn.
            ResetJustApplied(ctx, ctx.State.FindEnemy(id)!);
        }

        ctx.SetState(ctx.State with { Phase = CombatPhase.EnemyTurnEnd });

        // Resolve intents for the upcoming player turn.
        ResolveEnemyIntents(ctx);

        // Transition to next player turn (handled by caller via
        // StartPlayerTurn) or to CombatEnd.
        if (ctx.State.IsCombatOver) return;

        // Note: we deliberately leave phase at EnemyTurnEnd; the smoke
        // harness calls StartPlayerTurn next, which bumps the turn counter
        // and transitions to PlayerActing.
    }

    // === CheckCombatEnd ===================================================

    /// <summary>
    /// Set <c>Phase = CombatEnd</c> if combat is over. Idempotent: calling on
    /// an already-ended combat is a no-op.
    /// </summary>
    public static void CheckCombatEnd(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.IsCombatOver) return;

        bool playerDead = ctx.State.Player.IsDead;
        bool allEnemiesDead = ctx.State.Enemies.All(e => e.IsDead);

        if (playerDead || allEnemiesDead)
        {
            ctx.SetState(ctx.State with { Phase = CombatPhase.CombatEnd });
        }
    }

    // === Helpers ==========================================================

    private static void FireHookAndDrain(
        HookRegistry hookRegistry,
        ActionQueue actionQueue,
        DomainExecutionContext execCtx,
        HookType type,
        ICombatContext combatCtx,
        EffectDispatcher.DispatchContext dispatch)
    {
        using (EffectObserver.Attach(out List<IAction> log))
        {
            hookRegistry.Fire(type, new HookContext(execCtx));
            actionQueue.Drain(execCtx);
            foreach (IAction action in log)
            {
                EffectDispatcher.Apply(action, combatCtx, dispatch);
            }
        }
    }

    /// <summary>
    /// B.1-gamma-T4: fire a hook through the context's persisted plumbing
    /// (attached by StartCombat). No-op if plumbing isn't attached (legacy
    /// engine tests that hand-construct CombatContext without StartCombat).
    /// </summary>
    private static void FirePersistedHook(CombatContext ctx, HookType type)
    {
        if (ctx.HookRegistryHandle is null || ctx.ActionQueueHandle is null
            || ctx.ExecutionContextHandle is null)
        {
            return;
        }
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: PlayerId,
            PrimaryTargetId: null,
            SourceCreatureId: PlayerId);
        FireHookAndDrain(
            ctx.HookRegistryHandle, ctx.ActionQueueHandle,
            ctx.ExecutionContextHandle, type, ctx, dispatch);
    }

    /// <summary>
    /// Build a <see cref="MoveBranchContext"/> snapshot from a creature for
    /// the state-machine resolver. Closes over the creature's powers list so
    /// HasPower / GetPowerStacks predicates report the correct values.
    /// </summary>
    private static MoveBranchContext MakeBranchContext(Creature creature)
    {
        // Snapshot the power list so closures stay valid even if the engine
        // mutates state between context creation and resolver invocation.
        var powers = creature.Powers;
        return new MoveBranchContext(
            CurrentHp: creature.CurrentHp,
            MaxHp: creature.MaxHp,
            HasPower: id =>
            {
                for (int i = 0; i < powers.Count; i++)
                {
                    if (powers[i].ModelId == id && powers[i].Stacks > 0) return true;
                }
                return false;
            },
            GetPowerStacks: id =>
            {
                for (int i = 0; i < powers.Count; i++)
                {
                    if (powers[i].ModelId == id) return powers[i].Stacks;
                }
                return 0;
            });
    }

    /// <summary>
    /// Resolve the next intent for each enemy (run state machine forward to
    /// pre-show the next turn's move). Smoke set's Cultists use deterministic
    /// rotations (no RNG branching).
    /// </summary>
    private static void ResolveEnemyIntents(CombatContext ctx)
    {
        for (int i = 0; i < ctx.State.Enemies.Count; i++)
        {
            Creature enemy = ctx.State.Enemies[i];
            if (enemy.IsDead) continue;
            var model = (MonsterModel)ctx.Monsters.Get(enemy.Name);
            // Stream-B-T3: prefer the per-creature MoveId (if any) over the shared
            // model's InitialMoveId. The model is a singleton in the catalog —
            // multiple enemies of the same type need independent rotation state.
            string moveId = !string.IsNullOrEmpty(enemy.Intent?.MoveId)
                ? enemy.Intent!.MoveId
                : model.InitialMoveId;
            Intent contentIntent = model.GetMove(moveId).Intent;
            var monsterIntent = MonsterIntent.FromContentIntent(contentIntent, moveId);
            ctx.SetState(ctx.State.WithEnemy(enemy with { Intent = monsterIntent }));
        }
    }

    /// <summary>
    /// Resolve a single monster move. Per Phase-1 smoke needs:
    /// - DARK_STRIKE / SingleAttack → DealDamage to player.
    /// - INCANTATION (CalcifiedCultist/DampCultist) → apply Ritual to self.
    /// More moves added in S12.
    /// </summary>
    private static void ResolveMove(CombatContext ctx, uint enemyId, MonsterModel model, string moveId)
    {
        MonsterMove move = model.GetMove(moveId);
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: PlayerId,
            PrimaryTargetId: PlayerId,
            SourceCreatureId: enemyId);

        if (move.Intent.Kind == IntentKind.Attack)
        {
            // Attack damages player. HitCount honored for multi-hit attacks
            // (Stream-B-T3: Chomper CLAMP 2x8, Exoskeleton SKITTER 3x1). Damage
            // modifiers (Strength/Weak/Vulnerable) recompute PER HIT, matching
            // upstream's per-hit DamageCmd loop where each hit goes through the
            // modifier pipeline independently.
            int raw = move.Intent.Value;
            int hits = move.Intent.HitCount > 0 ? move.Intent.HitCount : 1;
            for (int i = 0; i < hits; i++)
            {
                // Re-check liveness between hits — the player can die mid-stream.
                if (ctx.State.Player.IsDead) break;
                int modified = DamageModifier.Modify(ctx.State, enemyId, PlayerId, raw);
                ctx.DealDamage(PlayerId, modified, enemyId);
            }
        }
        else if (move.Intent.Kind == IntentKind.Buff)
        {
            // INCANTATION pattern: apply Ritual to self. Stack count comes from
            // the concrete monster type (CalcifiedCultist=2, DampCultist=5).
            int stacks = ExtractIncantationStacks(model, moveId);
            if (stacks > 0)
            {
                ctx.ApplyPower(enemyId, PowerIds.Ritual, stacks, enemyId);
            }
        }
        // Defend / Debuff / Status: no engine-side payload yet. Defend would gain
        // block on the enemy (engine has GainBlock); Debuff would apply a debuff
        // power to player; Status would add a status card to player discard. The
        // intent rotation still updates correctly even when the payload is a
        // no-op, which is what we need for the determinism probe.
    }

    /// <summary>
    /// Returns the Ritual stack count an enemy's INCANTATION-shaped move
    /// grants. Hard-coded for the smoke set; S12 generalises via a move
    /// payload.
    /// </summary>
    private static int ExtractIncantationStacks(MonsterModel model, string moveId)
    {
        if (model is Sts2Headless.Domain.Content.Monsters.CalcifiedCultist
            && moveId == Sts2Headless.Domain.Content.Monsters.CalcifiedCultist.IncantationMoveId)
        {
            return Sts2Headless.Domain.Content.Monsters.CalcifiedCultist.IncantationRitualStacks;
        }
        if (model is Sts2Headless.Domain.Content.Monsters.DampCultist
            && moveId == Sts2Headless.Domain.Content.Monsters.DampCultist.IncantationMoveId)
        {
            return Sts2Headless.Domain.Content.Monsters.DampCultist.IncantationRitualStacks;
        }
        return 0;
    }

    /// <summary>
    /// At end of an enemy's turn, if Ritual is on them and not just-applied
    /// this turn, grant Strength = Ritual stacks.
    /// </summary>
    private static void ApplyRitualEndOfTurn(CombatContext ctx, Creature enemy)
    {
        for (int i = 0; i < enemy.Powers.Count; i++)
        {
            PowerInstance p = enemy.Powers[i];
            if (p.ModelId == PowerIds.Ritual && !p.JustApplied && p.Stacks > 0)
            {
                ctx.ApplyPower(enemy.Id, PowerIds.Strength, p.Stacks, enemy.Id);
            }
        }
    }

    /// <summary>
    /// Reset JustApplied=false on every power for a creature. Called at end
    /// of owner's turn.
    /// </summary>
    private static void ResetJustApplied(CombatContext ctx, Creature creature)
    {
        bool anyChanged = false;
        var updated = creature.Powers;
        for (int i = 0; i < updated.Count; i++)
        {
            if (updated[i].JustApplied)
            {
                updated = updated.SetItem(i, updated[i] with { JustApplied = false });
                anyChanged = true;
            }
        }
        if (anyChanged)
        {
            var newCreature = creature with { Powers = updated };
            ctx.SetState(creature.IsPlayer
                ? ctx.State.WithPlayer(newCreature)
                : ctx.State.WithEnemy(newCreature));
        }
    }

    /// <summary>
    /// At the owner's turn start, if they have Poison: deal Poison-stacks
    /// unblockable damage to the owner, then decrement Poison by 1. Matches
    /// upstream's <c>PoisonPower.AfterTurnStart</c>: damage equals
    /// <c>Amount</c> (current stacks), then <c>Decrement</c>. Phase 1 smoke
    /// runs this for enemies (player can apply Poison via DeadlyPoison).
    /// </summary>
    private static void ApplyPoisonAtTurnStart(CombatContext ctx, uint ownerId)
    {
        Creature? owner = ownerId == PlayerId ? ctx.State.Player : ctx.State.FindEnemy(ownerId);
        if (owner is null || owner.IsDead) return;

        int poisonIndex = -1;
        for (int i = 0; i < owner.Powers.Count; i++)
        {
            if (owner.Powers[i].ModelId == PowerIds.Poison)
            {
                poisonIndex = i;
                break;
            }
        }
        if (poisonIndex < 0) return;

        PowerInstance poison = owner.Powers[poisonIndex];
        if (poison.Stacks <= 0) return;

        // Poison damage IGNORES block (upstream calls it "unblockable").
        // To bypass our DealDamage block-first logic, we directly mutate HP.
        int newHp = Math.Max(0, owner.CurrentHp - poison.Stacks);
        var hpReduced = owner with { CurrentHp = newHp };

        // Decrement stacks; remove if zero.
        int newStacks = poison.Stacks - 1;
        ImmutableList<PowerInstance> newPowers = newStacks > 0
            ? hpReduced.Powers.SetItem(poisonIndex, poison with { Stacks = newStacks })
            : hpReduced.Powers.RemoveAt(poisonIndex);

        var updated = hpReduced with { Powers = newPowers };
        ctx.SetState(updated.IsPlayer
            ? ctx.State.WithPlayer(updated)
            : ctx.State.WithEnemy(updated));
    }

    /// <summary>
    /// Tick down counter-debuff durations (Vulnerable, Weak) by 1 at owner's
    /// turn end. Poison damages the OWNER at start of owner's turn — that
    /// part is handled in <see cref="ApplyPoisonAtTurnStart"/>.
    /// </summary>
    private static void TickPowerDurations(CombatContext ctx, Creature owner)
    {
        var newPowers = owner.Powers;
        bool anyChanged = false;
        for (int i = 0; i < newPowers.Count; i++)
        {
            PowerInstance p = newPowers[i];
            bool shouldTick = p.ModelId == PowerIds.Vulnerable || p.ModelId == PowerIds.Weak;
            if (shouldTick && p.Stacks > 0)
            {
                int newStacks = p.Stacks - 1;
                newPowers = newPowers.SetItem(i, p with { Stacks = newStacks });
                anyChanged = true;
            }
        }
        if (anyChanged)
        {
            // Strip zero-stack powers.
            newPowers = newPowers.RemoveAll(p => p.Stacks <= 0 && p.ModelId != PowerIds.Strength);
            var updated = owner with { Powers = newPowers };
            ctx.SetState(owner.IsPlayer
                ? ctx.State.WithPlayer(updated)
                : ctx.State.WithEnemy(updated));
        }
    }
}
