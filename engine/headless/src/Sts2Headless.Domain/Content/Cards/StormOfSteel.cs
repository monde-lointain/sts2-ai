using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.StormOfSteel</c>: 1 energy Skill. Discard all + add 1 Shiv per card discarded.
/// No upgrade body.
/// </summary>
public sealed class StormOfSteel : CardModel
{
    public const string CanonicalId = "StormOfSteel";

    public StormOfSteel() : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Discard-hand semantics — combat-state dependent. Smoke records nothing.
    }
}
