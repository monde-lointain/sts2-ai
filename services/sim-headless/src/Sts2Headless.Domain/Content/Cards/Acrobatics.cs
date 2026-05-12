using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.Acrobatics</c>:
/// 1 energy, draw 3 cards + discard 1. Upgrade adds 1 card drawn.
/// </summary>
public sealed class Acrobatics : CardModel
{
    public const string CanonicalId = "Acrobatics";

    public const int BaseCards = 3;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public Acrobatics()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
        ctx.Queue.Enqueue(new DiscardCardsAction(Count: 1));
    }
}
