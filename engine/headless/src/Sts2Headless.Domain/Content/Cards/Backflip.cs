using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.Backflip</c>:
/// 1 energy, 5 block + draw 2 cards. Upgrade adds 3 block (cards stays 2).
/// </summary>
public sealed class Backflip : CardModel
{
    public const string CanonicalId = "Backflip";

    public const int BaseBlock = 5;
    public const int BaseCards = 2;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;
    public int Cards => BaseCards;

    public Backflip()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
    }
}
