using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Regret</c>: Unplayable Curse. Lose HP equal to hand size at turn end.
/// </summary>
public sealed class Regret : CardModel
{
    public const string CanonicalId = "Regret";

    public Regret()
        : base(CanonicalId, -1, CardType.Curse, CardRarity.Curse, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
