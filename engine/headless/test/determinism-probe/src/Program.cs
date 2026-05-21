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
            stdout.WriteLine(
                $"determinism-probe: corpus written to {corpusPath} ({generated.Entries.Count} entries)."
            );
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

        // mid-combat modes: parallel system to per-step probe (wave-45/Q1-A1).
        if (cli.Mode == ProbeMode.MidCombat || cli.Mode == ProbeMode.MidCombatCapture)
        {
            return RunMidCombat(cli, stdout, stderr);
        }

        // roundtrip-test: self-test the MidCombatRecord binary wire format.
        if (cli.Mode == ProbeMode.RoundtripTest)
        {
            return RunRoundtripTest(stdout, stderr);
        }

        // Load the corpus.
        if (!File.Exists(corpusPath))
        {
            stderr.WriteLine(
                $"determinism-probe: corpus '{corpusPath}' does not exist. "
                    + "Run with --mode generate-corpus first."
            );
            return ExitError;
        }
        Corpus corpus;
        try
        {
            corpus = Corpus.FromJson(File.ReadAllText(corpusPath));
        }
        catch (Exception ex)
        {
            stderr.WriteLine(
                $"determinism-probe: failed to load corpus '{corpusPath}': {ex.Message}"
            );
            return ExitError;
        }

        // Select the entries to run for this mode.
        IReadOnlyList<CorpusEntry> entries = SelectEntries(corpus, cli);
        stdout.WriteLine(
            $"determinism-probe: mode={cli.Mode} entries={entries.Count} goldens={goldensDir}"
        );

        // Run each entry.
        bool captureMode = cli.Mode == ProbeMode.Capture;
        var runner = new ProbeRunner(goldensDir);
        int passed = 0,
            failed = 0,
            errored = 0,
            captured = 0;
        ProbeRunner.EntryResult? firstFailure = null;
        var startTime = System.Diagnostics.Stopwatch.StartNew();

        foreach (CorpusEntry entry in entries)
        {
            ProbeRunner.EntryResult res = runner.RunEntry(entry, captureMode);
            switch (res.Outcome)
            {
                case ProbeRunner.EntryOutcome.Pass:
                    passed++;
                    if (cli.Verbose)
                        stdout.WriteLine(
                            $"  PASS {entry.Id} ({res.Duration.TotalMilliseconds:F0}ms)"
                        );
                    break;
                case ProbeRunner.EntryOutcome.Captured:
                    captured++;
                    if (cli.Verbose)
                        stdout.WriteLine(
                            $"  CAPTURED {entry.Id} ({res.Duration.TotalMilliseconds:F0}ms)"
                        );
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
        stdout.WriteLine(
            $"determinism-probe: summary — passed={passed} captured={captured} failed={failed} errored={errored} duration={startTime.Elapsed.TotalSeconds:F2}s"
        );
        if (firstFailure is not null)
        {
            stderr.WriteLine("determinism-probe: first failure ↓");
            stderr.WriteLine($"  entry: {firstFailure.Entry.Id}");
            stderr.WriteLine(
                $"  mode: {firstFailure.Entry.Mode}  seed: {firstFailure.Entry.Seed}  encounter: {firstFailure.Entry.Encounter}"
            );
            if (firstFailure.ErrorMessage is not null)
            {
                stderr.WriteLine($"  error: {firstFailure.ErrorMessage}");
            }
            if (firstFailure.Divergence is not null)
            {
                ProbeRunner.DivergencePoint d = firstFailure.Divergence;
                stderr.WriteLine($"  divergence at step {d.StepIndex}: {d.Reason}");
                stderr.WriteLine(
                    $"    Q1: turn={d.Q1Record.Turn} phase={d.Q1Record.Phase} hash={d.Q1Record.Hash}"
                );
                if (d.GoldenRecord is not null)
                {
                    stderr.WriteLine(
                        $"    golden: turn={d.GoldenRecord.Turn} phase={d.GoldenRecord.Phase} hash={d.GoldenRecord.Hash}"
                    );
                }
            }
        }

        if (errored > 0)
            return ExitError;
        if (failed > 0)
            return ExitFail;
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
        string goldensRoot =
            cli.GoldensDir ?? Path.Combine(LocateProbeDir(), "goldens-upstream", "initial-state");
        if (!Directory.Exists(goldensRoot))
        {
            stderr.WriteLine(
                $"determinism-probe: goldens-upstream root '{goldensRoot}' does not exist. "
                    + "Run `make probe-upstream-capture` first (Stream-C-T3)."
            );
            return ExitError;
        }

        // 18 encounters x 10 seeds — matches the corpus + upstream-capture EncounterCatalog.
        // wave-47a/C: added NibbitsWeak + NibbitsNormal (wave-46 deferred item; 160→180).
        IReadOnlyList<string> encounters = new[]
        {
            "CultistsNormal",
            "ChompersNormal",
            "ExoskeletonsNormal",
            "SmallSlimes",
            "MediumSlimes",
            "BowlbugsTrio",
            "FuzzyWurmCrawlerSolo",
            "FossilStalkerElite",
            "FrogKnightElite",
            "LagavulinElite",
            "HauntedShipSolo",
            "LivingFogSolo",
            "GremlinMercNormal",
            "KaiserCrabBoss",
            "CeremonialBeastBoss",
            "LouseProgenitorNormal",
            "NibbitsWeak",
            "NibbitsNormal",
        };
        int[] seeds = Enumerable.Range(42, 10).ToArray();

        var comparer = new UpstreamInitialStateComparer(goldensRoot);
        stdout.WriteLine(
            $"determinism-probe: mode=initial-state-upstream entries={encounters.Count * seeds.Length} goldens={goldensRoot}"
        );

        int passed = 0,
            diverged = 0,
            skipped = 0,
            errored = 0;
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
                        if (cli.Verbose)
                            stdout.WriteLine($"  PASS    {encId} seed={seed}");
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Diverged:
                        diverged++;
                        if (firstFailures.Count < 5)
                            firstFailures.Add(res);
                        if (cli.Verbose)
                        {
                            stdout.WriteLine($"  DIVERGE {encId} seed={seed}");
                            foreach (string line in (res.DiffSummary ?? "").TrimEnd().Split('\n'))
                            {
                                if (line.Length > 0)
                                    stdout.WriteLine($"    {line}");
                            }
                        }
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Skipped:
                        skipped++;
                        if (cli.Verbose)
                            stdout.WriteLine($"  SKIP    {encId} seed={seed} (MissingUpstream)");
                        break;
                    case UpstreamInitialStateComparer.EntryOutcome.Error:
                        errored++;
                        if (firstFailures.Count < 5)
                            firstFailures.Add(res);
                        if (cli.Verbose)
                            stdout.WriteLine($"  ERROR   {encId} seed={seed}: {res.ErrorMessage}");
                        break;
                }
            }
        }
        sw.Stop();

        stdout.WriteLine(
            $"determinism-probe: initial-state-upstream summary — passed={passed} diverged={diverged} skipped={skipped} errored={errored} "
                + $"duration={sw.Elapsed.TotalSeconds:F2}s"
        );

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
                rowParts.Add(
                    res.Outcome switch
                    {
                        UpstreamInitialStateComparer.EntryOutcome.Pass => "PASS",
                        UpstreamInitialStateComparer.EntryOutcome.Diverged => "DIVR",
                        UpstreamInitialStateComparer.EntryOutcome.Skipped => "SKIP",
                        UpstreamInitialStateComparer.EntryOutcome.Error => "ERR ",
                        _ => "??? ",
                    }
                );
            }
            stdout.WriteLine(string.Join("  ", rowParts));
        }

        if (diverged > 0 || errored > 0)
        {
            stderr.WriteLine();
            stderr.WriteLine(
                $"determinism-probe: {firstFailures.Count} failure(s) — first 5 with diff summary ↓"
            );
            foreach (var f in firstFailures)
            {
                stderr.WriteLine($"-- {f.EncounterId} seed={f.Seed} outcome={f.Outcome}");
                if (f.ErrorMessage is not null)
                    stderr.WriteLine($"   error: {f.ErrorMessage}");
                if (f.DiffSummary is not null)
                {
                    foreach (string line in f.DiffSummary.TrimEnd().Split('\n'))
                    {
                        if (line.Length > 0)
                            stderr.WriteLine($"   {line}");
                    }
                }
            }
        }

        // Exit code: ExitPass if all comparable entries pass (skipped doesn't fail),
        // ExitFail if any divergence, ExitError if any I/O / construction error.
        if (errored > 0)
            return ExitError;
        if (diverged > 0)
            return ExitFail;
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
                var structural = corpus
                    .Entries.Where(e => e.Mode == CorpusEntry.ModeStructural)
                    .ToList();
                int smokeN = cli.SmokeSeeds ?? 5;
                var perStep = corpus
                    .Entries.Where(e => e.Mode == CorpusEntry.ModePerStep)
                    .Take(smokeN);
                return structural.Concat(perStep).ToList();
            }
            case ProbeMode.Full:
            case ProbeMode.Capture:
                return corpus.Entries;
            default:
                throw new InvalidOperationException($"unhandled mode {cli.Mode}.");
        }
    }

    /// <summary>
    /// Run Q1-side mid-combat capture and, in compare mode, diff against stored goldens.
    /// In capture mode, write goldens to disk. Both modes use the Q1MidCombatCaptureDriver
    /// (parallel system to per-step probe; no UpstreamDriver involvement here since
    /// the upstream capture runs separately via probe-upstream-mid-combat-capture target).
    ///
    /// <para>
    /// In <see cref="ProbeMode.MidCombat"/> (compare mode): reads committed goldens from
    /// <c>goldens-upstream/mid-combat/</c> and diffs Q1's fresh output against them.
    /// In <see cref="ProbeMode.MidCombatCapture"/>: regenerates goldens from Q1 capture
    /// (use only on first-establish or post-substrate-change to re-anchor the baseline).
    /// </para>
    /// </summary>
    private static int RunMidCombat(ProbeCli cli, TextWriter stdout, TextWriter stderr)
    {
        bool captureMode = cli.Mode == ProbeMode.MidCombatCapture;
        string probeDir = LocateProbeDir();
        string goldensRoot = cli.GoldensDir
            ?? Path.Combine(probeDir, "goldens-upstream", "mid-combat");
        string actionSeqDir = Path.Combine(probeDir, "goldens-upstream", "mid-combat", "action-sequences");

        // Encounter list for wave-1: cultist smoke + Phase-1 pool per plan §2.2.
        // Fast subset: CultistsNormal × 1 seed. Full: all encounters × 10 seeds.
        bool smokeOnly = cli.SmokeSeeds.HasValue && cli.SmokeSeeds.Value == 1;

        // Build the encounter × seed table.
        string[] allEncounters = smokeOnly
            ? new[] { "CultistsNormal" }
            : new[]
            {
                "CultistsNormal",
                "LouseProgenitorNormal",
                "ChompersNormal",
                "ExoskeletonsNormal",
                "BowlbugsTrio",
                "FuzzyWurmCrawlerSolo",
                "FossilStalkerElite",
                "FrogKnightElite",
                "LagavulinElite",
                "HauntedShipSolo",
                "LivingFogSolo",
                "GremlinMercNormal",
                "KaiserCrabBoss",
                "CeremonialBeastBoss",
                // wave-47a/C: NibbitsWeak + NibbitsNormal (wave-46 deferred item;
                // no V1 divergence; substrate wired wave-38/B).
                "NibbitsWeak",
                "NibbitsNormal",
            };

        int[] seeds = smokeOnly ? new[] { 42 } : Enumerable.Range(42, 10).ToArray();

        // Action sequence mapping (encounter → JSON file).
        //
        // Schema note (wave-47a/A; supersedes wave-46/A.0 T3):
        //   - target_creature_id in each action JSON refers to the ENCOUNTER-START
        //     enemy-order index (0-indexed). Both Q1 + upstream resolve this
        //     index to the live creature ID at replay time.
        //   - target_creature_id = null does NOT mean "any alive enemy" —
        //     CardPlayer.cs:71-75 THROWS on null target for AnyEnemy cards
        //     (discovered wave-46/Q1-A2 GremlinMerc work; supersedes wave-46/A.0's
        //     incorrect "any alive enemy" doc).
        //   - For mid-combat-spawn targets (e.g., GremlinMerc post-death
        //     spawns of SneakyGremlin + FatGremlin), use the encounter-start
        //     enemy-order index that will resolve to the spawn slot at replay
        //     time (slot 2 + slot 3 for 2-Nibbit spawns; verify per encounter).
        //   - Dead-creature targeting is a NO-OP (engine finds dead body in
        //     Enemies list; DealDamage to 0-HP is harmless). Sequences can
        //     target a dead enemy slot to consume the action without error.
        //
        // Stub entries (wave-46/A.0): point at JSON files NOT YET EXISTING. Cohorts
        // land the JSON files. The probe loop below checks File.Exists(seqPath)
        // and skips with `SKIP-NO-JSON` when missing (NOT an error).
        var actionSeqIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CultistsNormal"] = "cultist-strategy.json",
            ["LouseProgenitorNormal"] = "louse-progenitor-normal-strategy.json",
            ["GremlinMercNormal"] = "gremlin-merc-normal-strategy.json",
            ["KaiserCrabBoss"] = "kaiser-crab-boss-strategy.json",
            ["LagavulinElite"] = "lagavulin-elite-strategy.json",
            ["CeremonialBeastBoss"] = "ceremonial-beast-boss-strategy.json",
            ["ExoskeletonsNormal"] = "exoskeletons-normal-strategy.json",
            // wave-47a/C: NibbitsWeak + NibbitsNormal action sequences.
            ["NibbitsWeak"] = "nibbits-weak-strategy.json",
            ["NibbitsNormal"] = "nibbits-normal-strategy.json",
        };

        var driver = new Q1MidCombatCaptureDriver();
        var comparer = new MidCombatComparer(goldensRoot);

        stdout.WriteLine(
            $"determinism-probe: mode={cli.Mode} encounters={allEncounters.Length} seeds={seeds.Length} goldensRoot={goldensRoot}");

        int passed = 0, captured = 0, diverged = 0, skipped = 0, errored = 0;
        var firstFailures = new List<MidCombatComparer.EntryResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (string encId in allEncounters)
        {
            // Load action plan if available; skip encounter if missing.
            if (!actionSeqIds.TryGetValue(encId, out string? seqFile))
            {
                if (cli.Verbose)
                    stdout.WriteLine($"  SKIP    {encId} (no action-sequence; add to actionSeqIds to enable)");
                skipped += seeds.Length;
                continue;
            }

            string seqPath = Path.Combine(actionSeqDir, seqFile);

            // Wave-46/A.0 (per plan T1): skip stub entries whose JSON file
            // does not yet exist on disk. Cohorts land the JSON files; until
            // then, the entry is a stub.
            if (!File.Exists(seqPath))
            {
                if (cli.Verbose)
                    stdout.WriteLine($"  SKIP-NO-JSON {encId} (action-seq stub pending cohort; {seqFile})");
                skipped += seeds.Length;
                continue;
            }

            MidCombatActionPlan plan;
            try
            {
                plan = MidCombatActionPlan.LoadFromFile(seqPath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"  ERROR   {encId} action-seq load failed: {ex.Message}");
                errored += seeds.Length;
                continue;
            }

            foreach (int seed in seeds)
            {
                IReadOnlyList<MidCombatRecord> q1Records;
                try
                {
                    q1Records = driver.Capture(seed, encId, plan);
                }
                catch (Exception ex)
                {
                    errored++;
                    if (firstFailures.Count < 5)
                        firstFailures.Add(
                            new MidCombatComparer.EntryResult(
                                encId,
                                seed,
                                MidCombatComparer.EntryOutcome.Error,
                                null,
                                $"Q1 capture threw: {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}"
                            )
                        );
                    if (cli.Verbose)
                        stderr.WriteLine($"  ERROR   {encId} seed={seed}: {(ex.InnerException ?? ex).Message}");
                    continue;
                }

                if (captureMode)
                {
                    string goldenPath = comparer.GoldenPath(encId, seed);
                    MidCombatRecord.WriteFile(goldenPath, q1Records);
                    captured++;
                    if (cli.Verbose)
                        stdout.WriteLine($"  CAPTURED {encId} seed={seed} records={q1Records.Count}");
                }
                else
                {
                    MidCombatComparer.EntryResult res = comparer.CompareOne(encId, seed, q1Records);
                    switch (res.Outcome)
                    {
                        case MidCombatComparer.EntryOutcome.Pass:
                            passed++;
                            if (cli.Verbose)
                                stdout.WriteLine($"  PASS    {encId} seed={seed} records={q1Records.Count}");
                            break;
                        case MidCombatComparer.EntryOutcome.Diverged:
                            diverged++;
                            if (firstFailures.Count < 5) firstFailures.Add(res);
                            if (cli.Verbose)
                                stdout.WriteLine($"  DIVERGE {encId} seed={seed}: {res.DiffSummary}");
                            break;
                        case MidCombatComparer.EntryOutcome.GoldenMissing:
                            errored++;
                            if (firstFailures.Count < 5) firstFailures.Add(res);
                            if (cli.Verbose)
                                stdout.WriteLine($"  ERR-NOGOLDEN {encId} seed={seed}");
                            break;
                        case MidCombatComparer.EntryOutcome.Error:
                            errored++;
                            if (firstFailures.Count < 5) firstFailures.Add(res);
                            if (cli.Verbose)
                                stdout.WriteLine($"  ERROR   {encId} seed={seed}: {res.ErrorMessage}");
                            break;
                    }
                }
            }
        }
        sw.Stop();

        if (captureMode)
        {
            stdout.WriteLine(
                $"determinism-probe: mid-combat-capture summary — captured={captured} skipped={skipped} errored={errored} duration={sw.Elapsed.TotalSeconds:F2}s");
        }
        else
        {
            stdout.WriteLine(
                $"determinism-probe: mid-combat summary — passed={passed} diverged={diverged} skipped={skipped} errored={errored} duration={sw.Elapsed.TotalSeconds:F2}s");
        }

        if (firstFailures.Count > 0)
        {
            stderr.WriteLine($"determinism-probe: {firstFailures.Count} failure(s) ↓");
            foreach (var f in firstFailures)
            {
                stderr.WriteLine($"-- {f.EncounterId} seed={f.Seed} outcome={f.Outcome}");
                if (f.ErrorMessage is not null)
                    stderr.WriteLine($"   error: {f.ErrorMessage}");
                if (f.DiffSummary is not null)
                    stderr.WriteLine($"   diff:  {f.DiffSummary}");
            }
        }

        if (errored > 0) return ExitError;
        if (diverged > 0) return ExitFail;
        return ExitPass;
    }

    /// <summary>
    /// Self-test: write a synthetic MidCombatRecord sequence to a temp file,
    /// read it back, and assert field-level identity. Verifies magic, CRC32,
    /// length prefix, and all record fields round-trip through WriteFile/ReadFile.
    /// Exits 0 on pass, 2 (ExitError) on any mismatch.
    /// </summary>
    private static int RunRoundtripTest(TextWriter stdout, TextWriter stderr)
    {
        string tmpFile = Path.Combine(Path.GetTempPath(), $"midcombat-roundtrip-{Guid.NewGuid():N}.bin");
        try
        {
            // Build a synthetic two-record sequence exercising all fields.
            var powers1 = new PowerStackEntry[]
            {
                new PowerStackEntry("RitualPower", 3),
                new PowerStackEntry("StrengthPower", 1),
            };
            var powers2 = new PowerStackEntry[] { new PowerStackEntry("PoisonPower", 5) };
            var enemies = new EnemySnapshot[]
            {
                new EnemySnapshot(
                    "CalcifiedCultist",
                    42,
                    0,
                    "INCANTATION_MOVE",
                    "Buff",
                    0,
                    0,
                    0,
                    powers2
                ),
            };
            var rec0 = new MidCombatRecord(
                Turn: 1,
                Side: "player-pre",
                Phase: "PlayerTurn",
                PlayerHp: 70,
                PlayerBlock: 5,
                Energy: 3,
                PowerStacks: powers1,
                Enemies: enemies,
                RngCounter: 7
            );
            var rec1 = new MidCombatRecord(
                Turn: 1,
                Side: "enemy-end",
                Phase: "EnemyTurnEnd",
                PlayerHp: 62,
                PlayerBlock: 0,
                Energy: 0,
                PowerStacks: Array.Empty<PowerStackEntry>(),
                Enemies: enemies,
                RngCounter: 11
            );
            var written = new MidCombatRecord[] { rec0, rec1 };
            MidCombatRecord.WriteFile(tmpFile, written);

            // Read back.
            IReadOnlyList<MidCombatRecord> read = MidCombatRecord.ReadFile(tmpFile);

            // Assert count.
            if (read.Count != written.Length)
            {
                stderr.WriteLine(
                    $"determinism-probe: roundtrip-test FAIL — count: written={written.Length} read={read.Count}"
                );
                return ExitError;
            }

            // Assert field equality for each record.
            for (int i = 0; i < written.Length; i++)
            {
                MidCombatRecord w = written[i];
                MidCombatRecord r = read[i];
                string? mismatch = null;
                if (w.Turn != r.Turn)
                    mismatch = $"record[{i}].Turn: w={w.Turn} r={r.Turn}";
                else if (w.Side != r.Side)
                    mismatch = $"record[{i}].Side: w={w.Side} r={r.Side}";
                else if (w.Phase != r.Phase)
                    mismatch = $"record[{i}].Phase: w={w.Phase} r={r.Phase}";
                else if (w.PlayerHp != r.PlayerHp)
                    mismatch = $"record[{i}].PlayerHp: w={w.PlayerHp} r={r.PlayerHp}";
                else if (w.PlayerBlock != r.PlayerBlock)
                    mismatch = $"record[{i}].PlayerBlock: w={w.PlayerBlock} r={r.PlayerBlock}";
                else if (w.Energy != r.Energy)
                    mismatch = $"record[{i}].Energy: w={w.Energy} r={r.Energy}";
                else if (w.RngCounter != r.RngCounter)
                    mismatch = $"record[{i}].RngCounter: w={w.RngCounter} r={r.RngCounter}";
                else if (w.PowerStacks.Count != r.PowerStacks.Count)
                    mismatch = $"record[{i}].PowerStacks.Count: w={w.PowerStacks.Count} r={r.PowerStacks.Count}";
                else if (w.Enemies.Count != r.Enemies.Count)
                    mismatch = $"record[{i}].Enemies.Count: w={w.Enemies.Count} r={r.Enemies.Count}";

                if (mismatch is not null)
                {
                    stderr.WriteLine($"determinism-probe: roundtrip-test FAIL — {mismatch}");
                    return ExitError;
                }
            }

            stdout.WriteLine("determinism-probe: roundtrip-test PASS");
            return ExitPass;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(
                $"determinism-probe: roundtrip-test ERROR — {ex.GetType().Name}: {ex.Message}"
            );
            return ExitError;
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
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

    /// <summary>
    /// Mid-combat compare mode (wave-45/Q1-A1): drive Q1MidCombatCaptureDriver and
    /// compare per-turn-side snapshots against committed goldens in
    /// <c>goldens-upstream/mid-combat/</c>. PASS if all snapshots match golden.
    /// </summary>
    MidCombat,

    /// <summary>
    /// Mid-combat capture mode (wave-45/Q1-A1): drive Q1MidCombatCaptureDriver and
    /// write per-turn-side snapshots as golden files under
    /// <c>goldens-upstream/mid-combat/</c>. Use only on first-establish or when
    /// re-anchoring after a substrate change. Running twice must produce byte-identical output.
    /// </summary>
    MidCombatCapture,

    /// <summary>
    /// Self-test: write a synthetic <see cref="MidCombatRecord"/> sequence to a
    /// temp file, read it back, and assert field-level identity. Verifies the
    /// binary-framed wire format (magic, CRC32, length prefix) round-trips cleanly.
    /// Exits 0 on pass, 2 on failure.
    /// </summary>
    RoundtripTest,
}

/// <summary>Parsed CLI args for the probe driver.</summary>
public sealed record ProbeCli(
    ProbeMode Mode,
    string? CorpusPath,
    string? GoldensDir,
    int? SmokeSeeds,
    bool Verbose
)
{
    public const string Usage =
        "Usage: determinism-probe --mode <mode> [--corpus path] [--goldens dir] [--smoke-seeds N] [--verbose]\n"
        + "  modes: quick, full, structural, per-step, initial-state, initial-state-upstream, capture, generate-corpus, mid-combat, mid-combat-capture, roundtrip-test";

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
                if (inlineValue is not null)
                    return inlineValue;
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"missing value for {flag}.");
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
                    if (
                        !int.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed
                        )
                        || parsed < 1
                    )
                    {
                        throw new ArgumentException(
                            $"--smoke-seeds: expected positive int, got '{v}'."
                        );
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
        if (mode is null)
            throw new ArgumentException("--mode is required.");
        return new ProbeCli(mode.Value, corpusPath, goldensDir, smokeSeeds, verbose);
    }

    private static ProbeMode ParseMode(string s) =>
        s switch
        {
            "quick" => ProbeMode.Quick,
            "full" => ProbeMode.Full,
            "structural" => ProbeMode.Structural,
            "per-step" => ProbeMode.PerStep,
            "initial-state" => ProbeMode.InitialState,
            "initial-state-upstream" => ProbeMode.InitialStateUpstream,
            "capture" => ProbeMode.Capture,
            "generate-corpus" => ProbeMode.GenerateCorpus,
            "mid-combat" => ProbeMode.MidCombat,
            "mid-combat-capture" => ProbeMode.MidCombatCapture,
            "roundtrip-test" => ProbeMode.RoundtripTest,
            _ => throw new ArgumentException(
                $"--mode '{s}' unknown (expected: quick|full|structural|per-step|initial-state|initial-state-upstream|capture|generate-corpus|mid-combat|mid-combat-capture|roundtrip-test)."
            ),
        };
}
