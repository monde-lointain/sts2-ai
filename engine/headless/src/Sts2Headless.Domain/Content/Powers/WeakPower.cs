using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.WeakPower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/WeakPower.cs):
/// counter-type debuff. Catalog metadata only; <see cref="DamageDecrease"/> is read
/// directly by combat-side damage-modifier code.
/// </summary>
public sealed class WeakPower : PowerModel
{
    /// <summary>
    /// Upstream <c>DynamicVar("DamageDecrease", 0.75m)</c>. Read by combat-side
    /// damage-modifier code (e.g. <c>EffectDispatcher.ApplyDamageModifiers</c>).
    /// </summary>
    public const decimal DamageDecrease = 0.75m;

    public WeakPower()
        : base(PowerIds.Weak, PowerType.Debuff, PowerStackType.Counter) { }
}
