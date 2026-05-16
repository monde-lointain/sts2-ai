using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;

namespace Sts2Headless.Host;

/// <summary>
/// Orchestrates a single combat from <see cref="CombatPhase.PlayerActing"/>
/// through to <see cref="CombatPhase.CombatEnd"/>. Pulls <see cref="PlayerAction"/>s
/// from an <see cref="IScriptedActionProvider"/>, applies them via the S6
/// <see cref="CombatEngine"/>, and emits one log line per turnover.
///
/// <para>
/// <b>Lifecycle (per CombatEngine spec):</b>
/// </para>
/// <list type="number">
///   <item>StartCombat (already run by <see cref="CompositionRoot"/>).</item>
///   <item>While not CombatEnd:
///     <list type="bullet">
///       <item>Enumerate legal actions.</item>
///       <item>Ask provider for next action.</item>
///       <item>If <c>PlayCard</c> → <c>CombatEngine.PlayerPlayCard</c>.</item>
///       <item>If <c>EndTurn</c> → <c>EndPlayerTurn</c> + <c>EnemyTurn</c>
///             + <c>StartPlayerTurn</c> (only the last if combat still alive).</item>
///     </list>
///   </item>
/// </list>
///
/// <para>
/// <b>Cooperative cancellation:</b> the provided
/// <see cref="CancellationToken"/> is checked at every action boundary and at
/// every turnover. When triggered, the loop completes the current
/// <see cref="Domain.Actions.ExecutionContext"/> step (i.e. a single
/// PlayerPlayCard or one EnemyTurn) and returns <see cref="LoopOutcome.Cancelled"/>.
/// </para>
///
/// <para>
/// <b>Metrics:</b> the <see cref="IMetricsRegistry"/> is incremented at each
/// boundary. The metrics names mirror the M9 spec's placeholder set:
/// <c>sts2_combats_total</c>, <c>sts2_turns_total</c>, <c>sts2_actions_total</c>.
/// </para>
/// </summary>
public static class MainLoop
{
    /// <summary>Outcome of <see cref="Run"/>: drives the host's exit code.</summary>
    public enum LoopOutcome
    {
        /// <summary>Player won — <see cref="Program.ExitVictory"/>.</summary>
        Victory,

        /// <summary>Player lost — <see cref="Program.ExitDefeat"/>.</summary>
        Defeat,

        /// <summary>Provider exhausted before combat ended — <see cref="Program.ExitError"/>.</summary>
        ScriptExhausted,

        /// <summary>SIGTERM observed — <see cref="Program.ExitVictory"/> per graceful-shutdown contract.</summary>
        Cancelled,
    }

    /// <summary>Result bundle returned by <see cref="Run"/>.</summary>
    public sealed record RunResult(
        LoopOutcome Outcome,
        int TurnsPlayed,
        int ActionsApplied,
        CombatState FinalState
    );

