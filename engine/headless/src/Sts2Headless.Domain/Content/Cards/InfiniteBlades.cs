using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.InfiniteBlades</c>: 1 energy Power. At turn start, add Shiv to hand.
/// Upgrade adds Innate.
/// </summary>
public sealed class InfiniteBlades : CardModel
{
    public const string CanonicalId = "InfiniteBlades";
    public const int Amount = 1;

    public InfiniteBlades()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.InfiniteBlades, Amount, null));
    }
}
