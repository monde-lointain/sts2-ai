using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Expertise</c>: 1 energy Skill. Draw 6 (capped at hand limit). Upgrade: +1.
/// </summary>
public sealed class Expertise : CardModel
{
    public const string CanonicalId = "Expertise";
    public const int BaseCards = 6;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public Expertise()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
    }
}
