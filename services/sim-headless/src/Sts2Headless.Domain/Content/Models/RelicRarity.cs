namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Relic rarity. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Relics.RelicRarity</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Relics/RelicRarity.cs).
/// </summary>
public enum RelicRarity
{
    None = 0,
    Starter,
    Common,
    Uncommon,
    Rare,
    Shop,
    Event,
    Ancient,
}
