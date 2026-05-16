using System.Diagnostics;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Host;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Runs Q1 (via in-process invocation of <see cref="Program.Run(string[], TextWriter, TextWriter, bool)"/>)
/// for a given <see cref="CorpusEntry"/>, captures the per-step probe-out
/// stream, and compares against a golden trace.
///
/// <para>
/// <b>Why in-process</b> rather than spawning the binary: the probe runs
/// 22+220+50 = ~290 invocations for the full corpus. Spawning subprocesses
/// would cost ~50ms each (~15s of pure process-startup overhead) and would
/// require JSON-IPC for status. In-process gets us deterministic linkage to
/// the exact Q1 build under test (same SHA + same loaded assemblies) and
/// keeps probe-quick under the 60-second budget.
/// </para>
/// </summary>
public sealed class ProbeRunner
{
    private readonly string _goldensDir;

    public ProbeRunner(string goldensDir)
    {
        ArgumentNullException.ThrowIfNull(goldensDir);
        _goldensDir = goldensDir;
    }

    /// <summary>Outcome of a single corpus entry.</summary>
    public enum EntryOutcome
    {
        /// <summary>Q1 hashes matched the golden trace exactly.</summary>
        Pass,

        /// <summary>At least one record diverged; <see cref="EntryResult.Divergence"/> is set.</summary>
        Diverged,

        /// <summary>No golden file existed yet; the run captured one.</summary>
        Captured,

        /// <summary>Q1 invocation or comparison failed.</summary>
        Error,
    }

    /// <summary>Describes the first divergence between Q1 and the golden trace.</summary>
    public sealed record DivergencePoint(
        int StepIndex,
        ProbeRecord Q1Record,
        ProbeRecord? GoldenRecord,
        string Reason
    );

    /// <summary>Per-entry result returned by <see cref="RunEntry"/>.</summary>
    public sealed record EntryResult(
        CorpusEntry Entry,
        EntryOutcome Outcome,
        IReadOnlyList<ProbeRecord> Q1Records,
        DivergencePoint? Divergence,
        string? ErrorMessage,
        TimeSpan Duration
    );

