using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Shadowmeld</c>: 1 energy Skill. Become Intangible for 1 turn. Upgrade: cost 0.
/// </summary>
public sealed class Shadowmeld : CardModel
{
    public const string CanonicalId = "Shadowmeld";
    public const int IntangibleStacks = 1;
    public const int BaseCost = 1;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Shadowmeld()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Intangible application — power-application not yet differentiated in S5 effects. Smoke records nothing.
    }
}
