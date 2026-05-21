using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.PoisonedStab</c>: 1 energy Attack. 6 dmg + 3 Poison. Upgrade: +2 dmg, +1 poison.
/// </summary>
public sealed class PoisonedStab : CardModel
{
    public const string CanonicalId = "PoisonedStab";
    public const int BaseDamage = 6;
    public const int BasePoison = 3;
    public const int UpgradeDeltaDamage = 2;
    public const int UpgradeDeltaPoison = 1;
    public int Damage => BaseDamage;
    public int Poison => BasePoison;

    public PoisonedStab()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target));
    }
}
