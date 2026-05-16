using System.Globalization;
using System.Text;
using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Host;

/// <summary>
/// Writes per-step canonical-hash records to a JSON-line file (one object per
/// line). The file format is intentionally trivial so the S13 probe can read
/// it with a streaming JSON parser without depending on host internals.
///
/// <para>
/// <b>Record schema:</b>
/// </para>
/// <code>
///   {"step": int, "event": string, "turn": int, "phase": string,
///    "player_hp": int, "energy": int, "enemy_count": int,
///    "hash": "&lt;64-char-hex&gt;"}
/// </code>
///
/// <para>
/// <b>Determinism:</b> the catalog id stream is pulled from the host bundle in
/// registration order (already deterministic via <c>ContentTable.EnumerateIds</c>),
/// so the token map embedded in the hashed blob is reproducible. The
/// <see cref="ManifestStamp"/> uses a fixed placeholder so the hash is
/// independent of build environment.
/// </para>
/// </summary>
public sealed class FileProbeStream : IProbeStream, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly CompositionRoot.CompositionRootBundle _bundle;
    private readonly ManifestStamp _stamp;
    private readonly TokenMap _tokens;
    private int _stepCounter;
    private bool _closed;

    /// <summary>
    /// Open a fresh probe-output file at <paramref name="path"/>. Overwrites
    /// any existing file. The <paramref name="bundle"/> supplies the catalogs
    /// used to build the token map.
    /// </summary>
    public FileProbeStream(string path, CompositionRoot.CompositionRootBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bundle);
        _bundle = bundle;
        // No buffering — the probe runs in-process and we want fail-safe
        // immediate-flush semantics so a crash doesn't lose the last record.
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        // Build the token map once (it's stable for a single combat) and the
        // ManifestStamp once (used to envelope the codec output deterministically).
        _tokens = BuildTokenMap(bundle);
        _stamp = new ManifestStamp(
            GitSha: ProbeManifestConstants.PlaceholderGitSha,
            BuildId: ProbeManifestConstants.PlaceholderBuildId,
            ContentHash: ManifestStamp.ContentHashFromIds(EnumerateAllCatalogIds(bundle))
        );
    }

    /// <inheritdoc/>
    public void Emit(string eventName, CombatState state)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(state);
        if (_closed)
            throw new ObjectDisposedException(nameof(FileProbeStream));

        // Build a fresh RNG bundle for this snapshot — uses the current Rng
        // counter so per-step records reflect post-action RNG state.
        var runRng = new RunRngSet(stringSeed: $"seed-{_bundle.Rng.Seed}");
        var playerRng = new PlayerRngSet(seed: _bundle.Rng.Seed);

        byte[] blob = StateCodec.Serialize(state, runRng, playerRng, _tokens, _stamp);
        string hash = CanonicalHash.Sha256Hex(blob);

        // Hand-built JSON to keep field order stable across runtimes — System.Text.Json
        // can reorder properties in some configurations.
        var sb = new StringBuilder(192);
        sb.Append('{');
        sb.Append("\"step\":").Append(_stepCounter.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"event\":").Append(JsonEncode(eventName));
        sb.Append(",\"turn\":").Append(state.TurnCounter.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"phase\":").Append(JsonEncode(state.Phase.ToString()));
        sb.Append(",\"player_hp\":")
            .Append(state.Player.CurrentHp.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"energy\":").Append(state.Energy.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"enemy_count\":")
            .Append(state.Enemies.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"hash\":\"").Append(hash).Append('"');
        sb.Append('}');

        _writer.WriteLine(sb.ToString());
        _stepCounter++;
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (_closed)
            return;
        _writer.Flush();
        _writer.Dispose();
        _closed = true;
    }

    public void Dispose() => Close();

    // === Helpers ==========================================================

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);

    private static TokenMap BuildTokenMap(CompositionRoot.CompositionRootBundle bundle)
    {
        var tokens = new TokenMap();
        foreach (string id in EnumerateAllCatalogIds(bundle))
        {
            tokens.GetOrAddId(id);
        }
        return tokens;
    }

    private static IEnumerable<string> EnumerateAllCatalogIds(
        CompositionRoot.CompositionRootBundle bundle
    )
    {
        foreach (string id in bundle.Cards.EnumerateIds())
            yield return id;
        foreach (string id in bundle.Relics.EnumerateIds())
            yield return id;
        foreach (string id in bundle.Powers.EnumerateIds())
            yield return id;
        foreach (string id in bundle.Monsters.EnumerateIds())
            yield return id;
        foreach (string id in bundle.Encounters.EnumerateIds())
            yield return id;
    }
}

/// <summary>
/// Pinned placeholders for the <see cref="ManifestStamp"/> used by the probe
/// stream. The probe-determinism contract requires these to be byte-stable
/// across runs, so they don't pull from <c>Environment.Version</c> or git.
/// </summary>
internal static class ProbeManifestConstants
{
    /// <summary>Reproducible 8-char placeholder git sha for the probe stream's stamp.</summary>
    public const string PlaceholderGitSha = "S13probe";

    /// <summary>Reproducible build id for the probe stream's stamp.</summary>
    public const string PlaceholderBuildId = "S13-T8-probe";
}
