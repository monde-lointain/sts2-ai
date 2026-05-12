using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.HandTrick</c>: 1 energy Skill. 7 block + Sly. Upgrade: +3 block.
/// </summary>
public sealed class HandTrick : CardModel
{
    public const string CanonicalId = "HandTrick";
    public const int BaseBlock = 7;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;

    public HandTrick() : base(CanonicalId, 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}
