using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Burn</c>: Unplayable Status. Take 2 dmg at turn end. Upgrade: 4 dmg.
/// </summary>
public sealed class Burn : CardModel
{
    public const string CanonicalId = "Burn";
    public const int BaseDamage = 2;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;

    public Burn() : base(CanonicalId, -1, CardType.Status, CardRarity.Status, TargetType.None) { }

    public override void OnPlay(ExecutionContext ctx, string? target) { }
}
