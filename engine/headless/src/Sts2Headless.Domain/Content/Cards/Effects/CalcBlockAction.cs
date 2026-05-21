using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Gain block equal to <see cref="BaseBlock"/> + <see cref="MultiplierKey"/>'s
/// resolved value" — Stream-B-T4 surface for calc-block cards (Mirage) where
/// the block scales with a CombatState aggregate.
///
/// <para>
/// <b>Multiplier-key vocabulary (block variants):</b>
/// </para>
/// <list type="bullet">
///   <item><c>poison_total_on_enemies</c> — Mirage.</item>
/// </list>
/// </summary>
public sealed record CalcBlockAction(int BaseBlock, string MultiplierKey) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        ctx.Observer?.Record(this);
    }
}
