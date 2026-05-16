using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Translates the S5-layer record actions
/// (<see cref="DealDamageAction"/>, <see cref="GainBlockAction"/>,
/// <see cref="ApplyPowerAction"/>, <see cref="DrawCardsAction"/>,
/// <see cref="DiscardCardsAction"/>, <see cref="HealAction"/>,
/// <see cref="ExtraHandDrawAction"/>) into state mutations on an
/// <see cref="ICombatContext"/>.
///
/// <para>
/// <b>Why a separate dispatcher:</b> S5's effect-action records have no-op
/// <c>Execute</c> bodies (they only record into <c>EffectObserver</c>) because
/// S5 was fenced from CombatState. S6 is the first stage with real state — so
/// the translation lives here, not in S5 source.
/// </para>
///
/// <para>
/// <b>Usage:</b> the engine attaches an <c>EffectObserver</c> scope around its
/// content invocations (card OnPlay, relic hooks). After the queue drains —
/// during which the S5 actions accumulate in the observer log as no-ops — the
/// engine iterates the log and calls <see cref="Apply"/> per action.
/// </para>
///
/// <para>
/// <b>Targeting:</b> some actions specify a string target (e.g., DealDamage).
/// The engine resolves the string to a creature id when constructing the
/// action's dispatch context; <see cref="DispatchContext.PlayerId"/> and
/// <see cref="DispatchContext.PrimaryTargetId"/> drive the resolution.
/// </para>
/// </summary>
public static class EffectDispatcher
{
    /// <summary>
    /// Per-card-play / per-hook dispatch context. Names a "default target" for
    /// the player (self-target effects) and a "primary enemy" (the card's
    /// chosen target, if any). String-id targets emitted by S5 actions resolve
    /// against these.
    /// </summary>
    public readonly record struct DispatchContext(
        uint PlayerId,
        uint? PrimaryTargetId,
        uint SourceCreatureId
    );

    /// <summary>
    /// Apply a single action to <paramref name="ctx"/>. Recognizes the S5
    /// effect-action records and translates each to the matching
    /// <see cref="ICombatContext"/> mutation. Unknown actions are no-ops
    /// (they were likely handled by S4 hook fires elsewhere).
    /// </summary>
    public static void Apply(IAction action, ICombatContext ctx, DispatchContext dispatch)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(ctx);

