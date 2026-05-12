using System.Globalization;
using Sts2Headless.DeterminismProbe;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Driver for the S13 determinism probe. Reads the corpus, runs each entry
/// through Q1 in-process, compares per-step hashes against the goldens, and
/// reports PASS / FAIL / ERROR with the first divergence.
///
/// <para>
/// <b>CLI:</b>
/// </para>
/// <code>
///   --mode (quick | full | structural | per-step | initial-state | capture | generate-corpus)
///   [--corpus &lt;path&gt;]          (default: corpus/phase1-corpus.json)
///   [--goldens &lt;path&gt;]         (default: goldens/)
///   [--smoke-seeds &lt;N&gt;]        (default: 5 for quick; per-mode override)
///   [--verbose]                  (print per-entry status, not just summary)
/// </code>
/// </summary>
public static class Program
{
    public const int ExitPass = 0;
    public const int ExitFail = 1;
    public const int ExitError = 2;

    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>Test-friendly entry point; identical to Main but lets callers swap writers.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ProbeCli cli;
        try
        {
            cli = ProbeCli.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"determinism-probe: {ex.Message}");
            stderr.WriteLine(ProbeCli.Usage);
            return ExitError;
        }

        // Resolve corpus + goldens paths.
        string corpusPath = cli.CorpusPath ?? DefaultCorpusPath();
        string goldensDir = cli.GoldensDir ?? DefaultGoldensDir();

        // generate-corpus mode: just emit the JSON and exit.
        if (cli.Mode == ProbeMode.GenerateCorpus)
        {
            Corpus generated = CorpusGenerator.BuildPhase1Corpus();
            Directory.CreateDirectory(Path.GetDirectoryName(corpusPath)!);
            File.WriteAllText(corpusPath, generated.ToJson());
            stdout.WriteLine($"determinism-probe: corpus written to {corpusPath} ({generated.Entries.Count} entries).");
            return ExitPass;
        }

        // initial-state-upstream mode: a parallel branch that reads byte goldens
        // produced by test/determinism-probe-upstream-capture (Stream-C-T3) and
        // byte-compares Q1's post-SetUpCombat snapshot against them. Doesn't use
        // the per-step hash flow, so it's wired before the SelectEntries() path.
        if (cli.Mode == ProbeMode.InitialStateUpstream)
        {
            return RunInitialStateUpstream(cli, stdout, stderr);
        }

        // Load the corpus.
        if (!File.Exists(corpusPath))
        {
            stderr.WriteLine(
                $"determinism-probe: corpus '{corpusPath}' does not exist. " +
                "Run with --mode generate-corpus first.");
            return ExitError;
        }
        Corpus corpus;
        try
        {
            corpus = Corpus.FromJson(File.ReadAllText(corpusPath));
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"determinism-probe: failed to load corpus '{corpusPath}': {ex.Message}");
            return ExitError;
        }

        // Select the entries to run for this mode.
        IReadOnlyList<CorpusEntry> entries = SelectEntries(corpus, cli);
        stdout.WriteLine($"determinism-probe: mode={cli.Mode} entries={entries.Count} goldens={goldensDir}");

        // Run each entry.
        bool captureMode = cli.Mode == ProbeMode.Capture;
        var runner = new ProbeRunner(goldensDir);
        int passed = 0, failed = 0, errored = 0, captured = 0;
        ProbeRunner.EntryResult? firstFailure = null;
        var startTime = System.Diagnostics.Stopwatch.StartNew();

        foreach (CorpusEntry entry in entries)
        {
            ProbeRunner.EntryResult res = runner.RunEntry(entry, captureMode);
            switch (res.Outcome)
            {
                case ProbeRunner.EntryOutcome.Pass:
                    passed++;
                    if (cli.Verbose) stdout.WriteLine($"  PASS {entry.Id} ({res.Duration.TotalMilliseconds:F0}ms)");
                    break;
                case ProbeRunner.EntryOutcome.Captured:
                    captured++;
                    if (cli.Verbose) stdout.WriteLine($"  CAPTURED {entry.Id} ({res.Duration.TotalMilliseconds:F0}ms)");
                    break;
                case ProbeRunner.EntryOutcome.Diverged:
                    failed++;
                    firstFailure ??= res;
                    if (cli.Verbose)
                        stdout.WriteLine($"  FAIL {entry.Id}: {res.Divergence!.Reason}");
                    break;
                case ProbeRunner.EntryOutcome.Error:
                    errored++;
                    firstFailure ??= res;
                    if (cli.Verbose)
                        stdout.WriteLine($"  ERROR {entry.Id}: {res.ErrorMessage}");
                    break;
            }
        }
        startTime.Stop();

        // Summary.
        stdout.WriteLine($"determinism-probe: summary — passed={passed} captured={captured} failed={failed} errored={errored} duration={startTime.Elapsed.TotalSeconds:F2}s");
        if (firstFailure is not null)
        {
            stderr.WriteLine("determinism-probe: first failure ↓");
            stderr.WriteLine($"  entry: {firstFailure.Entry.Id}");
            stderr.WriteLine($"  mode: {firstFailure.Entry.Mode}  seed: {firstFailure.Entry.Seed}  encounter: {firstFailure.Entry.Encounter}");
            if (firstFailure.ErrorMessage is not null)
            {
                stderr.WriteLine($"  error: {firstFailure.ErrorMessage}");
            }
            if (firstFailure.Divergence is not null)
            {
                ProbeRunner.DivergencePoint d = firstFailure.Divergence;
                stderr.WriteLine($"  divergence at step {d.StepIndex}: {d.Reason}");
                stderr.WriteLine($"    Q1: turn={d.Q1Record.Turn} phase={d.Q1Record.Phase} hash={d.Q1Record.Hash}");
                if (d.GoldenRecord is not null)
                {
                    stderr.WriteLine($"    golden: turn={d.GoldenRecord.Turn} phase={d.GoldenRecord.Phase} hash={d.GoldenRecord.Hash}");
                }
            }
        }

        if (errored > 0) return ExitError;
        if (failed > 0) return ExitFail;
        return ExitPass;
    }

    /// <summary>
    /// Run the upstream byte-comparison sweep. Iterates the Phase-1
    /// encounters x 10 seeds (42..51) and reports per-entry PASS / DIVERGED /
    /// SKIPPED / ERROR. Skipped entries are the documented Q1-vs-upstream
    /// content gaps from <c>test/determinism-probe-upstream-capture/src/EncounterCatalog.cs</c>
    /// (Q1 invented encounters whose monsters don't exist in upstream STS2).
    ///
    /// <para>B.1-final-T2: 7 STS1-only encounters deleted (JawWormSolo,
    /// TwoLouseNormal, LargeSlimeBoss, SentryTrio, SnakePlantSolo,
    /// FungalBossEncounter, CenturyGuardBoss); 1 added (LouseProgenitorNormal);
    /// KaiserCrabBoss reshaped to Crusher+Rocket. Slimes (SmallSlimes,
    /// MediumSlimes) remain as DEFER (skip, pending B.1-ε encounter-RNG plumbing).</para>
    /// </summary>
    private static int RunInitialStateUpstream(ProbeCli cli, TextWriter stdout, TextWriter stderr)
    {
        string goldensRoot = cli.GoldensDir
            ?? Path.Combine(LocateProbeDir(), "goldens-upstream", "initial-state");
        if (!Directory.Exists(goldensRoot))
        {
            stderr.WriteLine(
                $"determinism-probe: goldens-upstream root '{goldensRoot}' does not exist. " +
                "Run `make probe-upstream-capture` first (Stream-C-T3).");
            return ExitError;
        }

        // 16 encounters x 10 seeds — matches the corpus + upstream-capture EncounterCatalog.
        IReadOnlyList<string> encounters = new[]
        {
            "CultistsNormal", "ChompersNormal", "ExoskeletonsNormal",
            "SmallSlimes", "MediumSlimes",
            "BowlbugsTrio", "FuzzyWurmCrawlerSolo", "FossilStalkerElite",
            "FrogKnightElite", "LagavulinElite", "HauntedShipSolo",
            "LivingFogSolo", "GremlinMercNormal", "KaiserCrabBoss",
            "CeremonialBeastBoss", "LouseProgenitorNormal",
        };
        int[] seeds = Enumerable.Range(42, 10).ToArray();

        var comparer = new UpstreamInitialStateComparer(goldensRoot);
        stdout.WriteLine($"determinism-probe: mode=initial-state-upstream entries={encounters.Count * seeds.Length} goldens={goldensRoot}");

        int passed = 0, diverged = 0, skipped = 0, errored = 0;
        var firstFailures = new List<UpstreamInitialStateComparer.EntryResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (string encId in encounters)
        {
            foreach (int seed in seeds)
            {
                UpstreamInitialStateComparer.EntryResult res = comparer.CompareOne(encId, seed);
                switch (res.Outcome)
                {
                    case UpstreamInitialStateComparer.EntryOutcome.Pass:
                        passed++;
                        if (cli.Verbose) stdout.WriteLine($"  PASS    {encId} seed={seed}");
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Diverged:
                        diverged++;
                        if (firstFailures.Count < 5) firstFailures.Add(res);
                        if (cli.Verbose)
                        {
                            stdout.WriteLine($"  DIVERGE {encId} seed={seed}");
                            foreach (string line in (res.DiffSummary ?? "").TrimEnd().Split('\n'))
                            {
                                if (line.Length > 0) stdout.WriteLine($"    {line}");
                            }
                        }
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Skipped:
                        skipped++;
                        if (cli.Verbose) stdout.WriteLine($"  SKIP    {encId} seed={seed} (MissingUpstream)");
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Error:
                        errored++;
                        if (firstFailures.Count < 5) firstFailures.Add(res);
                        if (cli.Verbose) stdout.WriteLine($"  ERROR   {encId} seed={seed}: {res.ErrorMessage}");
                        break;
                }
            }
        }
        sw.Stop();

        stdout.WriteLine(
            $"determinism-probe: initial-state-upstream summary — passed={passed} diverged={diverged} skipped={skipped} errored={errored} " +
            $"duration={sw.Elapsed.TotalSeconds:F2}s");

        // Always print per-entry table (not just on failure) so the report
        // captures the full canary picture.
        stdout.WriteLine();
        stdout.WriteLine("== per-entry table ==");
        foreach (string encId in encounters)
        {
            var rowParts = new List<string> { encId.PadRight(24) };
            foreach (int seed in seeds)
            {
                UpstreamInitialStateComparer.EntryResult res = comparer.CompareOne(encId, seed);
                rowParts.Add(res.Outcome switch
                {
                    UpstreamInitialStateComparer.EntryOutcome.Pass => "PASS",
                    UpstreamInitialStateComparer.EntryOutcome.Diverged => "DIVR",
                    UpstreamInitialStateComparer.EntryOutcome.Skipped => "SKIP",
                    UpstreamInitialStateComparer.EntryOutcome.Error => "ERR ",
                    _ => "??? ",
                });
            }
            stdout.WriteLine(string.Join("  ", rowParts));
        }

        if (diverged > 0 || errored > 0)
        {
            stderr.WriteLine();
            stderr.WriteLine($"determinism-probe: {firstFailures.Count} failure(s) — first 5 with diff summary ↓");
            foreach (var f in firstFailures)
            {
                stderr.WriteLine($"-- {f.EncounterId} seed={f.Seed} outcome={f.Outcome}");
                if (f.ErrorMessage is not null) stderr.WriteLine($"   error: {f.ErrorMessage}");
                if (f.DiffSummary is not null)
                {
                    foreach (string line in f.DiffSummary.TrimEnd().Split('\n'))
                    {
                        if (line.Length > 0) stderr.WriteLine($"   {line}");
                    }
                }
            }
        }

        // Exit code: ExitPass if all comparable entries pass (skipped doesn't fail),
        // ExitFail if any divergence, ExitError if any I/O / construction error.
        if (errored > 0) return ExitError;
        if (diverged > 0) return ExitFail;
        return ExitPass;
    }

    /// <summary>
    /// Filter the corpus to the entries relevant for the requested mode.
    /// </summary>
    private static IReadOnlyList<CorpusEntry> SelectEntries(Corpus corpus, ProbeCli cli)
    {
        switch (cli.Mode)
        {
            case ProbeMode.Structural:
                return corpus.Entries.Where(e => e.Mode == CorpusEntry.ModeStructural).ToList();
            case ProbeMode.InitialState:
                return corpus.Entries.Where(e => e.Mode == CorpusEntry.ModeInitialState).ToList();
            case ProbeMode.PerStep:
                {
                    var list = corpus.Entries.Where(e => e.Mode == CorpusEntry.ModePerStep).ToList();
                    if (cli.SmokeSeeds.HasValue && cli.SmokeSeeds.Value < list.Count)
                    {
                        return list.Take(cli.SmokeSeeds.Value).ToList();
                    }
                    return list;
                }
            case ProbeMode.Quick:
                {
                    var structural = corpus.Entries.Where(e => e.Mode == CorpusEntry.ModeStructural).ToList();
                    int smokeN = cli.SmokeSeeds ?? 5;
                    var perStep = corpus.Entries.Where(e => e.Mode == CorpusEntry.ModePerStep).Take(smokeN);
                    return structural.Concat(perStep).ToList();
                }
            case ProbeMode.Full:
            case ProbeMode.Capture:
                return corpus.Entries;
            default:
                throw new InvalidOperationException($"unhandled mode {cli.Mode}.");
        }
    }

    /// <summary>Discover the corpus path relative to the worktree root.</summary>
    private static string DefaultCorpusPath()
    {
        string baseDir = LocateProbeDir();
        return Path.Combine(baseDir, "corpus", "phase1-corpus.json");
    }

    /// <summary>Discover the goldens directory relative to the worktree root.</summary>
    private static string DefaultGoldensDir()
    {
        string baseDir = LocateProbeDir();
        return Path.Combine(baseDir, "goldens");
    }

    /// <summary>
    /// Locate the probe project directory by walking up from the running
    /// assembly's location. Used by the default corpus / goldens paths so
    /// callers don't have to specify them.
    /// </summary>
    private static string LocateProbeDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            // Probe project root has the .csproj file and a corpus/ directory.
            if (File.Exists(Path.Combine(dir, "Sts2Headless.DeterminismProbe.csproj")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: current directory.
        return Directory.GetCurrentDirectory();
    }
}

