using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.HiddenDaggers</c>: 0 energy Skill. Draw 2 + create 2 Shivs.
/// Upgrade modifies shiv-upgrade behavior (deferred).
/// </summary>
public sealed class HiddenDaggers : CardModel
{
    public const string CanonicalId = "HiddenDaggers";
    public const int Cards = 2;
    public const int Shivs = 2;

    public HiddenDaggers()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(Cards + Shivs));
    }
}
