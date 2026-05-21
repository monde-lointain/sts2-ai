using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Gain <see cref="Amount"/> block IF the player's current block is zero" —
/// upstream Orichalcum (<c>BeforeTurnEnd</c>: if no block this turn, gain 6).
/// Predicate evaluated by the dispatcher at dispatch time.
/// </summary>
public sealed record ConditionalGainBlockAction(int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        ctx.Observer?.Record(this);
    }
}
