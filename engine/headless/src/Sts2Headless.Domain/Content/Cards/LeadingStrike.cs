using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.LeadingStrike</c>: 1 energy Attack. 3 dmg + 2 Shivs. Upgrade: +3 dmg.
/// </summary>
public sealed class LeadingStrike : CardModel
{
    public const string CanonicalId = "LeadingStrike";
    public const int BaseDamage = 3;
    public const int UpgradeDelta = 3;
    public int Damage => BaseDamage;
    public const int Shivs = 2;

    public LeadingStrike() : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new DrawCardsAction(Shivs));
    }
}
