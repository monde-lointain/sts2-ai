using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// B.1-gamma-T5: X-cost attack action. Deals
/// <see cref="DamagePerHit"/> damage <c>X</c> times, where <c>X</c> is the
/// most recently snapshotted spent energy (from
/// <see cref="Sts2Headless.Domain.Combat.CombatState.LastSpentEnergy"/>).
/// Mirrors upstream <c>Skewer.OnPlay</c>:
/// <c>DamageCmd.Attack(BaseDamage).WithHitCount(ResolveEnergyXValue())</c>.
/// </summary>
public sealed record XCostDamageAction(int DamagePerHit, string? Target) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        ctx.Observer?.Record(this);
    }
}
