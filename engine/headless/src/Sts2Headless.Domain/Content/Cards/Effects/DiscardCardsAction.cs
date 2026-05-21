using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Discard <see cref="Count"/> cards from hand (player selects)" — upstream
/// <c>CardSelectCmd.FromHandForDiscard(...)</c> chained with
/// <c>CardCmd.Discard(ctx, choice)</c>. The player-selection step is a player-choice
/// boundary handled by S11 Control Plane — at the card level we just enqueue the
/// discard requirement. See <see cref="DealDamageAction"/> for why Execute is a
/// no-op in S5.
/// </summary>
public sealed record DiscardCardsAction(int Count) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        ctx.Observer?.Record(this);
    }
}
