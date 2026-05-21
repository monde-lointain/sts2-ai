using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Pounce</c>: 2 energy Attack. 12 dmg. Upgrade: +6.
/// </summary>
public sealed class Pounce : CardModel
{
    public const string CanonicalId = "Pounce";
    public const int BaseDamage = 12;
    public const int UpgradeDelta = 6;
    public int Damage => BaseDamage;

    public Pounce()
        : base(CanonicalId, 2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
