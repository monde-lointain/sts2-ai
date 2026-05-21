using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// B.1-gamma-T5: KnifeTrap-shape damage action. Deals
/// <see cref="BasePerShiv"/> damage per Shiv-tagged card currently in the
/// player's exhaust pile (tracked by
/// <see cref="Sts2Headless.Domain.Combat.TrailCounters.ExhaustedShivCount"/>).
/// Mirrors upstream <c>KnifeTrap.OnPlay</c>'s
/// <c>CardCmd.AutoPlay</c> loop, simplified to a single damage application
/// of <c>BasePerShiv * shivsInExhaust</c>. With zero Shivs the action
/// resolves to zero damage — KnifeTrap is in the catalog as metadata
/// regardless.
/// </summary>
public sealed record CalcDamageFromShivExhaustAction(int BasePerShiv, Sts2Headless.Domain.Combat.CreatureId? Target) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        ctx.Observer?.Record(this);
    }
}
