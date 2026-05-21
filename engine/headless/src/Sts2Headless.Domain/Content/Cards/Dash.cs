using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Dash</c>: 2 energy Attack. 10 dmg + 10 block. Upgrade: +3 dmg, +3 block.
/// </summary>
public sealed class Dash : CardModel
{
    public const string CanonicalId = "Dash";
    public const int BaseDamage = 10;
    public const int BaseBlock = 10;
    public const int UpgradeDeltaDamage = 3;
    public const int UpgradeDeltaBlock = 3;
    public int Damage => BaseDamage;
    public int Block => BaseBlock;

    public Dash()
        : base(CanonicalId, 2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
