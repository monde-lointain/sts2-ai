using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Expose</c>: 0 energy Skill + Exhaust. Apply 2 Vulnerable. Upgrade: +1.
/// </summary>
public sealed class Expose : CardModel
{
    public const string CanonicalId = "Expose";
    public const int BasePower = 2;
    public const int UpgradeDelta = 1;
    public int Power => BasePower;

    public Expose() : base(CanonicalId, 0, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Vulnerable, BasePower, target));
    }
}
