using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Turn lifecycle driver: player turn start/end, enemy turn, and combat-end
/// check. Stateless — every method takes a live <see cref="CombatContext"/>.
/// Owns everything that was the turn-lifecycle section of <c>CombatEngine</c>
/// plus its 7 private helpers.
/// </summary>
internal static class TurnRunner
{
    /// <summary>
    /// Begin a new player turn (turn 2+). Refills energy, fires per-turn-start
    /// relics, draws hand, resolves enemy intents.
    ///
    /// <para>
    /// <b>Known Trap #2:</b> <see cref="HookType.ModifyHandDraw"/> and
    /// <see cref="HookType.AfterPlayerTurnStartLate"/> are deliberately NOT fired
    /// here — they were fired once at StartCombat for turn-1 only. Smoke relics
    /// do not gate themselves on round; S12 will introduce the round-1 guard.
    /// </para>
    /// </summary>
    internal static void StartPlayerTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase == CombatPhase.CombatEnd)
            return;

        CombatState s = ctx.State;
        ctx.SetState(
            s with
            {
                TurnCounter = s.TurnCounter + 1,
                Phase = CombatPhase.PlayerTurnStart,
                Energy = s.BaseEnergyPerTurn,
                // Reset block at start of player turn (upstream: not-Barricade).
                Player = s.Player with
                {
                    Block = 0,
                },
                // Stream-B-T4: reset per-turn attacks counter (Finisher etc.).
                Trail = s.Trail with { AttacksPlayedThisTurn = 0 },
            }
        );

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

    /// <summary>
    /// End the player's turn. Discards hand, ticks down counter-debuff
    /// durations on the player (Vulnerable, Weak), transitions to
    /// EnemyActing. Does not include the enemy turn itself.
    /// </summary>
    internal static void EndPlayerTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.PlayerActing)
        {
            throw new InvalidOperationException(
                $"EndPlayerTurn called in phase {ctx.State.Phase}; expected PlayerActing."
            );
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

    /// <summary>
    /// Run the enemy turn. For each living enemy in spawn order, apply Poison
    /// (damage owner = stacks, then decrement); resolve its intent (Attack
    /// → damage to player; Buff → apply the configured power to self); apply
    /// Ritual-grant-Strength if relevant; advance the move-state-machine.
    /// Transitions phase back to PlayerActing (next player turn) or to
    /// CombatEnd.
    /// </summary>
    internal static void EnemyTurn(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.EnemyTurnStart)
        {
            throw new InvalidOperationException(
                $"EnemyTurn called in phase {ctx.State.Phase}; expected EnemyTurnStart."
            );
        }
        ctx.SetState(ctx.State with { Phase = CombatPhase.EnemyActing });

        // Each enemy acts in spawn order. We snapshot ids to avoid skipping
        // due to mid-loop list mutation; dead enemies do nothing.
        ImmutableArray<uint> enemyIds = ctx.State.Enemies.Select(e => e.Id).ToImmutableArray();
        foreach (uint id in enemyIds)
        {
            Creature? enemy = ctx.State.FindEnemy(id);
            if (enemy is null || enemy.IsDead)
                continue;

            // --- Reset block (enemies clear block at THEIR turn start) ---
            ctx.SetState(ctx.State.WithEnemy(enemy with { Block = 0 }));

            // --- Poison fires at owner's turn start: damage = stacks, then decrement ---
            // ADR-030 §1: snapshot before Poison so a self-tick-kill announces
            // AfterDeath. (Only the Poison-owner can die here; ascending-id
            // ordering across multiple poisoned enemies is preserved by the
            // outer foreach.)
            ImmutableArray<uint> aliveBeforePoison = DeathBroadcaster.SnapshotAliveIds(ctx);
            ApplyPoisonAtTurnStart(ctx, id);
            DeathBroadcaster.FireAfterDeathForNewDeaths(ctx, aliveBeforePoison);
            CheckCombatEnd(ctx);
            if (ctx.State.IsCombatOver)
                return;
            // Re-fetch in case Poison killed the enemy.
            Creature? aliveEnemy = ctx.State.FindEnemy(id);
            if (aliveEnemy is null || aliveEnemy.IsDead)
                continue;

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
            // ADR-030 §1: snapshot before the move; an Attack intent that
            // kills the player (or — future-friendly — kills the acting
            // enemy itself via reflection / Thorns) announces AfterDeath
            // BEFORE the next enemy acts.
            ImmutableArray<uint> aliveBeforeMove = DeathBroadcaster.SnapshotAliveIds(ctx);
            ResolveMove(ctx, aliveEnemy.Id);
            DeathBroadcaster.FireAfterDeathForNewDeaths(ctx, aliveBeforeMove);

            CheckCombatEnd(ctx);
            if (ctx.State.IsCombatOver)
                return;

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
                var advancedIntent = priorIntent with
                {
                    MoveId = nextMoveId,
                };
                ctx.SetState(
                    ctx.State.WithEnemy(postResolveEnemy with { Intent = advancedIntent })
                );
            }
        }

        // --- Tick down enemy debuff durations ----------------------------
        foreach (uint id in enemyIds)
        {
            Creature? enemy = ctx.State.FindEnemy(id);
            if (enemy is null || enemy.IsDead)
                continue;
            TickPowerDurations(ctx, enemy);
            // Reset JustApplied flag now that we've passed end-of-owner-turn.
            ResetJustApplied(ctx, ctx.State.FindEnemy(id)!);
        }

        ctx.SetState(ctx.State with { Phase = CombatPhase.EnemyTurnEnd });

        // Resolve intents for the upcoming player turn.
        ResolveEnemyIntents(ctx);

        // Transition to next player turn (handled by caller via
        // StartPlayerTurn) or to CombatEnd.
        if (ctx.State.IsCombatOver)
            return;

        // Note: we deliberately leave phase at EnemyTurnEnd; the smoke
        // harness calls StartPlayerTurn next, which bumps the turn counter
        // and transitions to PlayerActing.
    }

    /// <summary>
    /// Set <c>Phase = CombatEnd</c> if combat is over. Idempotent: calling on
    /// an already-ended combat is a no-op.
    ///
    /// <para>
    /// <b>Wave-26 / ADR-030 §1 + §6:</b> when all enemies are dead (the
    /// player-victory branch), poll
    /// <see cref="HookType.ShouldStopCombatFromEnding"/> subscribers via the
    /// HookContext boolean-aggregation convention. Subscribers (e.g.,
    /// SurprisePower from Q1.D) set <c>ctx.DeferCombatEnd[0] = true</c> to
    /// veto the transition for this tick — useful when an
    /// <see cref="HookType.AfterDeath"/> handler has just spawned replacement
    /// enemies and the engine must process them before declaring victory.
    /// The defer is per-tick: a subsequent <see cref="CheckCombatEnd"/> call
    /// re-polls with a fresh flag. The player-defeat branch never defers
    /// (no upstream consumer exists; gameplay symmetry would require a
    /// distinct hook).
    /// </para>
    /// </summary>
    internal static void CheckCombatEnd(CombatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.IsCombatOver)
            return;

        bool playerDead = ctx.State.Player.IsDead;
        bool allEnemiesDead = ctx.State.Enemies.All(e => e.IsDead);

        if (!playerDead && !allEnemiesDead)
            return;

        // Player-death branch transitions immediately (no veto hook is defined
        // for that side; consult is victory-only per ADR-030 §1).
        if (playerDead)
        {
            ctx.SetState(ctx.State with { Phase = CombatPhase.CombatEnd });
            return;
        }

        // Victory branch: consult ShouldStopCombatFromEnding subscribers. If
        // any veto, skip the transition this tick — the next CheckCombatEnd
        // call (driven by the engine's existing post-mutation checks) will
        // re-poll. Wave A: plumbing is always present; empty plumbing has zero
        // subscribers so deferFlag stays false and we transition unconditionally,
        // preserving the prior snapshot-context behavior without a null check.
        bool[] deferFlag = new bool[1];
        var hookCtx = new HookContext(
            ctx.Plumbing.Context,
            dyingCreatureId: null,
            deferCombatEnd: deferFlag
        );
        ctx.Plumbing.Hooks.Fire(HookType.ShouldStopCombatFromEnding, hookCtx);
        if (deferFlag[0])
            return; // a subscriber vetoed; re-poll on the next CheckCombatEnd

        ctx.SetState(ctx.State with { Phase = CombatPhase.CombatEnd });
    }

    // === Helpers ==========================================================

    /// <summary>
    /// Fire a hook through the context's plumbing. Wave A: plumbing is always
    /// present; empty plumbing (snapshot contexts) has zero subscribers so
    /// Fire and Drain are no-ops, preserving the prior guard behavior without
    /// a null check.
    /// </summary>
    private static void FirePersistedHook(CombatContext ctx, HookType type)
    {
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: CombatEngine.PlayerId,
            PrimaryTargetId: null,
            SourceCreatureId: CombatEngine.PlayerId
        );
        HookFireSession.Run(
            ctx.Plumbing,
            dispatch,
            ctx,
            execCtxObs => ctx.Plumbing.Hooks.Fire(type, new HookContext(execCtxObs))
        );
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
                    if (powers[i].ModelId == id && powers[i].Stacks > 0)
                        return true;
                }
                return false;
            },
            GetPowerStacks: id =>
            {
                for (int i = 0; i < powers.Count; i++)
                {
                    if (powers[i].ModelId == id)
                        return powers[i].Stacks;
                }
                return 0;
            }
        );
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
            if (enemy.IsDead)
                continue;
            var model = (MonsterModel)ctx.Monsters.Get(enemy.Name);
            // Stream-B-T3: prefer the per-creature MoveId (if any) over the shared
            // model's InitialMoveId. The model is a singleton in the catalog —
            // multiple enemies of the same type need independent rotation state.
            string moveId = !string.IsNullOrEmpty(enemy.Intent?.MoveId)
                ? enemy.Intent!.MoveId
                : model.InitialMoveId;
            // Wave-38/B: pass the full MonsterMove so AppliesPowers+SelfBlockGain populate.
            var monsterIntent = MonsterIntent.FromContentIntent(model.GetMove(moveId), moveId);
            ctx.SetState(ctx.State.WithEnemy(enemy with { Intent = monsterIntent }));
        }
    }

    /// <summary>
    /// Resolve a single monster move from the live <see cref="MonsterIntent"/> on the
    /// creature (populated by <see cref="ResolveEnemyIntents"/> at turn-start).
    /// Flat dispatch off <c>intent.Kind</c>, <c>intent.SelfBlockGain</c>, and
    /// <c>intent.AppliesPowers</c> — no per-monster switch. Wave-38/B.
    /// </summary>
    private static void ResolveMove(CombatContext ctx, uint enemyId)
    {
        // Read the live intent that was stamped during ResolveEnemyIntents.
        // The creature is alive at this point (caller checked); use a local var
        // for clarity. MonsterIntent.None is the safe fallback.
        Creature? creature = ctx.State.FindEnemy(enemyId);
        MonsterIntent intent = creature?.Intent ?? MonsterIntent.None;

        // 1. Damage (Attack and AttackDefend both deal damage to player).
        if (
            intent.Kind is MonsterIntentKind.Attack
                or MonsterIntentKind.AttackDefend
        )
        {
            // HitCount honored for multi-hit attacks (Chomper CLAMP 2x8,
            // Exoskeleton SKITTER 3x1, etc.). Damage modifiers (Strength/Weak/
            // Vulnerable) recompute per hit — upstream's per-hit DamageCmd loop.
            int hits = intent.HitCount > 0 ? intent.HitCount : 1;
            for (int i = 0; i < hits; i++)
            {
                // Re-check liveness between hits.
                if (ctx.State.Player.IsDead)
                    break;
                int modified = DamageModifier.Modify(
                    ctx.State,
                    enemyId,
                    CombatEngine.PlayerId,
                    intent.DamagePerHit
                );
                ctx.DealDamage(CombatEngine.PlayerId, modified, enemyId);
            }
        }

        // 2. Self-block (Defend, AttackDefend, or any kind with SelfBlockGain > 0).
        if (intent.SelfBlockGain > 0)
        {
            ctx.GainBlock(enemyId, intent.SelfBlockGain);
        }

        // 3. Power applications — iterate in declaration order, respecting
        //    PowerTarget.Self vs Player.
        for (int i = 0; i < intent.AppliesPowers.Count; i++)
        {
            MonsterIntentPower p = intent.AppliesPowers[i];
            uint target = p.Target == PowerTarget.Player ? CombatEngine.PlayerId : enemyId;
            ctx.ApplyPower(target, p.PowerId, p.Stacks, enemyId);
        }

        // 4. Status kind: no engine payload yet (card-pollution path deferred);
        //    intent rotation still advances normally via the caller.
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
            ctx.SetState(
                creature.IsPlayer
                    ? ctx.State.WithPlayer(newCreature)
                    : ctx.State.WithEnemy(newCreature)
            );
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
        Creature? owner = ownerId == CombatEngine.PlayerId
            ? ctx.State.Player
            : ctx.State.FindEnemy(ownerId);
        if (owner is null || owner.IsDead)
            return;

        int poisonIndex = -1;
        for (int i = 0; i < owner.Powers.Count; i++)
        {
            if (owner.Powers[i].ModelId == PowerIds.Poison)
            {
                poisonIndex = i;
                break;
            }
        }
        if (poisonIndex < 0)
            return;

        PowerInstance poison = owner.Powers[poisonIndex];
        if (poison.Stacks <= 0)
            return;

        // Poison damage IGNORES block (upstream calls it "unblockable").
        // To bypass our DealDamage block-first logic, we directly mutate HP.
        int newHp = Math.Max(0, owner.CurrentHp - poison.Stacks);
        var hpReduced = owner with { CurrentHp = newHp };

        // Decrement stacks; remove if zero.
        int newStacks = poison.Stacks - 1;
        ImmutableList<PowerInstance> newPowers =
            newStacks > 0
                ? hpReduced.Powers.SetItem(poisonIndex, poison with { Stacks = newStacks })
                : hpReduced.Powers.RemoveAt(poisonIndex);

        var updated = hpReduced with { Powers = newPowers };
        ctx.SetState(
            updated.IsPlayer ? ctx.State.WithPlayer(updated) : ctx.State.WithEnemy(updated)
        );
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
            // Strip zero-stack powers — then fire OnRemoved for each stripped
            // PowerModel so _handlesByCreature is kept in sync.  Without this,
            // a re-application of the same power (e.g. Weak applied on turn N,
            // ticked to 0 and stripped at turn end, then Weak re-applied turn N+1)
            // hits the "already attached" guard in PowerModel.OnApplied because
            // the singleton still has the creature-id registered.
            var stripped = newPowers.FindAll(p => p.Stacks <= 0 && p.ModelId != PowerIds.Strength);
            newPowers = newPowers.RemoveAll(p => p.Stacks <= 0 && p.ModelId != PowerIds.Strength);
            var updated = owner with { Powers = newPowers };
            ctx.SetState(
                owner.IsPlayer ? ctx.State.WithPlayer(updated) : ctx.State.WithEnemy(updated)
            );
            // Notify each stripped PowerModel so it can un-subscribe its hooks.
            // Wave A: plumbing is always present; empty plumbing has a real-but-
            // inert registry — PowerModel.OnRemoved is idempotent on zero-subscriber
            // registry (PowerModel.cs:138-141), so unconditional call is safe.
            foreach (PowerInstance pi in stripped)
            {
                if (ctx.Powers.TryGet(pi.ModelId, out var raw) && raw is PowerModel pm)
                    pm.OnRemoved(owner.Id, ctx.Plumbing.Hooks);
            }
        }
    }
}
