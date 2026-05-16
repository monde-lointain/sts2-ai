using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Potions;

/// <summary>
/// Bulk-port of Phase-1 combat potions from upstream
/// <c>~/development/projects/godot/sts2/src/Core/Models/Potions/*.cs</c>. Each
/// captures id + name + rarity per upstream's <c>PotionModel.Rarity</c> override.
/// Use-effects (Apply X, deal Y damage) wire in S13 once the consumable queue
/// is available.
/// </summary>
public sealed class BlockPotion : PotionModel
{
    public const string CanonicalId = "BlockPotion";
    public const int BlockAmount = 12;

    public BlockPotion()
        : base(CanonicalId, "Block Potion", PotionRarity.Common) { }
}

public sealed class FirePotion : PotionModel
{
    public const string CanonicalId = "FirePotion";
    public const int DamageAmount = 20;

    public FirePotion()
        : base(CanonicalId, "Fire Potion", PotionRarity.Common) { }
}

public sealed class EnergyPotion : PotionModel
{
    public const string CanonicalId = "EnergyPotion";
    public const int EnergyAmount = 2;

    public EnergyPotion()
        : base(CanonicalId, "Energy Potion", PotionRarity.Common) { }
}

public sealed class ExplosiveAmpoule : PotionModel
{
    public const string CanonicalId = "ExplosiveAmpoule";
    public const int DamageAmount = 10;

    public ExplosiveAmpoule()
        : base(CanonicalId, "Explosive Ampoule", PotionRarity.Common) { }
}

public sealed class FlexPotion : PotionModel
{
    public const string CanonicalId = "FlexPotion";
    public const int StrengthAmount = 5;

    public FlexPotion()
        : base(CanonicalId, "Flex Potion", PotionRarity.Common) { }
}

public sealed class DexterityPotion : PotionModel
{
    public const string CanonicalId = "DexterityPotion";
    public const int DexterityAmount = 2;

    public DexterityPotion()
        : base(CanonicalId, "Dexterity Potion", PotionRarity.Common) { }
}

public sealed class StrengthPotion : PotionModel
{
    public const string CanonicalId = "StrengthPotion";
    public const int StrengthAmount = 2;

    public StrengthPotion()
        : base(CanonicalId, "Strength Potion", PotionRarity.Common) { }
}

public sealed class SkillPotion : PotionModel
{
    public const string CanonicalId = "SkillPotion";
    public const int CardChoices = 3;

    public SkillPotion()
        : base(CanonicalId, "Skill Potion", PotionRarity.Common) { }
}

public sealed class AttackPotion : PotionModel
{
    public const string CanonicalId = "AttackPotion";
    public const int CardChoices = 3;

    public AttackPotion()
        : base(CanonicalId, "Attack Potion", PotionRarity.Common) { }
}

public sealed class PowerPotion : PotionModel
{
    public const string CanonicalId = "PowerPotion";
    public const int CardChoices = 3;

    public PowerPotion()
        : base(CanonicalId, "Power Potion", PotionRarity.Common) { }
}

public sealed class SwiftPotion : PotionModel
{
    public const string CanonicalId = "SwiftPotion";
    public const int CardsDrawn = 3;

    public SwiftPotion()
        : base(CanonicalId, "Swift Potion", PotionRarity.Common) { }
}

public sealed class BloodPotion : PotionModel
{
    public const string CanonicalId = "BloodPotion";
    public const int HpPercentage = 25;

    public BloodPotion()
        : base(CanonicalId, "Blood Potion", PotionRarity.Common) { }
}

public sealed class PoisonPotion : PotionModel
{
    public const string CanonicalId = "PoisonPotion";
    public const int PoisonAmount = 6;

    public PoisonPotion()
        : base(CanonicalId, "Poison Potion", PotionRarity.Common) { }
}

public sealed class FocusPotion : PotionModel
{
    public const string CanonicalId = "FocusPotion";
    public const int FocusAmount = 2;

    public FocusPotion()
        : base(CanonicalId, "Focus Potion", PotionRarity.Common) { }
}

public sealed class CunningPotion : PotionModel
{
    public const string CanonicalId = "CunningPotion";
    public const int ShivsAmount = 3;

    public CunningPotion()
        : base(CanonicalId, "Cunning Potion", PotionRarity.Common) { }
}

// === Uncommon ===
public sealed class LiquidBronze : PotionModel
{
    public const string CanonicalId = "LiquidBronze";
    public const int ThornsAmount = 3;

    public LiquidBronze()
        : base(CanonicalId, "Liquid Bronze", PotionRarity.Uncommon) { }
}

public sealed class GamblersBrew : PotionModel
{
    public const string CanonicalId = "GamblersBrew";

    public GamblersBrew()
        : base(CanonicalId, "Gambler's Brew", PotionRarity.Uncommon) { }
}

public sealed class HeartOfIron : PotionModel
{
    public const string CanonicalId = "HeartOfIron";
    public const int MetallicizeAmount = 6;

    public HeartOfIron()
        : base(CanonicalId, "Heart of Iron", PotionRarity.Uncommon) { }
}

// === Rare ===
public sealed class FairyInABottle : PotionModel
{
    public const string CanonicalId = "FairyInABottle";
    public const int HealPercent = 40;

    public FairyInABottle()
        : base(CanonicalId, "Fairy in a Bottle", PotionRarity.Rare) { }
}

public sealed class LiquidMemories : PotionModel
{
    public const string CanonicalId = "LiquidMemories";
    public const int CardChoices = 1;

    public LiquidMemories()
        : base(CanonicalId, "Liquid Memories", PotionRarity.Rare) { }
}

public sealed class EntropicBrew : PotionModel
{
    public const string CanonicalId = "EntropicBrew";

    public EntropicBrew()
        : base(CanonicalId, "Entropic Brew", PotionRarity.Rare) { }
}
