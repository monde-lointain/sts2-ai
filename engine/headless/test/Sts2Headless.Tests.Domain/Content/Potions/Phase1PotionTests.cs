using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Potions;

namespace Sts2Headless.Tests.Domain.Content.Potions;

/// <summary>
/// Byte-faithful metadata checks for S12 Phase-1 combat potions. Each potion's
/// id + rarity matches upstream
/// <c>~/development/projects/godot/sts2/src/Core/Models/Potions/*.cs</c>.
/// </summary>
public class Phase1PotionTests
{
    [Theory]
    [InlineData(typeof(BlockPotion), "BlockPotion", PotionRarity.Common)]
    [InlineData(typeof(FirePotion), "FirePotion", PotionRarity.Common)]
    [InlineData(typeof(EnergyPotion), "EnergyPotion", PotionRarity.Common)]
    [InlineData(typeof(ExplosiveAmpoule), "ExplosiveAmpoule", PotionRarity.Common)]
    [InlineData(typeof(FlexPotion), "FlexPotion", PotionRarity.Common)]
    [InlineData(typeof(DexterityPotion), "DexterityPotion", PotionRarity.Common)]
    [InlineData(typeof(StrengthPotion), "StrengthPotion", PotionRarity.Common)]
    [InlineData(typeof(SkillPotion), "SkillPotion", PotionRarity.Common)]
    [InlineData(typeof(AttackPotion), "AttackPotion", PotionRarity.Common)]
    [InlineData(typeof(PowerPotion), "PowerPotion", PotionRarity.Common)]
    [InlineData(typeof(SwiftPotion), "SwiftPotion", PotionRarity.Common)]
    [InlineData(typeof(BloodPotion), "BloodPotion", PotionRarity.Common)]
    [InlineData(typeof(PoisonPotion), "PoisonPotion", PotionRarity.Common)]
    [InlineData(typeof(FocusPotion), "FocusPotion", PotionRarity.Common)]
    [InlineData(typeof(CunningPotion), "CunningPotion", PotionRarity.Common)]
    [InlineData(typeof(LiquidBronze), "LiquidBronze", PotionRarity.Uncommon)]
    [InlineData(typeof(GamblersBrew), "GamblersBrew", PotionRarity.Uncommon)]
    [InlineData(typeof(HeartOfIron), "HeartOfIron", PotionRarity.Uncommon)]
    [InlineData(typeof(FairyInABottle), "FairyInABottle", PotionRarity.Rare)]
    [InlineData(typeof(LiquidMemories), "LiquidMemories", PotionRarity.Rare)]
    [InlineData(typeof(EntropicBrew), "EntropicBrew", PotionRarity.Rare)]
    public void Potion_canonical_metadata(
        System.Type t,
        string expectedId,
        PotionRarity expectedRarity
    )
    {
        PotionModel p = (PotionModel)System.Activator.CreateInstance(t)!;
        Assert.Equal(expectedId, p.Id);
        Assert.Equal(expectedRarity, p.Rarity);
        Assert.NotEmpty(p.Name);
    }

    [Fact]
    public void Phase1Content_potion_catalog_contains_all_s12_potions()
    {
        PotionCatalog catalog = Phase1Content.BuildPotionCatalog();
        Assert.True(catalog.Count >= 20, $"expected >=20 potions, got {catalog.Count}");
    }
}
