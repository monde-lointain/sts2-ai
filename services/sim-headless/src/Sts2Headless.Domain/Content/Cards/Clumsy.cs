using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Clumsy</c>: Unplayable Curse. Ethereal (exhausts EOT).
/// </summary>
public sealed class Clumsy : CardModel
{
    public const string CanonicalId = "Clumsy";

    public Clumsy() : base(CanonicalId, -1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
