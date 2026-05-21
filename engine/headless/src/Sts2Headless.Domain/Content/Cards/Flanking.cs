using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Flanking</c>: 2 energy Skill. Make target take +50% damage this turn.
/// Upgrade: cost 1.
/// </summary>
public sealed class Flanking : CardModel
{
    public const string CanonicalId = "Flanking";
    public const int BaseCost = 2;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Flanking()
        : base(CanonicalId, 2, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Hook-only effect; smoke records nothing.
    }
}
