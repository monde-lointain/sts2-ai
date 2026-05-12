using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.RitualPower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/RitualPower.cs):
/// counter-type enemy buff. On enemy turn end (except the turn it was applied),
/// grants Strength equal to the per-instance Ritual stack count to the owner.
///
/// <para>
/// The "skip the turn it was applied" semantics is upstream's
/// <c>_wasJustAppliedByEnemy</c> flag. We surface it via
/// <see cref="MarkJustApplied"/> (called by content code on apply) and
/// <see cref="ShouldGrantStrengthThisTurnEnd"/>, a query that returns true and
/// clears the flag in one call.
/// </para>
/// </summary>
public sealed class RitualPower : PowerModel
{
    private bool _wasJustAppliedByEnemy;

    public RitualPower() : base(PowerIds.Ritual, PowerType.Buff, PowerStackType.Counter) { }

    /// <summary>
    /// Equivalent of upstream's <c>AfterApplied</c> override that sets
    /// <c>_wasJustAppliedByEnemy</c>. Callers (S6 combat code or the
    /// monster's apply hook) invoke this immediately after apply.
    /// </summary>
    public void MarkJustApplied() => _wasJustAppliedByEnemy = true;

    /// <summary>
    /// Equivalent of upstream's <c>AfterTurnEnd</c> guard: returns <c>true</c> if
    /// the owner's turn-end should grant Strength equal to the per-instance Ritual
    /// stack count to the owner; otherwise <c>false</c>. Calling this consumes the
    /// just-applied flag so subsequent turn ends will trigger.
    /// </summary>
    public bool ShouldGrantStrengthThisTurnEnd()
    {
        if (_wasJustAppliedByEnemy)
        {
            _wasJustAppliedByEnemy = false;
            return false;
        }
        return true;
    }
}
