using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.MasterPlanner</c>: 2 energy Power. Cards retained start of next turn cost 0.
/// Upgrade: cost 1.
/// </summary>
public sealed class MasterPlanner : CardModel
{
    public const string CanonicalId = "MasterPlanner";
    public const int BaseCost = 2;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public MasterPlanner() : base(CanonicalId, 2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.MasterPlanner, 1, null));
    }
}
