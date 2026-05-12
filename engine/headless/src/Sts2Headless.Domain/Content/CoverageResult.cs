namespace Sts2Headless.Domain.Content;

/// <summary>
/// Result of comparing one manifest bucket against one catalog. See
/// <see cref="CoverageGate"/> for the comparison semantics.
/// </summary>
/// <param name="Missing">
/// Ids the manifest expects but the catalog does not contain. Order follows the
/// manifest's id-declaration order so diff hunks are stable.
/// </param>
/// <param name="Extra">
/// Ids the catalog contains but the manifest does not list. Order follows the catalog's
/// insertion order. <b>Extras do not fail the gate</b> — they're a warning, surfaced for
/// orchestrator review.
/// </param>
/// <param name="OkCount">Number of ids present in both sides.</param>
public sealed record CoverageResult(
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra,
    int OkCount)
{
    /// <summary>
    /// True when no ids are missing. <b>Extras do not fail the gate</b> by design — see
    /// <see cref="Extra"/> remarks.
    /// </summary>
    public bool IsGreen => Missing.Count == 0;

    /// <summary>An empty (vacuously green) result.</summary>
    public static CoverageResult Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        0);
}

/// <summary>
/// Aggregate of all five per-kind coverage checks (cards, relics, powers, monsters,
/// potions). <see cref="IsGreen"/> is the AND of every bucket's gate result.
/// </summary>
public sealed record AggregateCoverageResult(
    CoverageResult Cards,
    CoverageResult Relics,
    CoverageResult Powers,
    CoverageResult Monsters,
    CoverageResult Potions)
{
    public bool IsGreen =>
        Cards.IsGreen && Relics.IsGreen && Powers.IsGreen &&
        Monsters.IsGreen && Potions.IsGreen;
}
