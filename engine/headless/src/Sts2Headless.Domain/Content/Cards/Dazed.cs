using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Dazed</c>: Unplayable Status. Ethereal (exhausts EOT).
/// </summary>
public sealed class Dazed : CardModel
{
    public const string CanonicalId = "Dazed";

    public Dazed()
        : base(CanonicalId, -1, CardType.Status, CardRarity.Status, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target) { }
}
