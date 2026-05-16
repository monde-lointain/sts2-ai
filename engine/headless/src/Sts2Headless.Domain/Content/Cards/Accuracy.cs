using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Accuracy</c>: 1 energy Power. Applies 4 Accuracy. Upgrade: +2.
/// </summary>
public sealed class Accuracy : CardModel
{
    public const string CanonicalId = "Accuracy";
    public const int BaseAmount = 4;
    public const int UpgradeDelta = 2;
    public int Amount => BaseAmount;

    public Accuracy()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Accuracy, BaseAmount, null));
    }
}
