using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Heal <see cref="Amount"/> HP on the player" — upstream
/// <c>CreatureCmd.Heal(player.Creature, amount)</c>. Used by BloodVial (heal 2 at
/// player turn start, round 1) and BurningBlood-style relics. See
/// <see cref="DealDamageAction"/> for why Execute is a no-op in S5.
/// </summary>
public sealed record HealAction(int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        ctx.Observer?.Record(this);
    }
}
