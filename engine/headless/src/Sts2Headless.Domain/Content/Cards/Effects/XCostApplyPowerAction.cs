using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// B.1-gamma-T5: X-cost debuff action. Applies <c>X</c> stacks (signed) of
/// <see cref="PowerId"/> to <see cref="Target"/>, where <c>X</c> is the most
/// recently snapshotted spent energy. <see cref="SignMultiplier"/> can be
/// -1 (Malaise's StrengthPower: -X) or +1 (Malaise's WeakPower: +X) so a
/// single card can fan out to multiple powers via two queued actions.
/// </summary>
public sealed record XCostApplyPowerAction(
    string PowerId,
    int SignMultiplier,
    string? Target,
    int Bonus = 0
) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        EffectObserver.Record(this);
    }
}
