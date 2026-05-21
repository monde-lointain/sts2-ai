using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Apply <see cref="Amount"/> stacks of <see cref="PowerId"/> to every living
/// enemy" — upstream <c>PowerCmd.Apply&lt;TPower&gt;(combatState.HittableEnemies,
/// amount, applier, source)</c>. Stream-B-T2 surface for relics like
/// BagOfMarbles that fan out to the full enemy list.
///
/// <para>
/// S5 layer is fenced from CombatState; the action records itself into the
/// thread-static <see cref="EffectObserver"/> log, and S6's
/// <c>EffectDispatcher.Apply</c> walks the live enemy list per call.
/// </para>
/// </summary>
public sealed record ApplyPowerToAllEnemiesAction(string PowerId, int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        ctx.Observer?.Record(this);
    }
}
