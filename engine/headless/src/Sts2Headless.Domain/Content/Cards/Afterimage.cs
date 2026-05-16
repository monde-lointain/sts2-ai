using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Afterimage</c>: 1 energy Power. Applies 1 Afterimage. Upgrade adds Innate (keyword).
/// </summary>
public sealed class Afterimage : CardModel
{
    public const string CanonicalId = "Afterimage";
    public const int Amount = 1;

    public Afterimage()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Afterimage, Amount, null));
    }
    // Upgrade adds Innate keyword (deferred to S13 — keyword surface not modelled in S12).
}
