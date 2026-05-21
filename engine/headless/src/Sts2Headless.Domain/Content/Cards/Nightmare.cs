using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Nightmare</c>: 3 energy Skill. Choose card; add 3 copies to hand next turn.
/// Upgrade: cost 2.
/// </summary>
public sealed class Nightmare : CardModel
{
    public const string CanonicalId = "Nightmare";
    public const int BaseCost = 3;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Nightmare()
        : base(CanonicalId, 3, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Card-selection effect; combat-state dependent. Smoke records no-op.
    }
}
