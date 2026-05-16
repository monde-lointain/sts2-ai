using System.Globalization;
using System.IO;

namespace Sts2Q1ConsoleHost;

/// <summary>
/// <para>
/// <b>P-1.5-1.α foundation.</b> Console host for the Phase-1.5 per-step
/// Godot probe. Loads upstream <c>sts2.dll</c> (no scene-tree mount) and
/// reflects the <c>CombatManager</c> + <c>Player</c> entry points so later
/// sub-streams can drive <c>StartCombatInternal</c> headlessly.
/// </para>
///
/// <para>
/// <b>Out of scope</b> for α: per-singleton stub bodies (β),
/// <c>Pinned&lt;TStub&gt;</c> harness (γ), per-step probe driver / output
/// JSONL schema (δ). α only ensures upstream binding succeeds and emits an
/// <c>upstream_bound</c> sentinel to stdout for downstream wiring.
/// </para>
///
/// <para>
/// <b>CLI:</b>
/// </para>
/// <code>
///   Sts2Q1ConsoleHost --sts2-dll &lt;path&gt; --seed &lt;uint&gt; --encounter &lt;id&gt; --out &lt;path&gt;
/// </code>
///
/// <para>
/// <b>Exit codes:</b>
/// </para>
/// <list type="bullet">
///   <item>0 — success (upstream bound, sentinel emitted, out file opened).</item>
///   <item>1 — CLI usage error.</item>
///   <item>2 — upstream-load / type-resolution error.</item>
/// </list>
/// </summary>
public static class Program
{
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitLoad = 2;

    public const string UsageText =
        "Usage: Sts2Q1ConsoleHost --sts2-dll <path> --seed <uint> --encounter <id> --out <path>\n"
        + "\n"
        + "Loads upstream sts2.dll, resolves CombatManager + Player via reflection,\n"
        + "and emits an upstream_bound sentinel JSON line on stdout. The per-step\n"
        + "driver (P-1.5-1.δ) populates the --out JSONL stream; α only opens it.";

    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Testable entrypoint. Returns an exit code; never throws.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        CliArgs cli;
        try
        {
            cli = CliArgs.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Sts2Q1ConsoleHost: {ex.Message}");
            stderr.WriteLine(UsageText);
            return ExitUsage;
        }

        if (!File.Exists(cli.Sts2DllPath))
        {
            stderr.WriteLine(
                $"Sts2Q1ConsoleHost: --sts2-dll path does not exist: {cli.Sts2DllPath}"
            );
            return ExitUsage;
        }

        // Open the --out file up front so a missing parent directory surfaces
        // as a usage error rather than a load error halfway through binding.
        // P-1.5-1.δ populates the stream; α just confirms it can be created.
        try
        {
            string? outDir = Path.GetDirectoryName(cli.OutputPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
            // Touch the file (create-or-truncate) to confirm we have write
            // access. The driver in δ re-opens this path in append mode.
#pragma warning disable SA1312 // discard pattern '_' is conventional
            using FileStream _ = new(
#pragma warning restore SA1312
                cli.OutputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine(
                $"Sts2Q1ConsoleHost: cannot open --out path '{cli.OutputPath}': {ex.Message}"
            );
            return ExitUsage;
        }

        try
        {
            CompositionRoot.BindUpstream(cli, stdout);
        }
        catch (CompositionRootException ex)
        {
            stderr.WriteLine($"Sts2Q1ConsoleHost: composition-root error: {ex.Message}");
            return ExitLoad;
        }

        return ExitOk;
    }
}

/// <summary>Parsed CLI args.</summary>
public sealed record CliArgs(string Sts2DllPath, uint Seed, string EncounterId, string OutputPath)
{
    public static CliArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? sts2Dll = null;
        uint? seed = null;
        string? encounter = null;
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            string Take()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"missing value for {flag}.");
                return args[++i];
            }
            switch (flag)
            {
                case "--sts2-dll":
                    sts2Dll = Take();
                    break;
                case "--seed":
                {
                    string v = Take();
                    if (
                        !uint.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out uint parsed
                        )
                    )
                    {
                        throw new ArgumentException($"--seed: expected uint, got '{v}'.");
                    }
                    seed = parsed;
                    break;
                }
                case "--encounter":
                    encounter = Take();
                    break;
                case "--out":
                    outputPath = Take();
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException("help requested.");
                default:
                    throw new ArgumentException($"unknown flag '{flag}'.");
            }
        }
        if (sts2Dll is null)
            throw new ArgumentException("--sts2-dll is required.");
        if (seed is null)
            throw new ArgumentException("--seed is required.");
        if (encounter is null)
            throw new ArgumentException("--encounter is required.");
        if (outputPath is null)
            throw new ArgumentException("--out is required.");
        return new CliArgs(sts2Dll, seed.Value, encounter, outputPath);
    }
}
