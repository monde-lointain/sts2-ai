using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Models;
using DomainExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Card-play logic: validates a card play from hand, consumes energy, runs
/// OnPlay, dispatches effects, moves the card to discard, announces deaths,
/// and checks combat end. Owns what was <c>CombatEngine.PlayerPlayCard</c>.
/// </summary>
internal static class CardPlayer
{
    /// <summary>
    /// Player plays a card from hand. Validates: card must be in hand,
    /// energy must be sufficient. Consumes energy, runs OnPlay, dispatches
    /// effects via <see cref="EffectDispatcher"/>, moves card to discard.
    /// </summary>
    /// <param name="ctx">Live combat context.</param>
    /// <param name="cardInstanceId">Id of the card in hand to play.</param>
    /// <param name="targetEnemyId">
    /// Id of the chosen target (null for self-target / no-target cards).
    /// </param>
    internal static void PlayCard(CombatContext ctx, uint cardInstanceId, CreatureId? targetEnemyId)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.State.Phase != CombatPhase.PlayerActing)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard called in phase {ctx.State.Phase}; expected PlayerActing."
            );
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
                $"PlayerPlayCard: card instance id={cardInstanceId} not in hand "
                    + $"(hand has: {string.Join(",", s.HandPile.Cards.Select(c => c.InstanceId))})."
            );
        }

        // --- Resolve model + cost ----------------------------------------
        var cardModel = (CardModel)ctx.Cards.Get(cardInstance.ModelId);
        // B.1-gamma-T5: X-cost cards consume ALL the player's current energy.
        // Upstream's CardModel.ResolveEnergyXValue() reads exactly this value.
        int cost = cardModel.IsXCost ? s.Energy : (cardInstance.CostOverride ?? cardModel.Cost);
        if (s.Energy < cost)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard: insufficient energy (have {s.Energy}, need {cost} for {cardModel.Id})."
            );
        }

        // --- Validate target shape ---------------------------------------
        bool needsEnemy =
            cardModel.Target == TargetType.AnyEnemy
            || cardModel.Target == TargetType.AllEnemies
            || cardModel.Target == TargetType.RandomEnemy;
        if (needsEnemy && targetEnemyId is null)
        {
            throw new InvalidOperationException(
                $"PlayerPlayCard: card {cardModel.Id} requires an enemy target."
            );
        }
        if (needsEnemy && targetEnemyId.HasValue)
        {
            // Throws if not found.
            _ = s.GetEnemy(targetEnemyId.Value);
        }

        // --- Consume energy ----------------------------------------------
        // B.1-gamma-T5: also snapshot the spent energy for X-cost cards so the
        // card's OnPlay body can read it via CombatContext.AllRemainingEnergy()
        // / TrailCounters.LastSpentEnergy.
        ctx.SetState(
            ctx.State with
            {
                Energy = ctx.State.Energy - cost,
                Trail = ctx.State.Trail with
                {
                    LastSpentEnergy = cardModel.IsXCost ? cost : ctx.State.Trail.LastSpentEnergy,
                },
            }
        );

        // --- Move card from hand to discard ------------------------------
        // Done BEFORE OnPlay so cards that draw can refill from a clean hand.
        ctx.SetState(
            ctx.State with
            {
                HandPile = ctx.State.HandPile.Remove(cardInstanceId),
                DiscardPile = ctx.State.DiscardPile.Add(cardInstance),
            }
        );

        // --- Invoke OnPlay; dispatch enqueued effects --------------------
        var hookRegistry = new HookRegistry();
        var actionQueue = new ActionQueue();
        var execCtx = new DomainExecutionContext(ctx.Clock, ctx.Rng, hookRegistry, actionQueue);
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: CombatEngine.PlayerId,
            PrimaryTargetId: targetEnemyId,
            SourceCreatureId: CombatEngine.PlayerId
        );

        // ADR-030 §1: snapshot pre-play alive-set so any deaths from this
        // card's effects fan out to AfterDeath subscribers. We snapshot
        // BEFORE OnPlay drains so multi-target / multi-hit cards that kill
        // several enemies in one drain fire AfterDeath once per id.
        ImmutableArray<CreatureId> aliveBeforePlay = DeathBroadcaster.SnapshotAliveIds(ctx);
        // Note: card-play uses a fresh per-play HookPlumbing (not ctx.Plumbing)
        // for the OnPlay invocation. We wrap it into a transient HookPlumbing
        // so HookFireSession can be used here as well.
        var cardPlumbing = new HookPlumbing(hookRegistry, actionQueue, execCtx);
        HookFireSession.Run(
            cardPlumbing,
            dispatch,
            ctx,
            execCtxObs => cardModel.OnPlay(execCtxObs, targetEnemyId)
        );
        // ADR-030 §1 fire-site: announce any deaths caused by this card
        // before the per-turn-attack counter bump and the combat-end check.
        // Order matters: a SurprisePower-style AfterDeath handler may
        // mid-combat-spawn replacement enemies; CheckCombatEnd then
        // consults ShouldStopCombatFromEnding so the new enemies are not
        // skipped.
        DeathBroadcaster.FireAfterDeathForNewDeaths(ctx, aliveBeforePlay);

        // Stream-B-T4: bump the per-turn attack counter AFTER OnPlay drains, so
        // calc-damage formulas (Finisher's "× attacks played THIS turn") see
        // the prior count — matching upstream's CardPlaysFinished semantics
        // (the entry for THIS card is recorded only after its play completes).
        int newAttacks =
            ctx.State.Trail.AttacksPlayedThisTurn + (cardModel.Type == CardType.Attack ? 1 : 0);
        ctx.SetState(
            ctx.State with
            {
                PlayerRngCounter = ctx.Rng.Counter,
                Trail = ctx.State.Trail with { AttacksPlayedThisTurn = newAttacks },
            }
        );

        // Check end on every mutation point in case the card killed an enemy.
        TurnRunner.CheckCombatEnd(ctx);
    }
}
