using System.Text.Json;

namespace Sts2Headless.Domain.Content;

/// <summary>
/// Strict JSON loader for <see cref="Q4Manifest"/>. Domain-side parser only — it never
/// touches the filesystem (banned per the determinism analyzer); the caller (M9 Host or
/// a test harness) is responsible for reading the file and handing the JSON content as
/// a <see cref="string"/>.
///
/// <para>
/// <b>Strictness:</b> every error path throws <see cref="Q4ManifestFormatException"/>
/// with the offending key / type in the message. We deliberately reject:
/// <list type="bullet">
///   <item>Malformed JSON (System.Text.Json bubbles up wrapped in a format exception).</item>
///   <item>Root that isn't a JSON object.</item>
///   <item>Missing required keys (<c>cards</c>, <c>relics</c>, <c>powers</c>,
///         <c>monsters</c>, <c>potions</c>).</item>
///   <item>Unknown keys at the root — catches typos like <c>"monster"</c> vs
///         <c>"monsters"</c> instead of silently skipping the typo'd list.</item>
///   <item>Non-array values for the five buckets.</item>
///   <item>Non-string entries inside a bucket array.</item>
/// </list>
/// Lenient parsing here would let bugs cross the schema boundary; the M7 spec calls for
/// "explicit error, not silent acceptance".
/// </para>
/// </summary>
public static class Q4ManifestLoader
{
    private const string CardsKey = "cards";
    private const string RelicsKey = "relics";
    private const string PowersKey = "powers";
    private const string MonstersKey = "monsters";
    private const string PotionsKey = "potions";

    private static readonly HashSet<string> KnownRootKeys = new(StringComparer.Ordinal)
    {
        CardsKey,
        RelicsKey,
        PowersKey,
        MonstersKey,
        PotionsKey,
    };

    /// <summary>
    /// Parse <paramref name="json"/> into a <see cref="Q4Manifest"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="Q4ManifestFormatException">JSON is malformed or schema-mismatched.</exception>
    public static Q4Manifest LoadFromString(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new Q4ManifestFormatException("Q4 manifest is not valid JSON.", ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new Q4ManifestFormatException(
                    $"Q4 manifest root must be a JSON object, got {root.ValueKind}."
                );
            }

            // Reject unknown keys at the root — catches typos that would otherwise let
            // a bucket silently fall to []. Iterating EnumerateObject is order-stable
            // per System.Text.Json — but we don't depend on that here.
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (!KnownRootKeys.Contains(prop.Name))
                {
                    throw new Q4ManifestFormatException(
                        $"Unknown key in Q4 manifest root: '{prop.Name}'. "
                            + $"Allowed: cards, relics, powers, monsters, potions."
                    );
                }
            }

            return new Q4Manifest(
                ReadStringArray(root, CardsKey),
                ReadStringArray(root, RelicsKey),
                ReadStringArray(root, PowersKey),
                ReadStringArray(root, MonstersKey),
                ReadStringArray(root, PotionsKey)
            );
        }
    }

    // CA1859: IReadOnlyList preserves manifest immutability contract.
#pragma warning disable CA1859
    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string key)
#pragma warning restore CA1859
    {
        if (!root.TryGetProperty(key, out JsonElement element))
        {
            throw new Q4ManifestFormatException($"Q4 manifest is missing required key '{key}'.");
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new Q4ManifestFormatException(
                $"Q4 manifest key '{key}' must be an array, got {element.ValueKind}."
            );
        }
        // Preserve declaration order — System.Text.Json's JsonElement array iteration is
        // documented index-ordered, which matches what the coverage gate expects.
        List<string> ids = new(element.GetArrayLength());
        int index = 0;
        foreach (JsonElement entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                throw new Q4ManifestFormatException(
                    $"Q4 manifest key '{key}' entry [{index}] must be a string, got {entry.ValueKind}."
                );
            }
            string? value = entry.GetString();
            if (value is null)
            {
                // System.Text.Json shouldn't return null for a JSON string, but defend
                // against the path so callers see a typed error instead of an NRE.
                throw new Q4ManifestFormatException(
                    $"Q4 manifest key '{key}' entry [{index}] is null."
                );
            }
            ids.Add(value);
            index++;
        }
        return ids;
    }
}

/// <summary>
/// Thrown by <see cref="Q4ManifestLoader"/> for any schema / format error. Callers
/// can catch this specifically to distinguish "the file is malformed" from system errors
/// raised at the file-IO boundary above the domain.
/// </summary>
public sealed class Q4ManifestFormatException : Exception
{
    public Q4ManifestFormatException() { }

    public Q4ManifestFormatException(string message)
        : base(message) { }

    public Q4ManifestFormatException(string message, Exception inner)
        : base(message, inner) { }
}
