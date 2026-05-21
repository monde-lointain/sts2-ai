using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Sts2Headless.UpstreamCapture;

/// <summary>
/// Console host that links upstream sts2.dll directly and drives
/// <c>CombatManager.SetUpCombat</c> for a (seed, encounter) tuple, dumping
/// canonical bytes matching Q1's <c>StateByteSerializer</c> field order.
///
/// <para>
/// <b>Stream C invariant:</b> this binary MUST NOT touch any Godot scene-tree
/// singleton (NRunMusicController, NCombatRoom, NModalContainer, NCombatStartBanner,
/// Cmd.CustomScaledWait, SaveManager, RunManager.Instance.ActionExecutor,
/// NetCombatCardDb.Instance — last verified by code inspection in T1).
/// SetUpCombat's call chain stays scene-tree-free; everything after
/// (StartCombatInternal et al.) is the scene-tree gauntlet that S13 documented
/// as blocking.
/// </para>
///
/// <para>
/// <b>CLI:</b>
/// </para>
/// <code>
///   dotnet run -- --seed N --encounter id --out path
///   dotnet run -- --mode mid-combat-capture [--golden-out DIR]
/// </code>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            CliArgs cli = CliArgs.Parse(args);
            if (cli.ListEncounters)
            {
                foreach (string id in EncounterCatalog.AllKnownIds())
                {
                    Console.WriteLine(id);
                }
                return 0;
            }
            if (cli.Diagnose)
            {
                return DiagnoseDll();
            }
            // wave-49/A.2 E1: mid-combat-capture mode — regenerate
            // goldens-upstream/mid-combat/ from upstream via UpstreamDriver.CaptureMidCombat().
            if (cli.Mode == CliMode.MidCombatCapture)
            {
                return RunMidCombatCapture(cli);
            }
            if (cli.BatchOutDir is not null)
            {
                return RunBatch(cli);
            }
            return RunCapture(cli);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"upstream-capture: {ex.Message}");
            Console.Error.WriteLine(CliArgs.Usage);
            return 2;
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                Console.Error.WriteLine($"upstream-capture: {e.GetType().Name}: {e.Message}");
            }
            Console.Error.WriteLine(ex.ToString());
            return 3;
        }
    }

    /// <summary>
    /// Batch mode: produce all (encounter, seed) tuples in one driver process.
    /// Outputs go to &lt;batch-out-dir&gt;/&lt;encounter&gt;/&lt;seed&gt;.bin (and .missing
    /// for MissingUpstream entries). Single sts2.dll load + single ModelDb
    /// init amortizes the 1-2s startup over all 220 captures.
    /// </summary>
    private static int RunBatch(CliArgs cli)
    {
        string outDir = cli.BatchOutDir!;
        int[] seeds = cli.BatchSeeds ?? Enumerable.Range(42, 10).ToArray();
        IReadOnlyList<string> encounters = cli.BatchEncounters ?? EncounterCatalog.AllKnownIds();

        Directory.CreateDirectory(outDir);
        var driver = new UpstreamDriver();
        int captured = 0,
            missing = 0,
            errored = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (string encId in encounters)
        {
            EncounterCatalog.EncounterPlan plan = EncounterCatalog.Resolve(encId);
            string encDir = Path.Combine(outDir, encId);
            Directory.CreateDirectory(encDir);
            foreach (int seed in seeds)
            {
                string outPath = Path.Combine(encDir, $"{seed}.bin");
                if (plan.Kind == EncounterCatalog.PlanKind.MissingUpstream)
                {
                    File.WriteAllText(
                        outPath + ".missing",
                        string.Join(
                            "\n",
                            $"encounter={encId}",
                            $"reason={plan.Reason}",
                            $"monsters={string.Join(",", plan.MonsterIds)}",
                            ""
                        )
                    );
                    File.WriteAllBytes(outPath, Array.Empty<byte>());
                    missing++;
                    continue;
                }
                try
                {
                    driver.ResetCombatManagerState();
                    byte[] bytes = driver.Capture(seed: seed, plan: plan);
                    File.WriteAllBytes(outPath, bytes);
                    captured++;
                    Console.Out.WriteLine($"  ok    {encId} seed={seed} ({bytes.Length} bytes)");
                }
                catch (Exception ex)
                {
                    errored++;
                    File.WriteAllText(
                        outPath + ".error",
                        $"{ex.GetType().Name}: {(ex.InnerException ?? ex).Message}\n{ex}"
                    );
                    File.WriteAllBytes(outPath, Array.Empty<byte>());
                    Console.Error.WriteLine(
                        $"  ERROR {encId} seed={seed}: {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}"
                    );
                }
            }
        }
        sw.Stop();
        Console.Out.WriteLine(
            $"upstream-capture: batch — captured={captured} missing={missing} errored={errored} "
                + $"duration={sw.Elapsed.TotalSeconds:F1}s out={outDir}"
        );
        return errored > 0 ? 5 : 0;
    }

    /// <summary>Inspect sts2.dll: list namespaces + key types so we can debug type-resolution.</summary>
    private static int DiagnoseDll()
    {
        // Use UpstreamDriver's load logic (same probing path the real
        // capture uses) so diagnose output reflects reality.
        var driver = new UpstreamDriver();
        Assembly asm = driver.Sts2Assembly;
        Console.WriteLine($"loaded: {asm.FullName}");
        Console.WriteLine($"location: {asm.Location}");
        Type[] allTypes;
        try
        {
            allTypes = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Console.Error.WriteLine(
                $"GetTypes threw ReflectionTypeLoadException — partial load. {ex.LoaderExceptions.Length} loader exceptions; using ex.Types."
            );
            allTypes = ex.Types.Where(t => t is not null).ToArray()!;
            foreach (var le in ex.LoaderExceptions.Take(3))
            {
                Console.Error.WriteLine($"  loader: {le?.GetType().Name}: {le?.Message}");
            }
        }
        Console.WriteLine($"types: {allTypes.Length}");
        var namespaces = allTypes
            .Select(t => t.Namespace)
            .Where(n => n is not null)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        Console.WriteLine($"namespaces: {namespaces.Count}");
        foreach (var n in namespaces)
            Console.WriteLine($"  ns: {n}");
        Console.WriteLine("--- types whose name = 'Player':");
        foreach (var t in allTypes.Where(t => t.Name == "Player").Take(5))
            Console.WriteLine($"  {t.FullName}");
        Console.WriteLine("--- types whose name = 'CombatManager':");
        foreach (var t in allTypes.Where(t => t.Name == "CombatManager").Take(5))
            Console.WriteLine($"  {t.FullName}");
        Console.WriteLine("--- types whose name = 'CombatState':");
        foreach (var t in allTypes.Where(t => t.Name == "CombatState").Take(5))
            Console.WriteLine($"  {t.FullName}");
        Console.WriteLine("--- types whose name = 'Silent':");
        foreach (var t in allTypes.Where(t => t.Name == "Silent").Take(5))
            Console.WriteLine($"  {t.FullName}");
        Console.WriteLine("--- types whose name = 'CalcifiedCultist':");
        foreach (var t in allTypes.Where(t => t.Name == "CalcifiedCultist").Take(5))
            Console.WriteLine($"  {t.FullName}");
        return 0;
    }

    /// <summary>
    /// Mid-combat-capture mode (wave-49/A.2 E1): iterate all encounters with a
    /// <c>MidCombatActionSequenceId</c> in <see cref="EncounterCatalog"/>, drive
    /// <see cref="UpstreamDriver.CaptureMidCombat"/> for seeds 42–51, and write
    /// <see cref="Sts2Headless.DeterminismProbe.MidCombatRecord"/> binary files to
    /// <c>goldens-upstream/mid-combat/{encounter}/{seed}.bin</c>.
    ///
    /// <para>
    /// Output directory defaults to the probe project's
    /// <c>goldens-upstream/mid-combat/</c> (resolved by walking up from AppContext.BaseDirectory
    /// looking for the probe .csproj). Override with <c>--golden-out DIR</c>.
    /// </para>
    ///
    /// <para>
    /// The action-sequence JSON files are read from the same probe project's
    /// <c>goldens-upstream/mid-combat/action-sequences/</c> directory.
    /// </para>
    /// </summary>
    private static int RunMidCombatCapture(CliArgs cli)
    {
        string probeDir = LocateProbeDir();
        string midCombatDir = cli.GoldenOutDir
            ?? Path.Combine(probeDir, "goldens-upstream", "mid-combat");
        string actionSeqDir = Path.Combine(probeDir, "goldens-upstream", "mid-combat", "action-sequences");

        if (!Directory.Exists(actionSeqDir))
        {
            Console.Error.WriteLine(
                $"upstream-capture: action-sequences dir not found: {actionSeqDir}. "
                + "Ensure the probe project is present at the expected relative path."
            );
            return 2;
        }

        int[] seeds = Enumerable.Range(42, 10).ToArray();
        var driver = new UpstreamDriver();
        int captured = 0, skipped = 0, errored = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Console.Out.WriteLine(
            $"==> upstream-capture mid-combat-capture: goldensDir={midCombatDir} seeds={seeds.Length}"
        );

        foreach (string encId in EncounterCatalog.AllKnownIds())
        {
            EncounterCatalog.EncounterPlan plan = EncounterCatalog.Resolve(encId);

            // Only process encounters that have a mid-combat action sequence.
            if (plan.MidCombatActionSequenceId is null)
            {
                Console.Out.WriteLine($"  SKIP    {encId} (no action-sequence)");
                skipped += seeds.Length;
                continue;
            }

            string seqPath = Path.Combine(actionSeqDir, plan.MidCombatActionSequenceId);
            if (!File.Exists(seqPath))
            {
                Console.Out.WriteLine(
                    $"  SKIP    {encId} (action-seq file not found: {plan.MidCombatActionSequenceId})"
                );
                skipped += seeds.Length;
                continue;
            }

            Sts2Headless.DeterminismProbe.MidCombatActionPlan actionPlan;
            try
            {
                actionPlan = Sts2Headless.DeterminismProbe.MidCombatActionPlan.LoadFromFile(seqPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"  ERROR   {encId}: failed to load action-seq '{seqPath}': {ex.Message}"
                );
                errored += seeds.Length;
                continue;
            }

            string encDir = Path.Combine(midCombatDir, encId);
            Directory.CreateDirectory(encDir);

            foreach (int seed in seeds)
            {
                string goldenPath = Path.Combine(encDir, $"{seed}.bin");
                try
                {
                    driver.ResetCombatManagerState();
                    IReadOnlyList<Sts2Headless.DeterminismProbe.MidCombatRecord> records =
                        driver.CaptureMidCombat(seed, plan, actionPlan);
                    Sts2Headless.DeterminismProbe.MidCombatRecord.WriteFile(goldenPath, records);
                    captured++;
                    Console.Out.WriteLine(
                        $"  ok      {encId} seed={seed} records={records.Count} ({new FileInfo(goldenPath).Length} bytes)"
                    );
                }
                catch (Exception ex)
                {
                    errored++;
                    string errPath = goldenPath + ".error";
                    File.WriteAllText(
                        errPath,
                        $"{ex.GetType().Name}: {(ex.InnerException ?? ex).Message}\n{ex}"
                    );
                    Console.Error.WriteLine(
                        $"  ERROR   {encId} seed={seed}: {(ex.InnerException ?? ex).Message}"
                    );
                }
            }
        }

        sw.Stop();
        Console.Out.WriteLine(
            $"upstream-capture: mid-combat-capture summary — "
            + $"captured={captured} skipped={skipped} errored={errored} "
            + $"duration={sw.Elapsed.TotalSeconds:F1}s goldens={midCombatDir}"
        );
        return errored > 0 ? 5 : 0;
    }

    /// <summary>
    /// Locate the determinism-probe project directory by walking up from the
    /// running assembly's AppContext.BaseDirectory. Used to resolve default
    /// golden + action-sequence paths for mid-combat-capture mode.
    /// </summary>
    private static string LocateProbeDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "Sts2Headless.DeterminismProbe.csproj")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: look for probe relative to upstream-capture project location.
        string? capDir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && capDir is not null; i++)
        {
            string candidate = Path.Combine(capDir, "test", "determinism-probe");
            if (Directory.Exists(candidate))
                return candidate;
            string candidate2 = Path.Combine(capDir, "determinism-probe");
            if (Directory.Exists(candidate2))
                return candidate2;
            capDir = Path.GetDirectoryName(capDir);
        }
        return Directory.GetCurrentDirectory();
    }

    private static int RunCapture(CliArgs cli)
    {
        if (cli.OutputPath is null)
        {
            throw new ArgumentException("--out is required.");
        }
        if (cli.EncounterId is null)
        {
            throw new ArgumentException("--encounter is required.");
        }

        EncounterCatalog.EncounterPlan plan = EncounterCatalog.Resolve(cli.EncounterId);

        if (plan.Kind == EncounterCatalog.PlanKind.MissingUpstream)
        {
            // Write a sentinel "MISSING_UPSTREAM" file so the probe knows the
            // encounter cannot be upstream-compared and why.
            Directory.CreateDirectory(Path.GetDirectoryName(cli.OutputPath)!);
            File.WriteAllText(
                cli.OutputPath + ".missing",
                string.Join(
                    "\n",
                    $"encounter={cli.EncounterId}",
                    $"reason={plan.Reason}",
                    $"monsters={string.Join(",", plan.MonsterIds)}",
                    ""
                )
            );
            // Also produce a zero-length .bin so callers consistently see a file.
            File.WriteAllBytes(cli.OutputPath, Array.Empty<byte>());
            Console.Error.WriteLine(
                $"upstream-capture: {cli.EncounterId}: MISSING_UPSTREAM ({plan.Reason})"
            );
            return 4; // distinct exit code for missing upstream
        }

        // Drive upstream sts2.dll. Reflection-only so we don't need compile-time
        // type references to the upstream assemblies (which would force the
        // .csproj to track every transitively-touched upstream type).
        UpstreamDriver driver = new UpstreamDriver();
        byte[] canonicalBytes = driver.Capture(seed: cli.Seed, plan: plan);

        Directory.CreateDirectory(Path.GetDirectoryName(cli.OutputPath)!);
        File.WriteAllBytes(cli.OutputPath, canonicalBytes);
        Console.Out.WriteLine(
            $"upstream-capture: wrote {canonicalBytes.Length} bytes to {cli.OutputPath}"
        );
        return 0;
    }
}

