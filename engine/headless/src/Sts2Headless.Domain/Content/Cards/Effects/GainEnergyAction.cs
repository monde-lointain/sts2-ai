using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Gain <see cref="Amount"/> energy" — upstream <c>EnergyCmd.Gain(amount)</c>. Used by
/// Adrenaline et al. See <see cref="DealDamageAction"/> for why Execute is a no-op in
/// S5/S12.
/// </summary>
public sealed record GainEnergyAction(int Amount) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        EffectObserver.Record(this);
    }
}
