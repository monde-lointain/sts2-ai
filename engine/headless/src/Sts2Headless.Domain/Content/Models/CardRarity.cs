namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Card rarity. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Entities.Cards.CardRarity</c>
/// (~/development/projects/godot/sts2/src/Core/Entities/Cards/CardRarity.cs).
/// </summary>
public enum CardRarity
{
    None = 0,
    Basic,
    Common,
    Uncommon,
    Rare,
    Ancient,
    Event,
    Token,
    Status,
    Curse,
    Quest,
}
