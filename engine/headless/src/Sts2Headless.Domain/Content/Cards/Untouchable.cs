using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Untouchable</c>: 2 energy Skill (Sly). 6 block. Upgrade: +3.
/// </summary>
// UPSTREAM_REF: src/Core/Models/Cards/Untouchable.cs:31 — upgrade Block +2 → +3
public sealed class Untouchable : CardModel
{
    public const string CanonicalId = "Untouchable";
    public const int BaseBlock = 6;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;

    public Untouchable()
        : base(CanonicalId, 2, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}
