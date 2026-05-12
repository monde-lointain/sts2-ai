using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Silent's basic block-and-discard. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Cards.Survivor</c>: 1 energy, 8 block + discard 1
/// from hand. Upgrade adds 3 block.
/// </summary>
public sealed class Survivor : CardModel
{
    public const string CanonicalId = "Survivor";

    public const int BaseBlock = 8;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;

    public Survivor()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Basic, TargetType.Self)
    { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
        ctx.Queue.Enqueue(new DiscardCardsAction(Count: 1));
    }
}
