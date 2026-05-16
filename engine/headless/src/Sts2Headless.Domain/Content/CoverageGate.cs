namespace Sts2Headless.Domain.Content;

/// <summary>
/// Compares a <see cref="Q4Manifest"/> bucket against a populated content catalog. The
/// M7 module spec uses this as a CI gate: every id the Q4 manifest promises must be
/// registered in the matching catalog.
///
/// <para>
/// <b>Gate semantics:</b> <i>missing fails, extras are reported but pass.</i>
/// </para>
/// <list type="bullet">
///   <item><b>Missing</b> — manifest lists an id the catalog doesn't have. This is a
///         hard failure: combat code may try to look up the id and crash.</item>
///   <item><b>Extra</b> — catalog has an id the manifest doesn't list. Treated as a
///         warning, not a failure. Rationale: content patches that add a card before
///         updating Q4 should not block builds; the orchestrator notices the gap and
///         updates the manifest in the same release.</item>
/// </list>
/// (If the orchestration requirements ever flip — extras must fail — toggle the
/// <see cref="CoverageResult.IsGreen"/> definition in one place.)
/// </summary>
public static class CoverageGate
{
    public static CoverageResult ComputeForCards(Q4Manifest manifest, CardCatalog catalog) =>
        Compute(manifest.Cards, catalog.EnumerateIds());

    public static CoverageResult ComputeForRelics(Q4Manifest manifest, RelicCatalog catalog) =>
        Compute(manifest.Relics, catalog.EnumerateIds());

    public static CoverageResult ComputeForPowers(Q4Manifest manifest, PowerCatalog catalog) =>
        Compute(manifest.Powers, catalog.EnumerateIds());

    public static CoverageResult ComputeForMonsters(Q4Manifest manifest, MonsterCatalog catalog) =>
        Compute(manifest.Monsters, catalog.EnumerateIds());

    public static CoverageResult ComputeForPotions(Q4Manifest manifest, PotionCatalog catalog) =>
        Compute(manifest.Potions, catalog.EnumerateIds());

    /// <summary>
    /// Aggregate coverage check over all five catalogs at once.
    /// </summary>
    public static AggregateCoverageResult ComputeAll(
        Q4Manifest manifest,
        CardCatalog cards,
        RelicCatalog relics,
        PowerCatalog powers,
        MonsterCatalog monsters,
        PotionCatalog potions
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(relics);
        ArgumentNullException.ThrowIfNull(powers);
        ArgumentNullException.ThrowIfNull(monsters);
        ArgumentNullException.ThrowIfNull(potions);

        return new AggregateCoverageResult(
            Cards: ComputeForCards(manifest, cards),
            Relics: ComputeForRelics(manifest, relics),
            Powers: ComputeForPowers(manifest, powers),
            Monsters: ComputeForMonsters(manifest, monsters),
            Potions: ComputeForPotions(manifest, potions)
        );
    }

    /// <summary>
    /// Core diff: classify each id as Missing / Extra / Ok. Missing preserves the
    /// manifest's declaration order; Extra preserves the catalog's insertion order —
    /// both orders are deterministic per the M7 contract.
    /// </summary>
    private static CoverageResult Compute(
        IReadOnlyList<string> expected,
        IEnumerable<string> actual
    )
    {
        // Materialize actual once so we can both probe membership and walk in insertion
        // order without depending on the iterator's re-enumeration semantics.
        List<string> actualOrdered = actual.ToList();
        HashSet<string> actualSet = new(actualOrdered, StringComparer.Ordinal);

        // Detect duplicate manifest ids loudly. A duplicate would make OkCount lie.
        HashSet<string> seenInExpected = new(StringComparer.Ordinal);
        List<string> missing = new();
        int okCount = 0;
        foreach (string id in expected)
        {
            if (!seenInExpected.Add(id))
            {
                throw new InvalidOperationException(
                    $"Duplicate id '{id}' in Q4 manifest bucket — manifest schema bug."
                );
            }
            if (actualSet.Contains(id))
            {
                okCount++;
            }
            else
            {
                missing.Add(id);
            }
        }

        // Extras: catalog ids the manifest doesn't list, in catalog insertion order.
        List<string> extra = new();
        foreach (string id in actualOrdered)
        {
            if (!seenInExpected.Contains(id))
            {
                extra.Add(id);
            }
        }

        return new CoverageResult(missing, extra, okCount);
    }
}
