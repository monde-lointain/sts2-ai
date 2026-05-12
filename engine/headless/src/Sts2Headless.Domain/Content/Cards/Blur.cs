using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Blur</c>: 1 energy Skill. 5 block + 1 Blur. Upgrade: +3 block.
/// </summary>
public sealed class Blur : CardModel
{
    public const string CanonicalId = "Blur";
    public const int BaseBlock = 5;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;
    public const int BlurStacks = 1;

    public Blur() : base(CanonicalId, 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}
