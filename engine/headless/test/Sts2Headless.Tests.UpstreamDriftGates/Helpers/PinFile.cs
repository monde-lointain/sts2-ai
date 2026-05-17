using System.IO;
using System.Text.Json;

namespace Sts2Headless.Tests.UpstreamDriftGates.Helpers;

/// <summary>
/// Reads <c>engine/headless/upstream-pin.json</c> (schema v1, per ADR-026).
/// </summary>
internal sealed record PinFile(
    string PinnedVersion,
    string PinnedBuildId,
    string PinnedDllSha256,
    string PinnedCommit
)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Locate and parse <c>upstream-pin.json</c> from the repo root, walking up
    /// from the test assembly's location. Throws <see cref="FileNotFoundException"/>
    /// if the file cannot be found.
    /// </summary>
    public static PinFile Load()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "engine", "headless", "upstream-pin.json");
            if (File.Exists(candidate))
            {
                return Parse(File.ReadAllText(candidate));
            }
            // Also check for the pin file directly (worktree CWD may differ)
            string direct = Path.Combine(dir, "upstream-pin.json");
            if (File.Exists(direct))
            {
                return Parse(File.ReadAllText(direct));
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "Could not locate engine/headless/upstream-pin.json — expected at repo root or above test assembly."
        );
    }

    private static PinFile Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        return new PinFile(
            PinnedVersion: root.GetProperty("pinned_version").GetString() ?? string.Empty,
            PinnedBuildId: root.GetProperty("pinned_buildid").GetString() ?? string.Empty,
            PinnedDllSha256: root.GetProperty("pinned_dll_sha256").GetString() ?? string.Empty,
            PinnedCommit: root.GetProperty("pinned_commit").GetString() ?? string.Empty
        );
    }
}
