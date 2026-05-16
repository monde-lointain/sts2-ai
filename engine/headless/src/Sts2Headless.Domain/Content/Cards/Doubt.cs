using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Doubt</c>: Unplayable Curse. Gain 1 Weak at turn end while in hand.
/// </summary>
public sealed class Doubt : CardModel
{
    public const string CanonicalId = "Doubt";
    public const int WeakStacks = 1;

    public Doubt()
        : base(CanonicalId, -1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
