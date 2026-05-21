using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.Slice</c>: 0 energy,
/// 6 damage common attack. Upgrade adds 3 damage.
/// </summary>
public sealed class Slice : CardModel
{
    public const string CanonicalId = "Slice";

    public const int BaseDamage = 6;
    public const int UpgradeDelta = 3;
    public int Damage => BaseDamage;

    public Slice()
        : base(CanonicalId, cost: 0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}
