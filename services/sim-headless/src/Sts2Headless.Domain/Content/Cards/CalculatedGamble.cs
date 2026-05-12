using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.CalculatedGamble</c>: 0 energy Skill. Discard hand, draw same number + Exhaust.
/// Upgrade adds Retain (keyword).
/// </summary>
public sealed class CalculatedGamble : CardModel
{
    public const string CanonicalId = "CalculatedGamble";

    public CalculatedGamble() : base(CanonicalId, 0, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Discard hand + draw same — combat-state dependent; smoke records no-op.
    }
    // Upgrade is keyword-only (deferred to S13).
}
