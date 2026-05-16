using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.ShadowStep</c>: 1 energy Skill. Draw 3. Upgrade: cost 0.
/// </summary>
public sealed class ShadowStep : CardModel
{
    public const string CanonicalId = "ShadowStep";
    public const int CardsDrawn = 3;
    public const int BaseCost = 1;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public ShadowStep()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(CardsDrawn));
    }
}
