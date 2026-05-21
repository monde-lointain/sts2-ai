using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Flechettes</c>: 1 energy Attack. 5 dmg per Skill in hand. Upgrade: +2.
/// </summary>
public sealed class Flechettes : CardModel
{
    public const string CanonicalId = "Flechettes";
    public const int BaseDamage = 5;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;

    public Flechettes()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
