using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.Accelerant</c>:
/// 1 energy Power. Applies 1 Accelerant. Upgrade: +1.
/// </summary>
public sealed class Accelerant : CardModel
{
    public const string CanonicalId = "Accelerant";
    public const int BaseAmount = 1;
    public const int UpgradeDelta = 1;
    public int Amount => BaseAmount;

    public Accelerant()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Accelerant, BaseAmount, null));
    }
}
