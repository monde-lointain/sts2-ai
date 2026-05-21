using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Backstab</c>: 0 energy Attack, Innate + Exhaust. 11 dmg.
/// Upgrade: +4 dmg.
/// </summary>
public sealed class Backstab : CardModel
{
    public const string CanonicalId = "Backstab";
    public const int BaseDamage = 11;
    public const int UpgradeDelta = 4;
    public int Damage => BaseDamage;

    public Backstab()
        : base(CanonicalId, 0, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
