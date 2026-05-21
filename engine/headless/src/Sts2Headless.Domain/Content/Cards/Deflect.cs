using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Deflect</c>: 0 energy Skill. 4 block. Upgrade: +3.
/// </summary>
public sealed class Deflect : CardModel
{
    public const string CanonicalId = "Deflect";
    public const int BaseBlock = 4;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;

    public Deflect()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}
