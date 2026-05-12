using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.ToolsOfTheTrade</c>: 1 energy Power. Start of turn, draw 1 + discard 1.
/// Upgrade: cost 0.
/// </summary>
public sealed class ToolsOfTheTrade : CardModel
{
    public const string CanonicalId = "ToolsOfTheTrade";
    public const int BaseCost = 1;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public ToolsOfTheTrade() : base(CanonicalId, 1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.ToolsOfTheTrade, 1, null));
    }
}
