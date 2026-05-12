using Sts2Headless.Domain.Content;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// Coverage gate compares an expected id set (from <see cref="Q4Manifest"/>) against an
/// actual id set (from a <see cref="ContentTable{TId, TModel}"/>). The CI-gate semantics
/// per the M7 module spec: <b>missing entries fail; extra entries are reported but do
/// not fail</b>. Rationale: a content patch that registers a new card before the Q4
/// manifest is updated should warn (orchestrator review) but not block builds — the
/// reverse (Q4 promises a card that isn't registered) is the one that breaks combat.
/// </summary>
public class CoverageGateTests
{
    private sealed record FakeCard(string Id) : ICardModel;
    private sealed record FakeRelic(string Id) : IRelicModel;

    [Fact]
    public void Empty_manifest_empty_catalog_is_green_with_zero_counts()
    {
        CardCatalog catalog = new();
        Q4Manifest manifest = Q4Manifest.Empty;

        CoverageResult result = CoverageGate.ComputeForCards(manifest, catalog);

        Assert.True(result.IsGreen);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Extra);
        Assert.Equal(0, result.OkCount);
    }

    [Fact]
    public void Manifest_lists_a_b_catalog_has_only_a_reports_missing_b_and_is_red()
    {
        CardCatalog catalog = new();
        catalog.Register("a", new FakeCard("a"));
        Q4Manifest manifest = new(
            Cards:    new[] { "a", "b" },
            Relics:   Array.Empty<string>(),
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        CoverageResult result = CoverageGate.ComputeForCards(manifest, catalog);

        Assert.False(result.IsGreen);
        Assert.Equal(new[] { "b" }, result.Missing);
        Assert.Empty(result.Extra);
        Assert.Equal(1, result.OkCount); // "a" matched both sides
    }

    [Fact]
    public void Manifest_lists_a_catalog_has_a_b_reports_extra_b_but_stays_green()
    {
        CardCatalog catalog = new();
        catalog.Register("a", new FakeCard("a"));
        catalog.Register("b", new FakeCard("b"));
        Q4Manifest manifest = new(
            Cards:    new[] { "a" },
            Relics:   Array.Empty<string>(),
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        CoverageResult result = CoverageGate.ComputeForCards(manifest, catalog);

        Assert.True(result.IsGreen); // extras don't fail the gate
        Assert.Empty(result.Missing);
        Assert.Equal(new[] { "b" }, result.Extra);
        Assert.Equal(1, result.OkCount);
    }

    [Fact]
    public void Missing_set_preserves_manifest_order()
    {
        // Determinism: reports must enumerate in manifest-declared order so diff hunks
        // are stable between runs. (Manifest order is the order ids appear in the JSON.)
        CardCatalog catalog = new();
        Q4Manifest manifest = new(
            Cards:    new[] { "zeta", "alpha", "mu" },
            Relics:   Array.Empty<string>(),
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        CoverageResult result = CoverageGate.ComputeForCards(manifest, catalog);

        Assert.Equal(new[] { "zeta", "alpha", "mu" }, result.Missing);
    }

    [Fact]
    public void Extra_set_preserves_catalog_insertion_order()
    {
        CardCatalog catalog = new();
        catalog.Register("zeta", new FakeCard("zeta"));
        catalog.Register("alpha", new FakeCard("alpha"));
        catalog.Register("mu", new FakeCard("mu"));
        Q4Manifest manifest = Q4Manifest.Empty;

        CoverageResult result = CoverageGate.ComputeForCards(manifest, catalog);

        Assert.Equal(new[] { "zeta", "alpha", "mu" }, result.Extra);
    }

    [Fact]
    public void Compute_handles_relics_just_like_cards()
    {
        RelicCatalog catalog = new();
        catalog.Register("burning_blood", new FakeRelic("burning_blood"));
        Q4Manifest manifest = new(
            Cards:    Array.Empty<string>(),
            Relics:   new[] { "burning_blood", "ring_of_the_snake" },
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        CoverageResult result = CoverageGate.ComputeForRelics(manifest, catalog);

        Assert.False(result.IsGreen);
        Assert.Equal(new[] { "ring_of_the_snake" }, result.Missing);
        Assert.Equal(1, result.OkCount);
    }

    [Fact]
    public void ComputeAll_aggregates_all_five_buckets_and_is_green_when_all_match()
    {
        CardCatalog cards = new();
        RelicCatalog relics = new();
        PowerCatalog powers = new();
        MonsterCatalog monsters = new();
        PotionCatalog potions = new();
        Q4Manifest manifest = Q4Manifest.Empty;

        AggregateCoverageResult result = CoverageGate.ComputeAll(
            manifest, cards, relics, powers, monsters, potions);

        Assert.True(result.IsGreen);
        Assert.True(result.Cards.IsGreen);
        Assert.True(result.Relics.IsGreen);
        Assert.True(result.Powers.IsGreen);
        Assert.True(result.Monsters.IsGreen);
        Assert.True(result.Potions.IsGreen);
    }

    [Fact]
    public void ComputeAll_is_red_when_any_single_bucket_is_red()
    {
        CardCatalog cards = new();
        RelicCatalog relics = new();
        PowerCatalog powers = new();
        MonsterCatalog monsters = new();
        PotionCatalog potions = new();
        // Manifest demands a card that the catalog doesn't have — one red bucket among five.
        Q4Manifest manifest = new(
            Cards:    new[] { "strike" },
            Relics:   Array.Empty<string>(),
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        AggregateCoverageResult result = CoverageGate.ComputeAll(
            manifest, cards, relics, powers, monsters, potions);

        Assert.False(result.IsGreen);
        Assert.False(result.Cards.IsGreen);
        Assert.True(result.Relics.IsGreen);
    }

    [Fact]
    public void ComputeAll_extras_in_any_bucket_do_not_fail_aggregate()
    {
        CardCatalog cards = new();
        cards.Register("extra_card", new FakeCard("extra_card"));
        RelicCatalog relics = new();
        PowerCatalog powers = new();
        MonsterCatalog monsters = new();
        PotionCatalog potions = new();
        Q4Manifest manifest = Q4Manifest.Empty;

        AggregateCoverageResult result = CoverageGate.ComputeAll(
            manifest, cards, relics, powers, monsters, potions);

        Assert.True(result.IsGreen);
        Assert.Equal(new[] { "extra_card" }, result.Cards.Extra);
    }

    [Fact]
    public void Manifest_with_duplicate_ids_throws_during_compute()
    {
        // Duplicate ids in the manifest are a schema bug — would let coverage falsely
        // report "ok" for a card that's silently double-counted. Catch loudly.
        CardCatalog catalog = new();
        catalog.Register("a", new FakeCard("a"));
        Q4Manifest manifest = new(
            Cards:    new[] { "a", "a" },
            Relics:   Array.Empty<string>(),
            Powers:   Array.Empty<string>(),
            Monsters: Array.Empty<string>(),
            Potions:  Array.Empty<string>());

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CoverageGate.ComputeForCards(manifest, catalog));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