        switch (action)
        {
            case DealDamageAction dmg:
                {
                    uint? target = ResolveTarget(dmg.Target, dispatch);
                    if (target is null)
                        return;
                    int modified = DamageModifier.Modify(
                        ctx.State,
                        dispatch.SourceCreatureId,
                        target.Value,
                        dmg.Amount
                    );
                    ctx.DealDamage(target.Value, modified, dispatch.SourceCreatureId);
                    break;
                }

            case GainBlockAction blk:
                ctx.GainBlock(dispatch.PlayerId, blk.Amount);
                break;

            case ApplyPowerAction pwr:
                {
                    uint? target = ResolveTarget(pwr.Target, dispatch);
                    if (target is null)
                        return;
                    ctx.ApplyPower(target.Value, pwr.PowerId, pwr.Amount, dispatch.SourceCreatureId);
                    break;
                }

            case ApplyPowerToAllEnemiesAction allPwr:
                {
                    // Snapshot enemy ids to avoid mid-iteration list mutation, then
                    // apply to every living enemy in spawn order. Matches upstream's
                    // PowerCmd.Apply(HittableEnemies, ...) shape.
                    var enemyIds = new List<uint>(ctx.State.Enemies.Count);
                    foreach (Creature e in ctx.State.Enemies)
                    {
                        if (!e.IsDead)
                            enemyIds.Add(e.Id);
                    }
                    foreach (uint eid in enemyIds)
                    {
                        ctx.ApplyPower(eid, allPwr.PowerId, allPwr.Amount, dispatch.SourceCreatureId);
                    }
                    break;
                }

            case DealDamageToAllEnemiesAction allDmg:
                {
                    // Snapshot enemy ids; deal damage in spawn order. Each target's
                    // damage goes through the modifier pipeline independently so
                    // per-target Vulnerable applies per enemy.
                    var enemyIds = new List<uint>(ctx.State.Enemies.Count);
                    foreach (Creature e in ctx.State.Enemies)
                    {
                        if (!e.IsDead)
                            enemyIds.Add(e.Id);
                    }
                    foreach (uint eid in enemyIds)
                    {
                        int modified = DamageModifier.Modify(
                            ctx.State,
                            dispatch.SourceCreatureId,
                            eid,
                            allDmg.Amount
                        );
                        ctx.DealDamage(eid, modified, dispatch.SourceCreatureId);
                    }
                    break;
                }

            case ConditionalGainBlockAction condBlk:
                {
                    // Orichalcum-shaped: gain block only if player has zero block.
                    if (ctx.State.Player.Block == 0)
                    {
                        ctx.GainBlock(dispatch.PlayerId, condBlk.Amount);
                    }
                    break;
                }

            case XCostDamageAction xDmg:
                {
                    // Skewer: damage_per_hit, with hit_count = LastSpentEnergy.
                    int hits = ctx.State.LastSpentEnergy;
                    if (hits <= 0)
                        return;
                    uint? target = ResolveTarget(xDmg.Target, dispatch);
                    if (target is null)
                        return;
                    for (int i = 0; i < hits; i++)
                    {
                        // Re-evaluate damage per hit so per-hit modifiers (which
                        // depend on live state) apply correctly. Mirrors the
                        // engine's existing per-hit attack loop.
                        Creature? freshTarget = ctx.State.FindEnemy(target.Value);
                        if (freshTarget is null || freshTarget.IsDead)
                            break;
                        int modified = DamageModifier.Modify(
                            ctx.State,
                            dispatch.SourceCreatureId,
                            target.Value,
                            xDmg.DamagePerHit
                        );
                        ctx.DealDamage(target.Value, modified, dispatch.SourceCreatureId);
                    }
                    break;
                }

            case XCostApplyPowerAction xPwr:
                {
                    // Malaise: applies SignMultiplier * (X + Bonus) stacks of
                    // PowerId to Target. X is LastSpentEnergy.
                    int x = ctx.State.LastSpentEnergy;
                    int total = (x + xPwr.Bonus) * xPwr.SignMultiplier;
                    if (total == 0)
                        return;
                    uint? target = ResolveTarget(xPwr.Target, dispatch);
                    if (target is null)
                        return;
                    ctx.ApplyPower(target.Value, xPwr.PowerId, total, dispatch.SourceCreatureId);
                    break;
                }

            case CalcDamageFromShivExhaustAction shivDmg:
                {
                    // KnifeTrap: damage scales with the count of Shiv-tagged
                    // cards in the player's exhaust pile (ExhaustedShivCount).
                    int raw = shivDmg.BasePerShiv * ctx.State.ExhaustedShivCount;
                    if (raw <= 0)
                        return;
                    uint? target = ResolveTarget(shivDmg.Target, dispatch);
                    if (target is null)
                        return;
                    int modified = DamageModifier.Modify(
                        ctx.State,
                        dispatch.SourceCreatureId,
                        target.Value,
                        raw
                    );
                    ctx.DealDamage(target.Value, modified, dispatch.SourceCreatureId);
                    break;
                }

            case CalcDamageAction calcDmg:
                {
                    // Stream-B-T4: resolve the formula multiplier against the live
                    // CombatState aggregate, then route through the standard
                    // damage pipeline (Strength / Vulnerable / Weak modifiers).
                    uint? target = ResolveTarget(calcDmg.Target, dispatch);
                    if (target is null)
                        return;
                    int multiplier = ResolveCalcMultiplier(ctx.State, calcDmg.MultiplierKey);
                    int raw = calcDmg.BaseDamage * multiplier;
                    if (raw <= 0)
                        return;
                    int modified = DamageModifier.Modify(
                        ctx.State,
                        dispatch.SourceCreatureId,
                        target.Value,
                        raw
                    );
                    ctx.DealDamage(target.Value, modified, dispatch.SourceCreatureId);
                    break;
                }

            case CalcBlockAction calcBlk:
                {
                    // Stream-B-T4: block scales with a CombatState aggregate
                    // (Mirage: total Poison stacks across living enemies).
                    int gained =
                        calcBlk.BaseBlock + ResolveCalcMultiplier(ctx.State, calcBlk.MultiplierKey);
                    if (gained <= 0)
                        return;
                    ctx.GainBlock(dispatch.PlayerId, gained);
                    break;
                }

            case DrawCardsAction draw:
                ctx.DrawCards(draw.Count);
                break;

            case DiscardCardsAction:
                // Player-choice discard from hand. Smoke harness doesn't model
                // player choice; we discard nothing for now (S11 control plane
                // will surface the choice). Recorded so the action queue drain
                // remains a single pass.
                break;

            case HealAction heal:
                ctx.Heal(dispatch.PlayerId, heal.Amount);
                break;

            case ExtraHandDrawAction extra:
                ctx.ModifyHandDrawSize(extra.Extra);
                break;

            default:
                // Other actions (cascading hooks, future-stage actions) are
                // handled by S4's drain or a later stage. No-op here.
                break;
        }
    }

    /// <summary>
    /// Translate the S5 action's string target (null = self) into a creature
    /// id. Null target on attack-shape actions in non-self-target cards is
    /// possible only when no enemy was chosen; we return null and the caller
    /// no-ops the action.
    /// </summary>
    private static uint? ResolveTarget(string? targetString, DispatchContext dispatch)
    {
        if (targetString is null)
        {
            // null = self / no-target. Use player id.
            return dispatch.PlayerId;
        }
        if (uint.TryParse(targetString, out uint parsedId))
        {
            return parsedId;
        }
        // Fallback to the dispatch context's primary target if the string is
        // a non-numeric placeholder (smoke set never emits non-numeric ids).
        return dispatch.PrimaryTargetId;
    }

    /// <summary>
    /// Stream-B-T4: resolve a calc-damage / calc-block multiplier key against
    /// the live <see cref="CombatState"/>. The vocabulary is small (one entry
    /// per supported formula card); unknown keys return 0 so the action
    /// degrades gracefully rather than throwing — keeps the dispatcher
    /// forward-compatible with content that adds new keys.
    /// </summary>
    private static int ResolveCalcMultiplier(CombatState state, string key)
    {
        if (string.IsNullOrEmpty(key))
            return 0;
        switch (key)
        {
            case "attacks_played_this_turn":
                // Finisher: hits == attacks-finished-this-turn (THIS card not yet
                // counted; engine bumps after OnPlay drains).
                return state.AttacksPlayedThisTurn;
            case "cards_drawn_this_combat":
                // Murder: damage = base × cards drawn this combat.
                return state.CardsDrawnThisCombat;
            case "poison_total_on_enemies":
                {
                    // Mirage: block = base + sum(Poison.Stacks for living enemies).
                    int total = 0;
                    for (int i = 0; i < state.Enemies.Count; i++)
                    {
                        Creature e = state.Enemies[i];
                        if (e.IsDead)
                            continue;
                        for (int j = 0; j < e.Powers.Count; j++)
                        {
                            PowerInstance p = e.Powers[j];
                            if (p.ModelId == Sts2Headless.Domain.Content.Powers.PowerIds.Poison)
                            {
                                total += p.Stacks;
                            }
                        }
                    }
                    return total;
                }
            default:
                return 0;
        }
    }
}
