namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// How a power's stack count behaves on re-apply. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Powers.PowerStackType</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Powers/PowerStackType.cs).
///
/// <list type="bullet">
///   <item><b>Counter</b> — new application is additive to existing amount; expires when
///         amount reaches zero (e.g., Poison, Vulnerable, Weak, Strength).</item>
///   <item><b>Single</b> — application is idempotent; amount tracks single-trigger state
///         (e.g., a one-shot proc flag).</item>
///   <item><b>None</b> — sentinel only; concrete powers must pick Counter or Single.</item>
/// </list>
/// </summary>
public enum PowerStackType
{
    None = 0,

    // CA1720: 'Single' shadows the float-type name. Domain terminology wins;
    // the enum member is unambiguous in context.
#pragma warning disable CA1720
    Counter,
    Single,
#pragma warning restore CA1720
}
