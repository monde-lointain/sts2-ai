using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.SuckerPunch</c>: 1 energy Attack. 8 dmg + 1 Weak. Upgrade: +2 dmg, +1 weak.
/// </summary>
public sealed class SuckerPunch : CardModel
{
    public const string CanonicalId = "SuckerPunch";
    public const int BaseDamage = 8;
    public const int BaseWeak = 1;
    public const int UpgradeDeltaDamage = 2;
    public const int UpgradeDeltaWeak = 1;
    public int Damage => BaseDamage;
    public int Weak => BaseWeak;

    public SuckerPunch()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target));
    }
}
