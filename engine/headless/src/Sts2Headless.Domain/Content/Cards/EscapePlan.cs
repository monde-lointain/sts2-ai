using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.EscapePlan</c>: 0 energy Skill. Draw 1; if Skill drawn, gain 3 block.
/// Upgrade: +2 block. Conditional rider deferred to S13.
/// </summary>
public sealed class EscapePlan : CardModel
{
    public const string CanonicalId = "EscapePlan";
    public const int BaseBlock = 3;
    public const int UpgradeDelta = 2;
    public int Block => BaseBlock;

    public EscapePlan()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DrawCardsAction(1));
    }
}
