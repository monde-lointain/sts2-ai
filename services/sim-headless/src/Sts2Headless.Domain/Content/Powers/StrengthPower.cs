using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.StrengthPower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/StrengthPower.cs):
/// counter-type buff. Catalog metadata only; the per-instance stack count lives on
/// the combat-side <c>PowerInstance</c>, and additive damage modification is
/// applied directly by <c>DamageModifier</c> / engine code reading those stacks.
/// </summary>
public sealed class StrengthPower : PowerModel
{
    public StrengthPower() : base(PowerIds.Strength, PowerType.Buff, PowerStackType.Counter) { }
}
