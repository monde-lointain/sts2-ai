using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Suppress</c>: 0 energy Attack (Ancient). 11 dmg + 3 Weak. Upgrade: +6 dmg, +2 weak.
/// </summary>
public sealed class Suppress : CardModel
{
    public const string CanonicalId = "Suppress";
    public const int BaseDamage = 11;
    public const int BaseWeak = 3;
    public const int UpgradeDeltaDamage = 6;
    public const int UpgradeDeltaWeak = 2;
    public int Damage => BaseDamage;
    public int Weak => BaseWeak;

    public Suppress() : base(CanonicalId, 0, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target));
    }
}
