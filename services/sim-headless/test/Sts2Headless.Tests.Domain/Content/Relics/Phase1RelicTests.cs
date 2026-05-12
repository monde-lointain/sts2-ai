using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Relics;

namespace Sts2Headless.Tests.Domain.Content.Relics;

/// <summary>
/// Byte-faithful metadata checks for S12 Phase-1 relics. Each test asserts canonical
/// id + name + rarity per upstream <c>RelicModel.Rarity</c> overrides.
/// </summary>
public class Phase1RelicTests
{
    [Theory]
    [InlineData(typeof(Akabeko), "Akabeko", RelicRarity.Uncommon)]
    [InlineData(typeof(BagOfMarbles), "BagOfMarbles", RelicRarity.Common)]
    [InlineData(typeof(BronzeScales), "BronzeScales", RelicRarity.Common)]
    [InlineData(typeof(CentennialPuzzle), "CentennialPuzzle", RelicRarity.Common)]
    [InlineData(typeof(HappyFlower), "HappyFlower", RelicRarity.Common)]
    [InlineData(typeof(OddlySmoothStone), "OddlySmoothStone", RelicRarity.Common)]
    [InlineData(typeof(RedSkull), "RedSkull", RelicRarity.Common)]
    [InlineData(typeof(Whetstone), "Whetstone", RelicRarity.Common)]
    [InlineData(typeof(DataDisk), "DataDisk", RelicRarity.Common)]
    [InlineData(typeof(MealTicket), "MealTicket", RelicRarity.Common)]
    [InlineData(typeof(PenNib), "PenNib", RelicRarity.Uncommon)]
    [InlineData(typeof(Pantograph), "Pantograph", RelicRarity.Uncommon)]
    [InlineData(typeof(MercuryHourglass), "MercuryHourglass", RelicRarity.Uncommon)]
    [InlineData(typeof(PaperPhrog), "PaperPhrog", RelicRarity.Uncommon)]
    [InlineData(typeof(GamblingChip), "GamblingChip", RelicRarity.Rare)]
    [InlineData(typeof(Mango), "Mango", RelicRarity.Rare)]
    [InlineData(typeof(Pocketwatch), "Pocketwatch", RelicRarity.Rare)]
    [InlineData(typeof(Shovel), "Shovel", RelicRarity.Rare)]
    [InlineData(typeof(ArtOfWar), "ArtOfWar", RelicRarity.Rare)]
    [InlineData(typeof(Bellows), "Bellows", RelicRarity.Rare)]
    [InlineData(typeof(Bookmark), "Bookmark", RelicRarity.Rare)]
    [InlineData(typeof(MeatOnTheBone), "MeatOnTheBone", RelicRarity.Rare)]
    [InlineData(typeof(BeautifulBracelet), "BeautifulBracelet", RelicRarity.Ancient)]
    [InlineData(typeof(BiiigHug), "BiiigHug", RelicRarity.Ancient)]
    [InlineData(typeof(CallingBell), "CallingBell", RelicRarity.Ancient)]
    [InlineData(typeof(Ectoplasm), "Ectoplasm", RelicRarity.Ancient)]
    [InlineData(typeof(Cauldron), "Cauldron", RelicRarity.Shop)]
    [InlineData(typeof(DollysMirror), "DollysMirror", RelicRarity.Shop)]
    [InlineData(typeof(LeesWaffle), "LeesWaffle", RelicRarity.Shop)]
    [InlineData(typeof(TheBoot), "TheBoot", RelicRarity.Event)]
    [InlineData(typeof(SneckosEye), "SneckosEye", RelicRarity.Rare)]
    [InlineData(typeof(SnakeSkull), "SnakeSkull", RelicRarity.Rare)]
    [InlineData(typeof(NinjaScroll), "NinjaScroll", RelicRarity.Common)]
    [InlineData(typeof(PaintedFan), "PaintedFan", RelicRarity.Common)]
    [InlineData(typeof(TwistedFunnel), "TwistedFunnel", RelicRarity.Rare)]
    [InlineData(typeof(WristBlade), "WristBlade", RelicRarity.Uncommon)]
    [InlineData(typeof(Tingsha), "Tingsha", RelicRarity.Uncommon)]
    [InlineData(typeof(TheTotem), "TheTotem", RelicRarity.Uncommon)]
    [InlineData(typeof(TornCard), "TornCard", RelicRarity.Rare)]
    [InlineData(typeof(HoveringKite), "HoveringKite", RelicRarity.Rare)]
    [InlineData(typeof(CeramicFish), "CeramicFish", RelicRarity.Common)]
    [InlineData(typeof(Strawberry), "Strawberry", RelicRarity.Common)]
    [InlineData(typeof(Pear), "Pear", RelicRarity.Common)]
    [InlineData(typeof(Anchovy), "Anchovy", RelicRarity.Common)]
    [InlineData(typeof(Lantern), "Lantern", RelicRarity.Uncommon)]
    [InlineData(typeof(Cookbook), "Cookbook", RelicRarity.Common)]
    [InlineData(typeof(GremlinHorn), "GremlinHorn", RelicRarity.Uncommon)]
    [InlineData(typeof(HornCleat), "HornCleat", RelicRarity.Uncommon)]
    [InlineData(typeof(LetterOpener), "LetterOpener", RelicRarity.Uncommon)]
    [InlineData(typeof(OrnamentalFan), "OrnamentalFan", RelicRarity.Uncommon)]
    [InlineData(typeof(Orichalcum), "Orichalcum", RelicRarity.Uncommon)]
    [InlineData(typeof(Kunai), "Kunai", RelicRarity.Uncommon)]
    [InlineData(typeof(Shuriken), "Shuriken", RelicRarity.Uncommon)]
    [InlineData(typeof(ToyOrnithopter), "ToyOrnithopter", RelicRarity.Uncommon)]
    public void Relic_canonical_metadata(System.Type t, string expectedId, RelicRarity expectedRarity)
    {
        RelicModel r = (RelicModel)System.Activator.CreateInstance(t)!;
        Assert.Equal(expectedId, r.Id);
        Assert.Equal(expectedRarity, r.Rarity);
        Assert.NotEmpty(r.Name);
    }

    [Fact]
    public void Phase1Content_relic_catalog_contains_all_smoke_plus_s12_relics()
    {
        RelicCatalog catalog = Phase1Content.BuildRelicCatalog();
        // 5 smoke + 53 S12 = 58 relics.
        Assert.True(catalog.Count >= 50, $"expected >=50 relics, got {catalog.Count}");
        Assert.True(catalog.Contains("RingOfTheSnake"));
        Assert.True(catalog.Contains("Akabeko"));
        Assert.True(catalog.Contains("SneckosEye"));
    }
}
