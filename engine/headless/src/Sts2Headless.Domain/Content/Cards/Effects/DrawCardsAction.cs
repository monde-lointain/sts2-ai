using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Draw <see cref="Count"/> cards" — upstream
/// <c>CardPileCmd.Draw(ctx, count, owner)</c>. See
/// <see cref="DealDamageAction"/> for why Execute is a no-op in S5.
/// </summary>
public sealed record DrawCardsAction(int Count) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}
