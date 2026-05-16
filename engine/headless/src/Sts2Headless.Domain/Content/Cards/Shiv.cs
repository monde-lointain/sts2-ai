using System.Collections.Generic;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Shiv</c>: 0 energy Attack (Token, Exhaust). 4 dmg. No upgrade.
/// </summary>
public sealed class Shiv : CardModel
{
    public const string CanonicalId = "Shiv";
    public const int Damage = 4;

    public Shiv()
        : base(CanonicalId, 0, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy) { }

    protected override void DeclareTags(HashSet<CardTag> tags) => tags.Add(CardTag.Shiv);

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(Damage, target));
    }
}
