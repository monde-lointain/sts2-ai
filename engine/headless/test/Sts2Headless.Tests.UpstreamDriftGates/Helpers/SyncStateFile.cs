using System.IO;
using System.Text.Json;

namespace Sts2Headless.Tests.UpstreamDriftGates.Helpers;

/// <summary>
/// Reads <c>.upstream-sync-state.json</c> from the repo root (written by
/// <c>tools/upstream-sync</c> on each sync).
/// </summary>
internal sealed record SyncStateFile(string LastSyncedBuildId, string LastSyncedVersion)
{
    /// <summary>
    /// Locate and parse <c>.upstream-sync-state.json</c> by walking up from the
    /// test assembly's base directory. Returns <see langword="null"/> if the file
    /// does not exist (first-sync scenario or CI without state file).
    /// </summary>
    public static SyncStateFile? TryLoad()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, ".upstream-sync-state.json");
            if (File.Exists(candidate))
            {
                return Parse(File.ReadAllText(candidate));
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static SyncStateFile Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string buildId = root.TryGetProperty("last_synced_buildid", out JsonElement bid)
            ? bid.GetString() ?? string.Empty
            : string.Empty;
        string version = root.TryGetProperty("last_synced_version", out JsonElement ver)
            ? ver.GetString() ?? string.Empty
            : string.Empty;
        return new SyncStateFile(LastSyncedBuildId: buildId, LastSyncedVersion: version);
    }
}
