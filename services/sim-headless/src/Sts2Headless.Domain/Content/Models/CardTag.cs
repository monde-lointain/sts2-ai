namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Card tag (functional family marker). Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Cards.CardTag</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Cards/CardTag.cs).
/// </summary>
public enum CardTag
{
    None = 0,
    Strike,
    Defend,
    Minion,
    OstyAttack,
    Shiv,
}
