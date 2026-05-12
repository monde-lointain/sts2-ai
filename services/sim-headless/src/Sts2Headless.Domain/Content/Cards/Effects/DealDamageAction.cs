using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Deal <see cref="Amount"/> damage to <see cref="Target"/>" — the upstream
/// <c>DamageCmd.Attack(amount).Targeting(target)</c> reduced to a card-side payload.
/// Concrete damage resolution (block subtraction, on-take-damage hooks, vulnerable /
/// weak multipliers, strength additive) is S6 Combat Domain territory; this action
/// just records intent so the smoke-content tests can assert byte-exact card values
/// without depending on combat infrastructure that does not exist yet.
///
/// <para>
/// S6 will replace <see cref="Execute"/> with the real damage pipeline; the
/// constructor and field shape is the stable card-side contract. The current Execute
/// is intentionally a no-op so S5 tests that drain the action queue see the action
/// arrive without errors.
/// </para>
/// </summary>
public sealed record DealDamageAction(int Amount, string? Target) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}
