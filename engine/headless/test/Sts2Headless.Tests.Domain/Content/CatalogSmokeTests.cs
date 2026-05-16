using Sts2Headless.Domain.Content;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// Smoke tests for the five concrete content catalogs. Each catalog is a thin
/// <see cref="ContentTable{TId, TModel}"/> alias — these tests prove the type wiring
/// (interface markers, generic args) is sound and that the inherited Register / Get /
/// Enumerate surface works for each catalog. Real content arrives in S5 / S12.
/// </summary>
public class CatalogSmokeTests
{
    private sealed record FakeCard(string Id) : ICardModel;

    private sealed record FakeRelic(string Id) : IRelicModel;

    private sealed record FakePower(string Id) : IPowerModel;

    private sealed record FakeMonster(string Id) : IMonsterModel;

    private sealed record FakePotion(string Id) : IPotionModel;

    [Fact]
    public void CardCatalog_instantiates_empty_and_supports_register_lookup()
    {
        CardCatalog catalog = new();
        Assert.Equal(0, catalog.Count);

        FakeCard strike = new("strike");
        catalog.Register(strike.Id, strike);

        Assert.Equal(1, catalog.Count);
        Assert.Same(strike, catalog.Get("strike"));
    }

    [Fact]
    public void RelicCatalog_instantiates_empty_and_supports_register_lookup()
    {
        RelicCatalog catalog = new();
        Assert.Equal(0, catalog.Count);

        FakeRelic ring = new("ring_of_the_snake");
        catalog.Register(ring.Id, ring);

        Assert.True(catalog.Contains("ring_of_the_snake"));
        Assert.Same(ring, catalog.Get("ring_of_the_snake"));
    }

    [Fact]
    public void PowerCatalog_instantiates_empty_and_supports_register_lookup()
    {
        PowerCatalog catalog = new();
        FakePower vulnerable = new("vulnerable");
        catalog.Register(vulnerable.Id, vulnerable);

        Assert.Same(vulnerable, catalog.Get("vulnerable"));
    }

    [Fact]
    public void MonsterCatalog_instantiates_empty_and_supports_register_lookup()
    {
        MonsterCatalog catalog = new();
        FakeMonster cultist = new("cultist");
        catalog.Register(cultist.Id, cultist);

        Assert.Same(cultist, catalog.Get("cultist"));
    }

    [Fact]
    public void PotionCatalog_instantiates_empty_and_supports_register_lookup()
    {
        PotionCatalog catalog = new();
        FakePotion swift = new("swift_potion");
        catalog.Register(swift.Id, swift);

        Assert.Same(swift, catalog.Get("swift_potion"));
    }

    [Fact]
    public void All_catalogs_inherit_insertion_order_enumeration()
    {
        CardCatalog catalog = new();
        catalog.Register("c", new FakeCard("c"));
        catalog.Register("a", new FakeCard("a"));
        catalog.Register("b", new FakeCard("b"));

        Assert.Equal(new[] { "c", "a", "b" }, catalog.EnumerateIds().ToArray());
    }

    [Fact]
    public void IContentModel_marker_uniformly_exposes_Id_across_kinds()
    {
        // Sanity: every catalog's element interface chains to IContentModel.Id so future
        // generic coverage code can treat them uniformly without per-kind branching.
        ICardModel card = new FakeCard("card_x");
        IRelicModel relic = new FakeRelic("relic_x");
        IPowerModel power = new FakePower("power_x");
        IMonsterModel monster = new FakeMonster("monster_x");
        IPotionModel potion = new FakePotion("potion_x");

        IContentModel[] all = { card, relic, power, monster, potion };
        Assert.Equal(
            new[] { "card_x", "relic_x", "power_x", "monster_x", "potion_x" },
            all.Select(m => m.Id).ToArray()
        );
    }
}
