using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.FanOfKnives</c>: 2 energy Power. Adds 4 Shivs to hand. Upgrade: +1.
/// </summary>
public sealed class FanOfKnives : CardModel
{
    public const string CanonicalId = "FanOfKnives";
    public const int BaseShivs = 4;
    public const int UpgradeDelta = 1;
    public int Shivs => BaseShivs;

    public FanOfKnives()
        : base(CanonicalId, 2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.FanOfKnives, BaseShivs, null));
    }
}
