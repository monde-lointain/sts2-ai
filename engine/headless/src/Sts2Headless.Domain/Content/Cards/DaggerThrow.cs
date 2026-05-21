using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.DaggerThrow</c>: 1 energy Attack. 9 dmg + draw 1 + discard 1. Upgrade: +3 dmg.
/// </summary>
public sealed class DaggerThrow : CardModel
{
    public const string CanonicalId = "DaggerThrow";
    public const int BaseDamage = 9;
    public const int UpgradeDelta = 3;
    public int Damage => BaseDamage;

    public DaggerThrow()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new DrawCardsAction(1));
        ctx.Queue.Enqueue(new DiscardCardsAction(1));
    }
}
