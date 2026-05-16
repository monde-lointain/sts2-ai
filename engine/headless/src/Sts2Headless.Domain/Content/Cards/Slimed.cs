using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Slimed</c>: 1 energy Status, Exhaust. No effect.
/// </summary>
public sealed class Slimed : CardModel
{
    public const string CanonicalId = "Slimed";

    public Slimed()
        : base(CanonicalId, 1, CardType.Status, CardRarity.Status, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
