using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Snakebite</c>: 2 energy Skill. 7 Poison + Retain. Upgrade: +3.
/// </summary>
public sealed class Snakebite : CardModel
{
    public const string CanonicalId = "Snakebite";
    public const int BasePoison = 7;
    public const int UpgradeDelta = 3;
    public int Poison => BasePoison;

    public Snakebite()
        : base(CanonicalId, 2, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target));
    }
}
