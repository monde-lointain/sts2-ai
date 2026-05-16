using System.Globalization;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Host;

/// <summary>
/// Entry point for the Q1 headless process host. Parses CLI args, builds the
/// composition root, runs the reference combat to completion, and writes the
/// final summary (or state blob) before exiting.
///
/// <para>
/// Exit codes (GNU convention):
/// </para>
/// <list type="bullet">
///   <item><c>0</c> — victory (or graceful shutdown).</item>
///   <item><c>1</c> — defeat.</item>
///   <item><c>2</c> — CLI parse error, unexpected end of script, or fatal.</item>
/// </list>
///
/// <para>
/// <b>Thread model:</b> single-threaded per Q1-ADR-008. The metrics HTTP
/// server (if enabled) and graceful-shutdown signal subscriptions run on
/// background threads, but combat decisions stay on this thread. S9 will need
/// to revisit this when the control-plane IPC adapter arrives, since the
/// thread-static <c>EffectObserver</c> wired by S5/S6 assumes the engine runs
/// on a single thread.
/// </para>
/// </summary>
public static class Program
{
    public const int ExitVictory = 0;
    public const int ExitDefeat = 1;
    public const int ExitError = 2;

    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error, attachProcessSignals: true);
    }

    /// <summary>
    /// Test-friendly main: lets the caller swap stdout/stderr writers and
    /// disable process-level signal attachment. Returns the exit code.
    /// </summary>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool attachProcessSignals
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        CliArgs cli;
        try
        {
            cli = CliArgs.Parse(args);
        }
        catch (CliParseException cpx) when (cpx.IsHelp)
        {
            stdout.WriteLine(cpx.Message);
            return ExitVictory;
        }
        catch (CliParseException cpx)
        {
            stderr.WriteLine($"sts2-headless: {cpx.Message}");
            stderr.WriteLine(CliArgs.UsageText);
            return ExitError;
        }

        // --- Composition root + services ----------------------------------
        CompositionRoot.CompositionRootBundle bundle;
        try
        {
            bundle = CompositionRoot.Build(cli);
        }
        catch (CompositionException cex)
        {
            stderr.WriteLine($"sts2-headless: {cex.Message}");
            return ExitError;
        }

        // --- Boot-time registry SHA (D6 single-source) --------------------
        // Read the Q4 token registry exactly once here; the bytes become the
        // ManifestStamp.ContentHash on every state-blob this run emits. When
        // --registry is omitted we keep the legacy catalog-id-derived hash
        // for back-compat with the S8-T7 golden.
        byte[]? registryShaBytes;
        try
        {
            registryShaBytes = cli.RegistryPath is null
                ? null
                : RegistryShaProvider.ReadRegistryShaBytes(cli.RegistryPath);
        }
        catch (FileNotFoundException ex)
        {
            stderr.WriteLine($"sts2-headless: {ex.Message}");
            return ExitError;
        }

        var logger = new JsonLineLogger(bundle.Clock, stderr);
        var metrics = new PrometheusMetricsRegistry();
        MetricsHttpServer? metricsServer = null;
        if (cli.MetricsPort.HasValue)
        {
            metricsServer = new MetricsHttpServer(metrics, cli.MetricsPort.Value);
            try
            {
                metricsServer.Start();
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"sts2-headless: metrics endpoint failed to start: {ex.Message}");
                metricsServer = null;
            }
        }

        using var shutdown = new GracefulShutdown(attachProcessSignals);

        // --- Resolve script provider --------------------------------------
        IScriptedActionProvider provider;
        try
        {
            if (cli.ScriptPath is null)
            {
                // No script supplied — empty provider so the loop exits via
                // ScriptExhausted at the first decision boundary. The
                // end-to-end test path always supplies one.
                provider = new FileScriptedActionProvider(Array.Empty<string>(), bundle.Cards);
            }
            else
            {
                provider = new FileScriptedActionProvider(cli.ScriptPath, bundle.Cards);
            }
        }
        catch (ScriptParseException spex)
        {
            stderr.WriteLine($"sts2-headless: {spex.Message}");
            metricsServer?.Dispose();
            return ExitError;
        }

        // --- Optional probe sink ------------------------------------------
        FileProbeStream? probe = null;
        if (cli.ProbeOutPath is not null)
        {
            try
            {
                probe = new FileProbeStream(cli.ProbeOutPath, bundle);
            }
            catch (Exception ex)
            {
                stderr.WriteLine(
                    $"sts2-headless: failed to open --probe-out '{cli.ProbeOutPath}': {ex.Message}"
                );
                metricsServer?.Dispose();
                return ExitError;
            }
        }

        // --- Run combat ---------------------------------------------------
        MainLoop.RunResult result;
        try
        {
            result = MainLoop.Run(
                bundle.Context,
                bundle.Cards,
                provider,
                logger,
                metrics,
                shutdown.Token,
                probe
            );
        }
        catch (ScriptParseException spex)
        {
            stderr.WriteLine($"sts2-headless: {spex.Message}");
            probe?.Dispose();
            metricsServer?.Dispose();
            return ExitError;
        }
        // Probe is intentionally NOT disposed here yet — the final-state hash
        // gets emitted by MainLoop just before it returns, so we close the
        // sink after that (below, near the metrics-server shutdown).

        // --- Final state output -------------------------------------------
        string finalHash = ComputeFinalStateSha(result.FinalState, bundle, registryShaBytes);
        if (cli.OutPath is not null)
        {
            try
            {
                byte[] blob = BuildStateBlob(result.FinalState, bundle, registryShaBytes);
                File.WriteAllBytes(cli.OutPath, blob);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"sts2-headless: failed to write --out: {ex.Message}");
                probe?.Dispose();
                metricsServer?.Dispose();
                return ExitError;
            }
        }

        // One-line summary to stdout (parseable by the end-to-end test).
        string outcomeLabel = result.Outcome switch
        {
            MainLoop.LoopOutcome.Victory => "victory",
            MainLoop.LoopOutcome.Defeat => "defeat",
            MainLoop.LoopOutcome.ScriptExhausted => "script_exhausted",
            MainLoop.LoopOutcome.Cancelled => "cancelled",
            _ => "unknown",
        };
        stdout.WriteLine(
            $"{outcomeLabel} | turns={result.TurnsPlayed} | final_state_sha256={finalHash}"
        );

        // --- Shutdown -----------------------------------------------------
        probe?.Dispose();
        metricsServer?.Dispose();

        return result.Outcome switch
        {
            MainLoop.LoopOutcome.Victory => ExitVictory,
            MainLoop.LoopOutcome.Defeat => ExitDefeat,
            MainLoop.LoopOutcome.Cancelled => ExitVictory, // graceful shutdown is success per spec
            MainLoop.LoopOutcome.ScriptExhausted => ExitError,
            _ => ExitError,
        };
    }

    // === Helpers ==========================================================

    private static byte[] BuildStateBlob(
        CombatState state,
        CompositionRoot.CompositionRootBundle bundle,
        byte[]? registryShaBytes
    )
    {
        // Token map: register the catalog ids so the codec's Tokens section
        // is non-empty. Smoke set: a stable, sorted union of all ids in the
        // bundle.
        var tokens = new TokenMap();
        foreach (string id in EnumerateAllCatalogIds(bundle))
        {
            tokens.GetOrAddId(id);
        }

        // RNG sets — placeholder: Phase 1 doesn't yet wire per-subsystem RNGs
        // through the combat path. Use empty/default sets. S13 will tighten
        // this.
        var runRng = new RunRngSet(stringSeed: $"seed-{bundle.Rng.Seed}");
        var playerRng = new PlayerRngSet(seed: bundle.Rng.Seed);

        // D6: when a --registry path was supplied, its SHA bytes (read once
        // at boot by RegistryShaProvider) own the ContentHash slot — the wire
        // position for state_blob.proto/registry_sha. Otherwise fall back to
        // the legacy catalog-id-derived hash so callers without a registry
        // (e.g. the S8-T7 golden test) keep their stable hash.
        byte[] contentHash =
            registryShaBytes ?? ManifestStamp.ContentHashFromIds(EnumerateAllCatalogIds(bundle));

        var stamp = new ManifestStamp(
            GitSha: "00000000",
            BuildId: "S8-T7",
            ContentHash: contentHash
        );

        return StateCodec.Serialize(state, runRng, playerRng, tokens, stamp);
    }

    private static string ComputeFinalStateSha(
        CombatState state,
        CompositionRoot.CompositionRootBundle bundle,
        byte[]? registryShaBytes
    )
    {
        byte[] blob = BuildStateBlob(state, bundle, registryShaBytes);
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(blob, hash);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
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
