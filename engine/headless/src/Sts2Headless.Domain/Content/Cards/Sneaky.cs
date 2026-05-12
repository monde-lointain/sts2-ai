using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Sneaky</c>: 2 energy Power. Apply 1 Sneaky. Upgrade: +1.
/// </summary>
public sealed class Sneaky : CardModel
{
    public const string CanonicalId = "Sneaky";
    public const int BaseAmount = 1;
    public const int UpgradeDelta = 1;
    public int Amount => BaseAmount;

    public Sneaky() : base(CanonicalId, 2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Sneaky, BaseAmount, null));
    }
}
