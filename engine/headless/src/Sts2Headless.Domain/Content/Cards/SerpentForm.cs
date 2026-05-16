using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.SerpentForm</c>: 3 energy Power. Apply 4 SerpentForm. Upgrade: +2.
/// </summary>
public sealed class SerpentForm : CardModel
{
    public const string CanonicalId = "SerpentForm";
    public const int BaseAmount = 4;
    public const int UpgradeDelta = 2;
    public int Amount => BaseAmount;

    public SerpentForm()
        : base(CanonicalId, 3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.SerpentForm, BaseAmount, null));
    }
}
