using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Predator</c>: 2 energy Attack. 15 dmg + draw 2 next turn. Upgrade: +5 dmg.
/// </summary>
public sealed class Predator : CardModel
{
    public const string CanonicalId = "Predator";
    public const int BaseDamage = 15;
    public const int UpgradeDelta = 5;
    public int Damage => BaseDamage;

    public Predator()
        : base(CanonicalId, 2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
