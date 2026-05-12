namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Card type. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Cards.CardType</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Cards/CardType.cs).
/// Order matters — these become token-mappable integer ids per Q1-ADR-005.
/// </summary>
public enum CardType
{
    None = 0,
    Attack,
    Skill,
    Power,
    Status,
    Curse,
    Quest,
}
