namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Power type (buff vs debuff). Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Powers.PowerType</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Powers/PowerType.cs).
/// </summary>
public enum PowerType
{
    None = 0,
    Buff,
    Debuff,
}
