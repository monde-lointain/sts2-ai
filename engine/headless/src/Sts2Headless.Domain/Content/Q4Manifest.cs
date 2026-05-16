namespace Sts2Headless.Domain.Content;

/// <summary>
/// Q4 content manifest — the Phase-1 surface, loaded once at process init from a JSON
/// file shipped with the model artifact (per pipeline ADR-010). Holds the set of ids
/// the catalog is *expected* to register, grouped by content kind. The coverage gate
/// compares this against the actual catalog contents.
///
/// <para>
/// <b>Schema (v1):</b>
/// <code>
/// {
///   "cards":    ["card_id_1", ...],
///   "relics":   ["relic_id_1", ...],
///   "powers":   ["power_id_1", ...],
///   "monsters": ["monster_id_1", ...],
///   "potions":  ["potion_id_1", ...]
/// }
/// </code>
/// Every key is required. Unknown keys at the JSON root are rejected by
/// <see cref="Q4ManifestLoader"/>. Empty arrays are valid — that's how Phase-1
/// infrastructure-only builds (S3) pass the coverage gate vacuously.
/// </para>
/// </summary>
public sealed record Q4Manifest(
    IReadOnlyList<string> Cards,
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Powers,
    IReadOnlyList<string> Monsters,
    IReadOnlyList<string> Potions
)
{
    /// <summary>An empty manifest — all five buckets empty. Useful for vacuous tests.</summary>
    public static Q4Manifest Empty { get; } =
        new(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()
        );
}
