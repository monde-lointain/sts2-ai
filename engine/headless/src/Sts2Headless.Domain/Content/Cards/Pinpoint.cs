using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Pinpoint</c>: 3 energy Attack. 15 dmg. Upgrade: +4.
/// </summary>
public sealed class Pinpoint : CardModel
{
    public const string CanonicalId = "Pinpoint";
    public const int BaseDamage = 15;
    public const int UpgradeDelta = 4;
    public int Damage => BaseDamage;

    public Pinpoint() : base(CanonicalId, 3, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
