using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.AscendersBane</c>: Unplayable Curse. Ethereal.
/// </summary>
public sealed class AscendersBane : CardModel
{
    public const string CanonicalId = "AscendersBane";

    public AscendersBane() : base(CanonicalId, -1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
