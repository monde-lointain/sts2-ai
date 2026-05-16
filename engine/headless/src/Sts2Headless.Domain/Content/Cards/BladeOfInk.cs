using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.BladeOfInk</c>: 1 energy Skill. Draw 2 cards. Upgrade: +1 card.
/// </summary>
public sealed class BladeOfInk : CardModel
{
    public const string CanonicalId = "BladeOfInk";
    public const int BaseCards = 2;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public BladeOfInk()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
    }
}
