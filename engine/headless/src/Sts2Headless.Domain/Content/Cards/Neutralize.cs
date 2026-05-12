using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Silent's basic shiv. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Cards.Neutralize</c>: 0 energy, 3 damage + 1 Weak,
/// upgrade adds 1 damage and 1 Weak.
/// </summary>
public sealed class Neutralize : CardModel
{
    public const string CanonicalId = "Neutralize";

    public const int BaseDamage = 3;
    public const int BaseWeak = 1;
    public const int UpgradeDeltaDamage = 1;
    public const int UpgradeDeltaWeak = 1;
    public int Damage => BaseDamage;
    public int Weak => BaseWeak;

    public Neutralize()
        : base(CanonicalId, cost: 0, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target));
    }
}