/// <summary>CLI operation mode for upstream-capture.</summary>
public enum CliMode
{
    /// <summary>Default: single (seed, encounter) capture to --out path.</summary>
    SingleCapture,

    /// <summary>
    /// wave-49/A.2 E1: iterate encounters with MidCombatActionSequenceId, drive
    /// UpstreamDriver.CaptureMidCombat, write goldens-upstream/mid-combat/ goldens.
    /// </summary>
    MidCombatCapture,
}

/// <summary>Parsed CLI args.</summary>
public sealed record CliArgs(
    int Seed,
    string? EncounterId,
    string? OutputPath,
    bool ListEncounters,
    bool Diagnose,
    string? BatchOutDir,
    int[]? BatchSeeds,
    string[]? BatchEncounters,
    CliMode Mode = CliMode.SingleCapture,
    string? GoldenOutDir = null
)
{
    public const string Usage =
        "Usage: upstream-capture --seed N --encounter ID --out PATH\n"
        + "       upstream-capture --batch-out DIR [--seeds 42,43,...] [--encounters ID,ID,...]\n"
        + "       upstream-capture --mode mid-combat-capture [--golden-out DIR]\n"
        + "       upstream-capture --list-encounters\n"
        + "       upstream-capture --diagnose";

    public static CliArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int? seed = null;
        string? encounter = null;
        string? outputPath = null;
        bool listEncounters = false;
        bool diagnose = false;
        string? batchOutDir = null;
        int[]? batchSeeds = null;
        string[]? batchEncounters = null;
        CliMode mode = CliMode.SingleCapture;
        string? goldenOutDir = null;
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
                case "--seed":
                {
                    string v = Take();
                    if (
                        !int.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed
                        )
                    )
                    {
                        throw new ArgumentException($"--seed: expected int, got '{v}'.");
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
                case "--batch-out":
                    batchOutDir = Take();
                    break;
                case "--golden-out":
                    goldenOutDir = Take();
                    break;
                case "--seeds":
                {
                    string v = Take();
                    var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var parsed = new int[parts.Length];
                    for (int k = 0; k < parts.Length; k++)
                    {
                        if (
                            !int.TryParse(
                                parts[k],
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out parsed[k]
                            )
                        )
                        {
                            throw new ArgumentException($"--seeds: invalid int '{parts[k]}'.");
                        }
                    }
                    batchSeeds = parsed;
                    break;
                }
                case "--encounters":
                {
                    string v = Take();
                    batchEncounters = v.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    break;
                }
                case "--mode":
                {
                    string v = Take();
                    mode = v switch
                    {
                        "mid-combat-capture" => CliMode.MidCombatCapture,
                        _ => throw new ArgumentException($"--mode: unknown mode '{v}' (supported: mid-combat-capture)."),
                    };
                    break;
                }
                case "--list-encounters":
                    listEncounters = true;
                    break;
                case "--diagnose":
                    diagnose = true;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"unknown flag '{flag}'.");
            }
        }
        if (listEncounters)
        {
            return new CliArgs(0, null, null, true, false, null, null, null);
        }
        if (diagnose)
        {
            return new CliArgs(0, null, null, false, true, null, null, null);
        }
        if (mode == CliMode.MidCombatCapture)
        {
            return new CliArgs(
                0,
                null,
                null,
                false,
                false,
                null,
                null,
                null,
                Mode: CliMode.MidCombatCapture,
                GoldenOutDir: goldenOutDir
            );
        }
        if (batchOutDir is not null)
        {
            return new CliArgs(
                0,
                null,
                null,
                false,
                false,
                batchOutDir,
                batchSeeds,
                batchEncounters
            );
        }
        if (seed is null)
            throw new ArgumentException("--seed is required.");
        return new CliArgs(seed.Value, encounter, outputPath, false, false, null, null, null);
    }
}
