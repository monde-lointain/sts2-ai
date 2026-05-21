using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.Abrasive</c>:
/// 3 energy, Power, applies 4 Thorns to self. Upgrade adds 2 Thorns.
/// </summary>
public sealed class Abrasive : CardModel
{
    public const string CanonicalId = "Abrasive";
    public const int BaseThorns = 4;
    public const int UpgradeDelta = 2;
    public int Thorns => BaseThorns;

    public Abrasive()
        : base(CanonicalId, cost: 3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Thorns, BaseThorns, Target: null));
    }
}
