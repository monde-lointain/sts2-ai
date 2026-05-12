using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Apply <see cref="Amount"/> stacks of <see cref="PowerId"/> to <see cref="Target"/>"
/// — upstream <c>PowerCmd.Apply&lt;TPower&gt;(target, amount, applier, source)</c>.
/// PowerId is a string id matching <see cref="PowerCatalog"/>; null target means
/// "apply to self" (the playing player). See <see cref="DealDamageAction"/> for why
/// Execute is a no-op in S5.
/// </summary>
public sealed record ApplyPowerAction(string PowerId, int Amount, string? Target) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}