    /// <summary>
    /// Run one corpus entry. The behaviour depends on <paramref name="captureMode"/>:
    /// <list type="bullet">
    ///   <item><c>false</c> (default) — load the golden and compare.</item>
    ///   <item><c>true</c> — write the Q1 trace as the new golden.</item>
    /// </list>
    /// </summary>
    public EntryResult RunEntry(CorpusEntry entry, bool captureMode = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var sw = Stopwatch.StartNew();

        string scriptPath = WriteTempScript(entry.Script);
        string probeOutPath = TempPath(".jsonl");
        try
        {
            string[] hostArgs = BuildHostArgs(entry, scriptPath, probeOutPath);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exit;
            try
            {
                exit = Sts2Headless.Host.Program.Run(
                    hostArgs,
                    stdout,
                    stderr,
                    attachProcessSignals: false
                );
            }
            catch (Exception ex)
            {
                return new EntryResult(
                    entry,
                    EntryOutcome.Error,
                    Q1Records: Array.Empty<ProbeRecord>(),
                    Divergence: null,
                    ErrorMessage: $"Host threw: {ex.GetType().Name}: {ex.Message}",
                    Duration: sw.Elapsed
                );
            }
            if (!File.Exists(probeOutPath))
            {
                return new EntryResult(
                    entry,
                    EntryOutcome.Error,
                    Q1Records: Array.Empty<ProbeRecord>(),
                    Divergence: null,
                    ErrorMessage: $"probe-out file not produced (host exit={exit}; stderr=<<<{stderr}>>>).",
                    Duration: sw.Elapsed
                );
            }

            IReadOnlyList<ProbeRecord> q1 = ProbeRecord.ReadFile(probeOutPath);

            // Mode-specific assertions on the Q1 trace before comparing.
            string? modeError = ValidateMode(entry, q1);
            if (modeError is not null)
            {
                return new EntryResult(
                    entry,
                    EntryOutcome.Error,
                    Q1Records: q1,
                    Divergence: null,
                    ErrorMessage: modeError,
                    Duration: sw.Elapsed
                );
            }

            string goldenPath = GoldenPath(entry);
            if (captureMode || !File.Exists(goldenPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
                File.Copy(probeOutPath, goldenPath, overwrite: true);
                return new EntryResult(
                    entry,
                    EntryOutcome.Captured,
                    q1,
                    Divergence: null,
                    ErrorMessage: null,
                    Duration: sw.Elapsed
                );
            }

            IReadOnlyList<ProbeRecord> golden = ProbeRecord.ReadFile(goldenPath);
            DivergencePoint? d = CompareTraces(entry, q1, golden);
            EntryOutcome outcome = d is null ? EntryOutcome.Pass : EntryOutcome.Diverged;
            return new EntryResult(entry, outcome, q1, d, ErrorMessage: null, Duration: sw.Elapsed);
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
            if (File.Exists(probeOutPath))
                File.Delete(probeOutPath);
        }
    }

    /// <summary>
    /// Per-mode invariants checked against the Q1 trace (before comparison).
    /// </summary>
    private static string? ValidateMode(CorpusEntry entry, IReadOnlyList<ProbeRecord> q1)
    {
        if (q1.Count == 0)
        {
            return "probe-out empty: no records emitted.";
        }
        switch (entry.Mode)
        {
            case CorpusEntry.ModeStructural:
            {
                // Structural mode: only checks that combat_start fired with the
                // expected encounter spawn count. Doesn't depend on the golden.
                int expectedEnemies = ResolveExpectedEnemyCount(entry.Encounter);
                ProbeRecord first = q1[0];
                if (first.Event != "combat_start")
                {
                    return $"structural: first event '{first.Event}' != combat_start.";
                }
                if (first.EnemyCount != expectedEnemies)
                {
                    return $"structural: {entry.Encounter} spawned {first.EnemyCount} enemies, expected {expectedEnemies}.";
                }
                return null;
            }
            case CorpusEntry.ModeInitialState:
            {
                // Initial-state: must have at least combat_start + turn_start so
                // the hash captures the post-StartCombat state.
                if (q1.Count < 2)
                {
                    return $"initial_state: expected >= 2 records (combat_start + turn_start), got {q1.Count}.";
                }
                if (q1[0].Event != "combat_start")
                {
                    return $"initial_state: first event '{q1[0].Event}' != combat_start.";
                }
                return null;
            }
            case CorpusEntry.ModePerStep:
            {
                // Per-step: must reach combat_end or run through the full script.
                bool reachedEnd = q1[^1].Event == "combat_end";
                if (!reachedEnd)
                {
                    return $"per_step: last event '{q1[^1].Event}' != combat_end (combat didn't terminate).";
                }
                return null;
            }
            default:
                return $"unknown mode '{entry.Mode}'.";
        }
    }

    /// <summary>Resolve how many enemies an encounter spawns (for structural checks).</summary>
    private static int ResolveExpectedEnemyCount(string cliToken)
    {
        EncounterCatalog cat = Phase1Content.BuildEncounterCatalog();
        // CLI accepts snake_case ("cultists_normal") or PascalCase ("ChompersNormal");
        // try canonical first, then fall back to case-insensitive match.
        foreach (string id in cat.EnumerateIds())
        {
            if (string.Equals(id, cliToken, StringComparison.OrdinalIgnoreCase))
            {
                return ((IEncounterModel)cat.Get(id)).MonsterIds.Count;
            }
        }
        if (string.Equals(cliToken, "cultists_normal", StringComparison.OrdinalIgnoreCase))
        {
            return ((IEncounterModel)cat.Get(CultistsNormal.CanonicalId)).MonsterIds.Count;
        }
        throw new InvalidOperationException(
            $"Unknown encounter '{cliToken}' for structural-count lookup."
        );
    }

    /// <summary>
    /// Compare two probe traces step-by-step. For initial_state we compare
    /// only the first 2 records (combat_start + turn_start, both reflect the
    /// post-StartCombat state). For per_step we compare every record. For
    /// structural we just compared in <see cref="ValidateMode"/>.
    /// </summary>
    private static DivergencePoint? CompareTraces(
        CorpusEntry entry,
        IReadOnlyList<ProbeRecord> q1,
        IReadOnlyList<ProbeRecord> golden
    )
    {
        int compareCount = entry.Mode switch
        {
            CorpusEntry.ModeStructural => 1, // combat_start only
            CorpusEntry.ModeInitialState => Math.Min(2, Math.Min(q1.Count, golden.Count)),
            CorpusEntry.ModePerStep => Math.Max(q1.Count, golden.Count),
            _ => Math.Max(q1.Count, golden.Count),
        };

        for (int i = 0; i < compareCount; i++)
        {
            if (i >= q1.Count)
            {
                return new DivergencePoint(
                    i,
                    golden[i],
                    golden[i],
                    $"Q1 trace ended early at step {i}; golden has {golden.Count} records."
                );
            }
            if (i >= golden.Count)
            {
                return new DivergencePoint(
                    i,
                    q1[i],
                    null,
                    $"Q1 trace is longer than golden ({q1.Count} vs {golden.Count}); extra record at step {i}."
                );
            }
            ProbeRecord q = q1[i];
            ProbeRecord g = golden[i];
            if (q.Hash != g.Hash)
            {
                return new DivergencePoint(
                    i,
                    q,
                    g,
                    $"hash mismatch at step {i} ({q.Event}): Q1={q.Hash[..16]}.. golden={g.Hash[..16]}.."
                );
            }
            if (q.Event != g.Event)
            {
                return new DivergencePoint(
                    i,
                    q,
                    g,
                    $"event mismatch at step {i}: Q1={q.Event} golden={g.Event}."
                );
            }
        }
        return null;
    }

    // === Helpers ==========================================================

    private string GoldenPath(CorpusEntry entry) => Path.Combine(_goldensDir, $"{entry.Id}.jsonl");

    private static string[] BuildHostArgs(CorpusEntry entry, string scriptPath, string probeOutPath)
    {
        var list = new List<string>
        {
            "--seed",
            entry.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--character",
            "silent",
            "--deck",
            "starter",
            "--relics",
            string.Join(",", entry.Relics),
            "--encounter",
            entry.Encounter,
            "--ascension",
            "0",
            "--probe-out",
            probeOutPath,
        };
        if (entry.Script.Count > 0)
        {
            list.Add("--script");
            list.Add(scriptPath);
        }
        return list.ToArray();
    }

    private static string WriteTempScript(IReadOnlyList<string> lines)
    {
        string path = TempPath(".script");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), $"sts2-probe-{Guid.NewGuid():N}{ext}");
}
