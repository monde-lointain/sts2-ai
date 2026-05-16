using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Injury</c>: Unplayable Curse.
/// </summary>
public sealed class Injury : CardModel
{
    public const string CanonicalId = "Injury";

    public Injury()
        : base(CanonicalId, -1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
