using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Deal damage equal to <see cref="BaseDamage"/> × <see cref="Multiplier"/>"
/// where the multiplier is evaluated against a <see cref="MultiplierKey"/>
/// known to the combat dispatcher. Stream-B-T4 surface for calc-damage cards
/// (Finisher, Murder) where the damage depends on a CombatState aggregate
/// counter.
///
/// <para>
/// <b>Multiplier-key vocabulary:</b>
/// </para>
/// <list type="bullet">
///   <item><c>attacks_played_this_turn</c> — Finisher.</item>
///   <item><c>cards_drawn_this_combat</c> — Murder.</item>
/// </list>
///
/// <para>
/// S5 layer is fenced from CombatState; the action records itself into the
/// per-context <see cref="Sts2Headless.Domain.Actions.IActionObserver"/>
/// (attached via <c>ExecutionContext.Observer</c>), and S6's
/// <c>EffectDispatcher.Apply</c> looks up the multiplier on the live state
/// per call.
/// </para>
/// </summary>
public sealed record CalcDamageAction(int BaseDamage, string MultiplierKey, Sts2Headless.Domain.Combat.CreatureId? Target)
    : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        ctx.Observer?.Record(this);
    }
}
