using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.VulnerablePower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/VulnerablePower.cs):
/// counter-type debuff. Catalog metadata only; <see cref="DamageIncrease"/> is read
/// directly by combat-side damage-modifier code.
/// </summary>
public sealed class VulnerablePower : PowerModel
{
    /// <summary>
    /// Upstream <c>DynamicVar("DamageIncrease", 1.5m)</c>. Read by combat-side
    /// damage-modifier code (e.g. <c>EffectDispatcher.ApplyDamageModifiers</c>).
    /// </summary>
    public const decimal DamageIncrease = 1.5m;

    public VulnerablePower() : base(PowerIds.Vulnerable, PowerType.Debuff, PowerStackType.Counter) { }
}
