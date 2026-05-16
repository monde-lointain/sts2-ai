using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Tracking</c>: 2 energy Power. Apply 1 Tracking. Upgrade: cost 1.
/// </summary>
public sealed class Tracking : CardModel
{
    public const string CanonicalId = "Tracking";
    public const int BaseCost = 2;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Tracking()
        : base(CanonicalId, 2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Tracking, 1, null));
    }
}
