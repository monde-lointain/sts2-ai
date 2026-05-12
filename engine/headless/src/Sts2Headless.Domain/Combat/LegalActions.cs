using System.Collections.Immutable;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Computes the set of <see cref="PlayerAction"/>s legal in the current
/// <see cref="CombatState"/>. M6d will surface this through the
/// hook-protocol-mask channel.
///
/// <para>
/// <b>Rules (Phase 1 smoke set):</b>
/// </para>
/// <list type="bullet">
///   <item>During <see cref="CombatPhase.PlayerActing"/>:
///     <list type="bullet">
///       <item>Every card in hand with cost &lt;= current energy and a valid
///             target is playable. For <c>AnyEnemy</c> cards, one action per
///             living enemy. For <c>Self</c>-target cards, one action with
///             <c>TargetEnemyId = null</c>.</item>
///       <item><see cref="PlayerAction.EndTurn.Instance"/> is always
///             available.</item>
///     </list>
///   </item>
///   <item>During any other phase (enemy phase, combat-end): empty list.</item>
/// </list>
/// </summary>
public static class LegalActions
{
    /// <summary>
    /// Enumerate legal player actions for <paramref name="state"/> using
    /// <paramref name="cards"/> to resolve <c>CardInstance.ModelId</c> →
    /// <see cref="CardModel"/>.
    /// </summary>
    public static ImmutableArray<PlayerAction> Enumerate(CombatState state, CardCatalog cards)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);

        if (state.Phase != CombatPhase.PlayerActing)
        {
            return ImmutableArray<PlayerAction>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PlayerAction>();

        // Per-card legality.
        for (int i = 0; i < state.HandPile.Cards.Count; i++)
        {
            CardInstance instance = state.HandPile.Cards[i];
            var model = (CardModel)cards.Get(instance.ModelId);
            int cost = instance.CostOverride ?? model.Cost;
            if (cost > state.Energy) continue;

            switch (model.Target)
            {
                case TargetType.AnyEnemy:
                case TargetType.RandomEnemy: // one action per living enemy (player still chooses for AnyEnemy)
                    for (int e = 0; e < state.Enemies.Count; e++)
                    {
                        Creature enemy = state.Enemies[e];
                        if (enemy.IsDead) continue;
                        builder.Add(new PlayerAction.PlayCard(instance.InstanceId, enemy.Id));
                    }
                    break;

                case TargetType.AllEnemies:
                    // Single play, no target enumerated (the engine resolves
                    // damage against all enemies in the AppliesPowers list).
                    if (state.Enemies.Any(e => e.IsAlive))
                    {
                        builder.Add(new PlayerAction.PlayCard(instance.InstanceId, TargetEnemyId: null));
                    }
                    break;

                case TargetType.Self:
                case TargetType.None:
                case TargetType.AnyPlayer:
                case TargetType.AnyAlly:
                case TargetType.AllAllies:
                case TargetType.TargetedNoCreature:
                case TargetType.Osty:
                default:
                    builder.Add(new PlayerAction.PlayCard(instance.InstanceId, TargetEnemyId: null));
                    break;
            }
        }

        // EndTurn always legal during PlayerActing.
        builder.Add(PlayerAction.EndTurn.Instance);

        return builder.ToImmutable();
    }
}
