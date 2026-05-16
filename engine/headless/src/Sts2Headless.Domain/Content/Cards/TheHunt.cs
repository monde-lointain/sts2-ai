using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.TheHunt</c>: 1 energy Attack + Exhaust. 10 dmg, fatal pick. Upgrade: +5 dmg.
/// </summary>
public sealed class TheHunt : CardModel
{
    public const string CanonicalId = "TheHunt";
    public const int BaseDamage = 10;
    public const int UpgradeDelta = 5;
    public int Damage => BaseDamage;

    public TheHunt()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
