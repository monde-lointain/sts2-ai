using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Deal <see cref="Amount"/> damage to every living enemy" — upstream
/// <c>DamageCmd.Damage(choiceContext, hittableEnemies, amount, applier)</c>.
/// Used by relics like MercuryHourglass (3 dmg to all enemies on player turn
/// start) and Orichalcum-shaped AoE effects. The damage path runs through the
/// engine's damage-modifier pipeline (Vulnerable/Strength etc.) per target.
/// </summary>
public sealed record DealDamageToAllEnemiesAction(int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}