    /// <summary>
    /// Drive the combat to completion. Returns when the combat ends, the
    /// provider returns null, or <paramref name="cancellation"/> fires.
    /// </summary>
    /// <param name="probe">
    /// Optional per-step canonical-hash sink (S13 determinism probe). When
    /// non-null, one record is emitted at each turn boundary
    /// (<c>combat_start</c>, <c>turn_start</c>, <c>card_played</c>,
    /// <c>enemy_turn</c>, <c>combat_end</c>).
    /// </param>
    public static RunResult Run(
        CombatContext ctx,
        CardCatalog cards,
        IScriptedActionProvider provider,
        IStructuredLogger logger,
        IMetricsRegistry metrics,
        CancellationToken cancellation = default,
        IProbeStream? probe = null
    )
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);

        metrics.IncrementCounter(MetricNames.CombatsTotal, 1);
        logger.Log(
            "combat_start",
            new Dictionary<string, object?>
            {
                ["turn"] = ctx.State.TurnCounter,
                ["player_hp"] = ctx.State.Player.CurrentHp,
                ["enemies"] = ctx
                    .State.Enemies.Select(e => new
                    {
                        id = e.Id,
                        name = e.Name,
                        hp = e.CurrentHp,
                    })
                    .ToArray(),
            }
        );
        probe?.Emit("combat_start", ctx.State);
        // Turn 1 has already been opened by CombatEngine.StartCombat.
        logger.Log(
            "turn_start",
            new Dictionary<string, object?>
            {
                ["turn"] = ctx.State.TurnCounter,
                ["hand_size"] = ctx.State.HandPile.Cards.Count,
            }
        );
        probe?.Emit("turn_start", ctx.State);
        metrics.IncrementCounter(MetricNames.TurnsTotal, 1);

        int actionsApplied = 0;
        int turnsPlayed = 1; // turn 1 is already started

        while (!ctx.State.IsCombatOver)
        {
            if (cancellation.IsCancellationRequested)
            {
                logger.Log(
                    "shutdown",
                    new Dictionary<string, object?>
                    {
                        ["reason"] = "cancellation_requested",
                        ["turn"] = ctx.State.TurnCounter,
                    }
                );
                return new RunResult(LoopOutcome.Cancelled, turnsPlayed, actionsApplied, ctx.State);
            }

            ImmutableArray<PlayerAction> legal = LegalActions.Enumerate(ctx.State, cards);
            if (legal.IsEmpty)
            {
                // No legal player action — shouldn't happen during PlayerActing,
                // but guard against it. Force end-turn if we're not already at
                // combat-end.
                logger.Log(
                    "legal_actions_empty",
                    new Dictionary<string, object?> { ["phase"] = ctx.State.Phase.ToString() }
                );
                break;
            }

            PlayerAction? next = provider.NextAction(ctx.State, legal);
            if (next is null)
            {
                logger.Log(
                    "script_exhausted",
                    new Dictionary<string, object?>
                    {
                        ["turn"] = ctx.State.TurnCounter,
                        ["legal_count"] = legal.Length,
                    }
                );
                return new RunResult(
                    LoopOutcome.ScriptExhausted,
                    turnsPlayed,
                    actionsApplied,
                    ctx.State
                );
            }

            switch (next)
            {
                case PlayerAction.PlayCard pc:
                {
                    // Look up the card model id for the log line BEFORE the
                    // engine mutates the hand. (PlayerPlayCard moves the
                    // card to discard so the lookup would still succeed,
                    // but doing it before keeps the log temporally honest.)
                    CardInstance? inst = ctx.State.HandPile.Cards.FirstOrDefault(c =>
                        c.InstanceId == pc.CardInstanceId
                    );
                    string modelId = inst?.ModelId ?? "<unknown>";
                    CombatEngine.PlayerPlayCard(ctx, pc.CardInstanceId, pc.TargetEnemyId);
                    actionsApplied++;
                    metrics.IncrementCounter(MetricNames.ActionsTotal, 1);
                    logger.Log(
                        "card_played",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = ctx.State.TurnCounter,
                            ["card"] = modelId,
                            ["instance_id"] = pc.CardInstanceId,
                            ["target"] = pc.TargetEnemyId,
                        }
                    );
                    probe?.Emit("card_played", ctx.State);
                    break;
                }
                case PlayerAction.EndTurn:
                {
                    CombatEngine.EndPlayerTurn(ctx);
                    actionsApplied++;
                    metrics.IncrementCounter(MetricNames.ActionsTotal, 1);
                    if (ctx.State.IsCombatOver)
                        break;

                    CombatEngine.EnemyTurn(ctx);
                    logger.Log(
                        "enemy_action",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = ctx.State.TurnCounter,
                            ["player_hp"] = ctx.State.Player.CurrentHp,
                            ["player_block"] = ctx.State.Player.Block,
                        }
                    );
                    probe?.Emit("enemy_turn", ctx.State);
                    if (ctx.State.IsCombatOver)
                        break;

                    CombatEngine.StartPlayerTurn(ctx);
                    turnsPlayed++;
                    metrics.IncrementCounter(MetricNames.TurnsTotal, 1);
                    logger.Log(
                        "turn_start",
                        new Dictionary<string, object?>
                        {
                            ["turn"] = ctx.State.TurnCounter,
                            ["hand_size"] = ctx.State.HandPile.Cards.Count,
                        }
                    );
                    probe?.Emit("turn_start", ctx.State);
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"MainLoop: unhandled PlayerAction subtype {next.GetType().Name}."
                    );
            }
        }

        LoopOutcome outcome =
            ctx.State.PlayerWon ? LoopOutcome.Victory
            : ctx.State.PlayerLost ? LoopOutcome.Defeat
            : LoopOutcome.ScriptExhausted;

        logger.Log(
            "combat_end",
            new Dictionary<string, object?>
            {
                ["outcome"] = outcome.ToString(),
                ["turns"] = turnsPlayed,
                ["actions"] = actionsApplied,
                ["player_hp"] = ctx.State.Player.CurrentHp,
            }
        );
        probe?.Emit("combat_end", ctx.State);

        return new RunResult(outcome, turnsPlayed, actionsApplied, ctx.State);
    }
}
