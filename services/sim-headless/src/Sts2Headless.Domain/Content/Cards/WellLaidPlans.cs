using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.WellLaidPlans</c>: 1 energy Power. Retain 1 card at end of turn. Upgrade: +1.
/// </summary>
public sealed class WellLaidPlans : CardModel
{
    public const string CanonicalId = "WellLaidPlans";
    public const int BaseRetain = 1;
    public const int UpgradeDelta = 1;
    public int RetainAmount => BaseRetain;

    public WellLaidPlans() : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Hook-only effect (retain handler).
    }
}
