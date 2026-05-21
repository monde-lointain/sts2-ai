using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Prepared</c>: 0 energy Skill. Draw 1, discard 1. Upgrade: +1.
/// </summary>
public sealed class Prepared : CardModel
{
    public const string CanonicalId = "Prepared";
    public const int BaseCards = 1;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public Prepared()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
        ctx.Queue.Enqueue(new DiscardCardsAction(BaseCards));
    }
}
