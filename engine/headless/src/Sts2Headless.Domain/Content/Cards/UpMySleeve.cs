using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.UpMySleeve</c>: 2 energy Skill. Draw 3; exhaust 3rd time played. Upgrade: +1 draw.
/// </summary>
public sealed class UpMySleeve : CardModel
{
    public const string CanonicalId = "UpMySleeve";
    public const int BaseCards = 3;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public UpMySleeve() : base(CanonicalId, 2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(BaseCards));
    }
}
