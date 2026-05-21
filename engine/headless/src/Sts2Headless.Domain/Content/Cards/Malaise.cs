using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>Cards.Malaise</c>: X energy Skill, Exhaust —
/// lose X Strength + apply X Weak on target. Upgrade bumps X by 1 for both.
/// Engine snapshots LastSpentEnergy for X-cost cards (see <see cref="Skewer"/>).
/// </summary>
public sealed class Malaise : CardModel
{
    public const string CanonicalId = "Malaise";

    /// <summary>Upgrade adds 1 to both Strength-down and Weak.</summary>
    public const int UpgradeDelta = 1;

    public Malaise()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy) { }

    /// <inheritdoc />
    public override bool IsXCost => true;

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // -X StrengthPower stacks (Q1 uses StrengthDown power id for the
        // negative-strength application — same effect, separate power model
        // for legibility).
        ctx.Queue.Enqueue(
            new XCostApplyPowerAction(
                PowerId: PowerIds.Strength,
                SignMultiplier: -1,
                Target: target
            )
        );
        // +X Weak stacks.
        ctx.Queue.Enqueue(
            new XCostApplyPowerAction(PowerId: PowerIds.Weak, SignMultiplier: +1, Target: target)
        );
    }
}
