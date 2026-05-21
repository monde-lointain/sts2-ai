using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Assassinate</c>: 0 energy Attack. 10 dmg + 1 Vulnerable + Exhaust.
/// Upgrade: +3 dmg, +1 Vulnerable.
/// </summary>
public sealed class Assassinate : CardModel
{
    public const string CanonicalId = "Assassinate";
    public const int BaseDamage = 10;
    public const int BaseVuln = 1;
    public const int UpgradeDeltaDamage = 3;
    public const int UpgradeDeltaVuln = 1;
    public int Damage => BaseDamage;
    public int Vulnerable => BaseVuln;

    public Assassinate()
        : base(CanonicalId, 0, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Vulnerable, BaseVuln, target));
    }
}
