using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// One probe entry: a single (seed, encounter, scriptLines) tuple. The
/// <see cref="Mode"/> selects how the probe should validate this entry.
/// </summary>
public sealed record CorpusEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("seed")] uint Seed,
    [property: JsonPropertyName("encounter")] string Encounter,
    [property: JsonPropertyName("relics")] IReadOnlyList<string> Relics,
    [property: JsonPropertyName("script")] IReadOnlyList<string> Script
)
{
    /// <summary>Probe modes — exactly mirrors the <c>--mode</c> CLI choices.</summary>
    public const string ModePerStep = "per_step";
    public const string ModeInitialState = "initial_state";
    public const string ModeStructural = "structural";
}

/// <summary>
/// Top-level corpus envelope written to <c>corpus/phase1-corpus.json</c>.
/// Versioned so the schema can evolve without breaking older corpora.
/// </summary>
public sealed record Corpus(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("entries")] IReadOnlyList<CorpusEntry> Entries
)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>Serialize this corpus to JSON (pretty-printed, stable order).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, PrettyOptions);

    /// <summary>Parse a JSON file into a Corpus. Throws on schema mismatch.</summary>
    public static Corpus FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Corpus? c = JsonSerializer.Deserialize<Corpus>(json, PrettyOptions);
        if (c is null)
        {
            throw new InvalidDataException("Corpus.FromJson: JSON deserialized to null.");
        }
        if (c.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Corpus.FromJson: version {c.Version} unsupported (this probe handles v{CurrentVersion})."
            );
        }
        return c;
    }
}
