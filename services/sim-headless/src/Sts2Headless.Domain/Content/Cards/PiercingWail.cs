using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.PiercingWail</c>: 1 energy Skill, AllEnemies. Lose 6 Strength + Exhaust.
/// Upgrade: +2.
/// </summary>
public sealed class PiercingWail : CardModel
{
    public const string CanonicalId = "PiercingWail";
    public const int BaseStrengthLoss = 6;
    public const int UpgradeDelta = 2;
    public int StrengthLoss => BaseStrengthLoss;

    public PiercingWail() : base(CanonicalId, 1, CardType.Skill, CardRarity.Common, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Strength, -BaseStrengthLoss, target));
    }
}
