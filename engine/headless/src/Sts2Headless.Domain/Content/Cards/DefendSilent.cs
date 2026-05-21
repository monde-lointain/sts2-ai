using System.Collections.Generic;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Silent's basic defend. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Cards.DefendSilent</c>: 1 energy, 5 block, +3 block on
/// upgrade. Tagged <c>Defend</c>.
/// </summary>
public sealed class DefendSilent : CardModel
{
    public const string CanonicalId = "DefendSilent";

    public const int BaseBlock = 5;
    public const int UpgradeDelta = 3;
    public int Block => BaseBlock;

    public DefendSilent()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override void DeclareTags(HashSet<CardTag> tags) => tags.Add(CardTag.Defend);

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}
