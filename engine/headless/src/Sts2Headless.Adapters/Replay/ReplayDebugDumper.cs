using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Human-readable JSON dumper for replay files. The default per Q1
/// implementation-plan Open Question Q3 is "binary primary + JSON debug
/// dumper alongside" — this is the debug dumper.
///
/// <para>
/// <b>Use-cases:</b>
/// </para>
/// <list type="bullet">
///   <item>Solo-dev inspection of a replay file when a probe diverges and
///   the failing step needs eyeballing.</item>
///   <item>Diff-friendly format for committing reference replays to the
///   test corpus (binary diffs are unreadable in code review).</item>
///   <item>Equivalence-tested vs the binary reader so the JSON is a true
///   alternative representation, not a lossy summary.</item>
/// </list>
///
/// <para>
/// <b>Output shape:</b>
/// </para>
/// <code>
///   {
///     "schemaVersion": 1,
///     "manifestStamp": {
///       "gitSha": "deadbeef...",
///       "buildId": "Q1-Phase1...",
///       "contentHashHex": "00...32 hex chars..."
///     },
///     "initialSeed": 1234567890,
///     "entries": [
///       {
///         "turnNo": 0,
///         "phase": "CombatStart",
///         "actionType": "EndTurn",
///         "actionDataHex": "",
///         "postHashHex": "ab...64 hex chars..."
///       },
///       ...
///     ],
///     "trailerValidated": true
///   }
/// </code>
/// </summary>
public static class ReplayDebugDumper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Read the binary replay at <paramref name="replayPath"/>, decode it,
    /// and write the human-readable JSON form to <paramref name="jsonOutPath"/>.
    /// </summary>
    public static void WriteJson(string replayPath, string jsonOutPath)
    {
        ArgumentNullException.ThrowIfNull(replayPath);
        ArgumentNullException.ThrowIfNull(jsonOutPath);
        ReplayBlob blob = ReplayReader.Open(replayPath);
        File.WriteAllText(jsonOutPath, ToJsonString(blob));
    }

    /// <summary>
    /// Render <paramref name="blob"/> to a JSON string (pretty-printed).
    /// </summary>
    public static string ToJsonString(ReplayBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ReplayDumpDto dto = ToDto(blob);
        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    /// <summary>
    /// Build the JSON DTO for a blob. Exposed so equivalence tests can
    /// compare DTOs directly without parsing JSON.
    /// </summary>
    public static ReplayDumpDto ToDto(ReplayBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ManifestStamp stamp = blob.ManifestStamp;
        ManifestStampDto stampDto = new(
            GitSha: stamp.GitSha,
            BuildId: stamp.BuildId,
            ContentHashHex: Convert.ToHexStringLower(stamp.ContentHash)
        );

        EntryDto[] entryDtos = new EntryDto[blob.Entries.Count];
        for (int i = 0; i < blob.Entries.Count; i++)
        {
            ReplayEntry e = blob.Entries[i];
            entryDtos[i] = new EntryDto(
                TurnNo: e.TurnNo,
                Phase: e.Phase.ToString(),
                ActionType: e.ActionType.ToString(),
                ActionDataHex: Convert.ToHexStringLower(e.ActionData),
                PostHashHex: Convert.ToHexStringLower(e.PostHash)
            );
        }

        return new ReplayDumpDto(
            SchemaVersion: blob.SchemaVersion,
            ManifestStamp: stampDto,
            InitialSeed: blob.InitialSeed,
            Entries: entryDtos,
            TrailerValidated: blob.TrailerValidated
        );
    }
}

/// <summary>Top-level DTO for the JSON dump.</summary>
public sealed record ReplayDumpDto(
    ushort SchemaVersion,
    ManifestStampDto ManifestStamp,
    uint InitialSeed,
    EntryDto[] Entries,
    bool TrailerValidated
);

/// <summary>Manifest-stamp DTO with hex-encoded content_hash.</summary>
public sealed record ManifestStampDto(string GitSha, string BuildId, string ContentHashHex);

/// <summary>Per-step DTO. Bytes are hex-encoded for diff-friendliness.</summary>
public sealed record EntryDto(
    uint TurnNo,
    string Phase,
    string ActionType,
    string ActionDataHex,
    string PostHashHex
);
