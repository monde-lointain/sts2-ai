using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Outbreak</c>: 1 energy Power. Apply 11 Outbreak. Upgrade: +4.
/// </summary>
public sealed class Outbreak : CardModel
{
    public const string CanonicalId = "Outbreak";
    public const int BaseAmount = 11;
    public const int UpgradeDelta = 4;
    public int Amount => BaseAmount;
    public const int Repeat = 3;

    public Outbreak() : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Outbreak, BaseAmount, null));
    }
}
