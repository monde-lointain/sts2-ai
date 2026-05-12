namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Card target type. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Cards.TargetType</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Cards/TargetType.cs).
/// </summary>
public enum TargetType
{
    None = 0,
    Self,
    AnyEnemy,
    AllEnemies,
    RandomEnemy,
    AnyPlayer,
    AnyAlly,
    AllAllies,
    TargetedNoCreature,
    Osty,
}
