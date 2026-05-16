using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.PhantomBlades</c>: 1 energy Power. Apply 9 PhantomBlades. Upgrade: +3.
/// </summary>
public sealed class PhantomBlades : CardModel
{
    public const string CanonicalId = "PhantomBlades";
    public const int BaseAmount = 9;
    public const int UpgradeDelta = 3;
    public int Amount => BaseAmount;

    public PhantomBlades()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.PhantomBlades, BaseAmount, null));
    }
}
