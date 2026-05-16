using System.Globalization;

namespace Sts2Headless.Host;

/// <summary>
/// Parsed CLI arguments for the Q1 process host. Construct via
/// <see cref="Parse(string[])"/>; the canonical CLI surface is:
/// <code>
///   sts2-headless --seed &lt;uint&gt; --character &lt;name&gt; --deck &lt;name&gt;
///                 --relics &lt;id&gt;[,&lt;id&gt;...] --encounter &lt;id&gt;
///                 --ascension &lt;int&gt;
///                 [--metrics-port &lt;port&gt;]
///                 [--script &lt;path&gt;]
///                 [--out &lt;path&gt;]
///                 [--probe-out &lt;path&gt;]
/// </code>
/// All flags use the GNU-style <c>--key value</c> form; <c>--key=value</c> is
/// also accepted. The order is free; unknown flags or missing required values
/// raise <see cref="CliParseException"/> with the canonical usage message.
///
/// <para>
/// Per Q1-ADR-008 the host is single-threaded; the metrics endpoint and SIGTERM
/// shutdown live behind dedicated background threads, but combat decisions
/// happen on the main thread. <see cref="MetricsPort"/> is opt-in: when null
/// the metrics HTTP listener is not started at all (R8 — keeps HttpListener
/// platform fragility off the critical path).
/// </para>
/// </summary>
/// <param name="Seed">RNG seed (uint).</param>
/// <param name="Character">Character name (only "silent" supported in Phase 1).</param>
/// <param name="Deck">Deck preset name (only "starter" supported in Phase 1).</param>
/// <param name="Relics">Ordered list of relic ids; the first is the priority slot.</param>
/// <param name="Encounter">Encounter id (e.g., "CULTISTS_NORMAL" / canonical id).</param>
/// <param name="Ascension">Ascension level (Phase 1 supports 0 only).</param>
/// <param name="MetricsPort">Optional TCP port for Prometheus scrape; null = no metrics server.</param>
/// <param name="ScriptPath">Optional path to a scripted-action file (line-based commands).</param>
/// <param name="OutPath">Optional path; when set, the final-state blob is written here.</param>
/// <param name="ProbeOutPath">
/// Optional path; when set, a JSON-line stream of per-step <c>CanonicalHash</c>
/// records is written here (one line per turn boundary + the initial state +
/// the final state). Used by the S13 determinism probe.
/// </param>
/// <param name="RegistryPath">
/// Optional path to the canonical Q4 token registry
/// (e.g. <c>contracts/registry/phase1-silent.json</c>). When set, the SHA-256
/// of the file's bytes is computed exactly once at boot and stamped into the
/// emitted state-blob's <c>ManifestStamp.ContentHash</c> slot (the wire
/// position for <c>state_blob.proto/registry_sha</c>). When null, the legacy
/// catalog-id-derived hash is used (preserves the S8-T7 golden SHA for tests
/// that don't supply a registry).
/// </param>
public sealed record CliArgs(
    uint Seed,
    string Character,
    string Deck,
    IReadOnlyList<string> Relics,
    string Encounter,
    int Ascension,
    int? MetricsPort,
    string? ScriptPath,
    string? OutPath,
    string? ProbeOutPath,
    string? RegistryPath
)
{
    /// <summary>The single canonical usage block. Emitted on parse failure and on <c>--help</c>.</summary>
    public const string UsageText =
        "Usage: sts2-headless --seed <uint> --character <name> --deck <name>"
        + "\n"
        + "                    --relics <id>[,<id>...] --encounter <id>"
        + "\n"
        + "                    --ascension <int>"
        + "\n"
        + "                    [--metrics-port <port>] [--script <path>] [--out <path>]"
        + "\n"
        + "                    [--probe-out <path>] [--registry <path>]";

    /// <summary>Parse a CLI argument vector. Throws <see cref="CliParseException"/> on any malformed input.</summary>
    public static CliArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        uint? seed = null;
        string? character = null;
        string? deck = null;
        List<string>? relics = null;
        string? encounter = null;
        int? ascension = null;
        int? metricsPort = null;
        string? scriptPath = null;
        string? outPath = null;
        string? probeOutPath = null;
        string? registryPath = null;

        int i = 0;
        while (i < args.Length)
        {
            string token = args[i];
            if (token is null)
            {
                throw new CliParseException($"null argument at position {i}.");
            }

            // Split --key=value form.
            string flag;
            string? inlineValue;
            int eq = token.IndexOf('=');
            if (token.StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                flag = token[..eq];
                inlineValue = token[(eq + 1)..];
            }
            else
            {
                flag = token;
                inlineValue = null;
            }

            string TakeValue(string forFlag)
            {
                if (inlineValue is not null)
                    return inlineValue;
                if (i + 1 >= args.Length)
                {
                    throw new CliParseException($"missing value for {forFlag}.");
                }
                return args[++i];
            }

            switch (flag)
            {
                case "--seed":
                {
                    string v = TakeValue(flag);
                    if (
                        !uint.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out uint parsed
                        )
                    )
                    {
                        throw new CliParseException(
                            $"--seed: expected unsigned integer, got '{v}'."
                        );
                    }
                    seed = parsed;
                    break;
                }
                case "--character":
                    character = TakeValue(flag);
                    break;
                case "--deck":
                    deck = TakeValue(flag);
                    break;
                case "--relics":
                {
                    string v = TakeValue(flag);
                    relics = ParseCommaList(v, flag);
                    break;
                }
                case "--encounter":
                    encounter = TakeValue(flag);
                    break;
                case "--ascension":
                {
                    string v = TakeValue(flag);
                    if (
                        !int.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed
                        )
                    )
                    {
                        throw new CliParseException($"--ascension: expected integer, got '{v}'.");
                    }
                    ascension = parsed;
                    break;
                }
                case "--metrics-port":
                {
                    string v = TakeValue(flag);
                    if (
                        !int.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed
                        )
                        || parsed < 1
                        || parsed > 65535
                    )
                    {
                        throw new CliParseException(
                            $"--metrics-port: expected 1..65535, got '{v}'."
                        );
                    }
                    metricsPort = parsed;
                    break;
                }
                case "--script":
                    scriptPath = TakeValue(flag);
                    break;
                case "--out":
                    outPath = TakeValue(flag);
                    break;
                case "--probe-out":
                    probeOutPath = TakeValue(flag);
                    break;
                case "--registry":
                    registryPath = TakeValue(flag);
                    break;
                case "--help":
                case "-h":
                    throw new CliParseException(UsageText) { IsHelp = true };
                default:
                    throw new CliParseException($"unknown flag '{flag}'.");
            }

            i++;
        }

        if (seed is null)
            throw new CliParseException("--seed is required.");
        if (character is null)
            throw new CliParseException("--character is required.");
        if (deck is null)
            throw new CliParseException("--deck is required.");
        if (relics is null)
            throw new CliParseException("--relics is required.");
        if (encounter is null)
            throw new CliParseException("--encounter is required.");
        if (ascension is null)
            throw new CliParseException("--ascension is required.");

        return new CliArgs(
            Seed: seed.Value,
            Character: character,
            Deck: deck,
            Relics: relics,
            Encounter: encounter,
            Ascension: ascension.Value,
            MetricsPort: metricsPort,
            ScriptPath: scriptPath,
            OutPath: outPath,
            ProbeOutPath: probeOutPath,
            RegistryPath: registryPath
        );
    }

    private static List<string> ParseCommaList(string raw, string flag)
    {
        // Empty value is an error: --relics must list at least one id, but the
        // smoke set includes RingOfTheSnake so this is enforced upstream.
        if (string.IsNullOrEmpty(raw))
        {
            throw new CliParseException($"{flag}: empty value.");
        }
        var list = new List<string>();
        foreach (string part in raw.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                throw new CliParseException($"{flag}: empty entry in comma-separated list.");
            }
            list.Add(trimmed);
        }
        return list;
    }
}

/// <summary>
/// Raised by <see cref="CliArgs.Parse(string[])"/> on any malformed-input
/// condition. The <see cref="System.Exception.Message"/> is the user-facing
/// error; callers may also prepend <see cref="CliArgs.UsageText"/>.
/// <para>
/// <see cref="IsHelp"/> distinguishes the <c>--help</c> path (exit code 0)
/// from genuine errors (exit code 2 per the GNU convention).
/// </para>
/// </summary>
public sealed class CliParseException : Exception
{
    public bool IsHelp { get; init; }

    public CliParseException() { }

    public CliParseException(string message)
        : base(message) { }

    public CliParseException(string message, Exception inner)
        : base(message, inner) { }
}
