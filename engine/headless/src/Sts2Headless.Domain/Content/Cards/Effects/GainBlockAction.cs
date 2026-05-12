using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Gain <see cref="Amount"/> block on the player" — upstream
/// <c>CreatureCmd.GainBlock(owner.Creature, amount, cardPlay)</c>. See
/// <see cref="DealDamageAction"/> for why Execute is a no-op in S5.
/// </summary>
public sealed record GainBlockAction(int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}
