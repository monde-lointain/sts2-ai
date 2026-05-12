using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Tactician</c>: 3 energy Skill (Sly). Gain 1 energy on discard. Upgrade: +1 energy.
/// </summary>
public sealed class Tactician : CardModel
{
    public const string CanonicalId = "Tactician";
    public const int BaseEnergy = 1;
    public const int UpgradeDelta = 1;
    public int EnergyGain => BaseEnergy;

    public Tactician() : base(CanonicalId, 3, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Sly — energy gained on discard, not on play. Smoke records nothing.
    }
}
