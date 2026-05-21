using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.FollowThrough</c>: 1 energy Attack. 7 dmg. Reduces own cost based on card count.
/// Upgrade: +2 dmg.
/// </summary>
public sealed class FollowThrough : CardModel
{
    public const string CanonicalId = "FollowThrough";
    public const int BaseDamage = 7;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;
    public const int CardCount = 5;

    public FollowThrough()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
