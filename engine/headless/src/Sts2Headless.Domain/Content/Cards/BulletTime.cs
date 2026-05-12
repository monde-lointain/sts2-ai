using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.BulletTime</c>: 3 energy Skill. Make all cards in hand cost 0 this turn.
/// Upgrade: cost 2. Cost-down modelled as a no-op enqueue (S6+ replaces with hand-cost hook).
/// </summary>
public sealed class BulletTime : CardModel
{
    public const string CanonicalId = "BulletTime";
    public const int BaseEnergyCost = 3;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseEnergyCost;

    public BulletTime() : base(CanonicalId, 3, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Modify-hand-cost is a hook-only effect; smoke records nothing (intentional).
    }
}
