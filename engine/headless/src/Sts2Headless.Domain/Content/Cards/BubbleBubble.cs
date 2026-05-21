using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.BubbleBubble</c>: 1 energy Skill. 9 Poison. Upgrade: +3.
/// </summary>
public sealed class BubbleBubble : CardModel
{
    public const string CanonicalId = "BubbleBubble";
    public const int BasePoison = 9;
    public const int UpgradeDelta = 3;
    public int Poison => BasePoison;

    public BubbleBubble()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target));
    }
}
