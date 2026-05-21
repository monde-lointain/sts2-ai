using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Reflex</c>: 3 energy Skill (Unplayable normally; Sly). Draw 2 when discarded.
/// Upgrade: +1.
/// </summary>
public sealed class Reflex : CardModel
{
    public const string CanonicalId = "Reflex";
    public const int BaseCards = 2;
    public const int UpgradeDelta = 1;
    public int Cards => BaseCards;

    public Reflex()
        : base(CanonicalId, 3, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Sly card — draws on discard, not on play. Smoke records nothing on play.
    }
}
