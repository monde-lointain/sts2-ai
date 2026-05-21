using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.FlickFlack</c>: 1 energy Attack, AllEnemies. 6 dmg. Upgrade: +2.
/// </summary>
public sealed class FlickFlack : CardModel
{
    public const string CanonicalId = "FlickFlack";
    public const int BaseDamage = 6;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;

    public FlickFlack()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