/// <summary>Probe operational mode (selects which corpus subset to run).</summary>
public enum ProbeMode
{
    /// <summary>Smoke 5 seeds + all structural (default budget &lt; 60s).</summary>
    Quick,
    /// <summary>Full corpus.</summary>
    Full,
    /// <summary>Structural per-encounter only.</summary>
    Structural,
    /// <summary>Initial-state across all encounters / seeds.</summary>
    InitialState,
    /// <summary>
    /// Byte-compare Q1's post-SetUpCombat snapshot against upstream-derived
    /// goldens (Stream-C-T3). Reads from <c>goldens-upstream/initial-state/</c>.
    /// </summary>
    InitialStateUpstream,
    /// <summary>Per-step smoke entries only.</summary>
    PerStep,
    /// <summary>Capture mode: overwrite all goldens with the current Q1 trace.</summary>
    Capture,
    /// <summary>Generate and write phase1-corpus.json from <see cref="CorpusGenerator"/>.</summary>
    GenerateCorpus,
}

/// <summary>Parsed CLI args for the probe driver.</summary>
public sealed record ProbeCli(
    ProbeMode Mode,
    string? CorpusPath,
    string? GoldensDir,
    int? SmokeSeeds,
    bool Verbose)
{
    public const string Usage =
        "Usage: determinism-probe --mode <mode> [--corpus path] [--goldens dir] [--smoke-seeds N] [--verbose]\n" +
        "  modes: quick, full, structural, per-step, initial-state, initial-state-upstream, capture, generate-corpus";

    public static ProbeCli Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        ProbeMode? mode = null;
        string? corpusPath = null;
        string? goldensDir = null;
        int? smokeSeeds = null;
        bool verbose = false;
        int i = 0;
        while (i < args.Length)
        {
            string token = args[i];
            string? inlineValue = null;
            string flag = token;
            int eq = token.IndexOf('=');
            if (token.StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                flag = token[..eq];
                inlineValue = token[(eq + 1)..];
            }
            string Take()
            {
                if (inlineValue is not null) return inlineValue;
                if (i + 1 >= args.Length) throw new ArgumentException($"missing value for {flag}.");
                return args[++i];
            }
            switch (flag)
            {
                case "--mode":
                    mode = ParseMode(Take());
                    break;
                case "--corpus":
                    corpusPath = Take();
                    break;
                case "--goldens":
                    goldensDir = Take();
                    break;
                case "--smoke-seeds":
                    {
                        string v = Take();
                        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                        {
                            throw new ArgumentException($"--smoke-seeds: expected positive int, got '{v}'.");
                        }
                        smokeSeeds = parsed;
                        break;
                    }
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"unknown flag '{flag}'.");
            }
            i++;
        }
        if (mode is null) throw new ArgumentException("--mode is required.");
        return new ProbeCli(mode.Value, corpusPath, goldensDir, smokeSeeds, verbose);
    }

    private static ProbeMode ParseMode(string s) => s switch
    {
        "quick" => ProbeMode.Quick,
        "full" => ProbeMode.Full,
        "structural" => ProbeMode.Structural,
        "per-step" => ProbeMode.PerStep,
        "initial-state" => ProbeMode.InitialState,
        "initial-state-upstream" => ProbeMode.InitialStateUpstream,
        "capture" => ProbeMode.Capture,
        "generate-corpus" => ProbeMode.GenerateCorpus,
        _ => throw new ArgumentException(
            $"--mode '{s}' unknown (expected: quick|full|structural|per-step|initial-state|initial-state-upstream|capture|generate-corpus)."),
    };
}
