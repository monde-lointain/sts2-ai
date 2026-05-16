using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// One per-step record as emitted by the Host's <c>--probe-out</c> stream.
/// Mirrors <see cref="Sts2Headless.Host.FileProbeStream"/>'s output schema.
/// </summary>
public sealed record ProbeRecord(
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("player_hp")] int PlayerHp,
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("enemy_count")] int EnemyCount,
    [property: JsonPropertyName("hash")] string Hash
)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Read a JSON-line probe-output file into a list of records.
    /// </summary>
    public static IReadOnlyList<ProbeRecord> ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var list = new List<ProbeRecord>();
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            ProbeRecord? r = JsonSerializer.Deserialize<ProbeRecord>(trimmed, Options);
            if (r is null)
            {
                throw new InvalidDataException(
                    $"ProbeRecord.ReadFile: failed to parse line in {path}: <<<{trimmed}>>>"
                );
            }
            list.Add(r);
        }
        return list;
    }
}
