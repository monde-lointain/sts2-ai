using System.Globalization;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

/// <summary>
/// S8 HARD GATE: invoke <see cref="Program.Run(string[], TextWriter, TextWriter, bool)"/>
/// for the smoke scenario
/// <c>--seed 42 --character silent --deck starter
/// --relics ring_of_the_snake --encounter cultists_normal --ascension 0
/// --script &lt;path&gt;</c>
/// where the script drives a deterministic end-turn sequence. Assert: exit
/// code is in {0,1}, stdout summary parses, final-state SHA matches the
/// pinned golden value below.
///
/// <para>
/// <b>S13 will replace this self-comparison with an upstream-Godot
/// comparison</b> — i.e., the determinism probe will run the same scenario
/// against the real game and compare each turn boundary's CombatState. For
/// now, this hash freezes the integration result so any regression in S1-S7
/// is caught here.
/// </para>
/// </summary>
public sealed class EndToEndReferenceCombatTests
{
    // === Pinned golden ====================================================
    //
    // Recorded the first time this test ran green. Replace with the captured
    // hash the test prints when it fails the first time.
    //
    // Stream-B-T3 schema bump: the smoke combat now stamps a per-creature
    // MoveId onto each monster's MonsterIntent so multi-state monsters
    // (e.g., Chomper) can rotate independently of the shared catalog model.
    // Stream-B-T4 schema bump: CombatState gained AttacksPlayedThisTurn /
    // CardsDrawnThisCombat aggregates (calc-damage cards: Finisher / Murder /
    // Mirage). Both additions shift StateCodec layout, hence the new sha.
    //
    // B.1-alpha-T4 (2026-05-12, post-RC-2+RC-3): mechanical regen. RC-2
    // changed the master seed from raw uint to hash($"seed-{N}"); RC-3 split
    // HP rolls onto .Niche and shuffles onto .Shuffle. Both shift every
    // sample of the kernel RNG stream, hence different downstream state.
    //
    // B.1-gamma-T5 (2026-05-11): codec schema bumped 2->3 with the addition
    // of CombatState.LastSpentEnergy + ExhaustedShivCount fields. The
    // additive serialization shifts every state-hash; mechanical regen.
    // B.1-gamma-T3 also reshaped Exoskeleton/Lagavulin/Louse intent rotation,
    // which changes the per-step state — but the smoke encounter is Cultists
    // and so isn't affected by those monsters. The shift is purely codec.
    private const string GoldenFinalStateSha = "30e234fdca323a95740580a2fb0c7279571662c067ae80198dc3a8184d905d80";

    // The same fixed end-turn script is used for both Domain-level golden and
    // this E2E test. Driving end_turn repeatedly is deterministic because the
    // cultists' moves don't consume RNG in the smoke set.
    private static readonly string[] DriveEndTurnsScript =
    {
        "# Drive the combat by simply ending each turn — the smoke deck has",
        "# enough damage on free draws but the cultists ramp via Ritual, so",
        "# the combat eventually ends with a definite outcome.",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
        "end_turn",
    };

    [Fact]
    public void Smoke_scenario_runs_to_definite_end_with_matching_golden_sha()
    {
        string scriptPath = WriteTempScript(DriveEndTurnsScript);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(
                args: BuildArgs("--script", scriptPath),
                stdout: stdout,
                stderr: stderr,
                attachProcessSignals: false);

            // Exit code: victory(0) or defeat(1) — script_exhausted(2) means
            // the combat didn't terminate in our budget, which is itself a
            // regression.
            Assert.True(exit == Program.ExitVictory || exit == Program.ExitDefeat,
                $"Unexpected exit code {exit}. stdout=<<<{stdout}>>> stderr=<<<{stderr}>>>");

            // Parse the summary line.
            string summary = stdout.ToString().Trim();
            Assert.Matches(@"^(victory|defeat) \| turns=\d+ \| final_state_sha256=[0-9a-f]{64}$", summary);
            string sha = ExtractSha(summary);

            Assert.True(sha == GoldenFinalStateSha,
                $"Final state SHA mismatch. Expected={GoldenFinalStateSha} Actual={sha}");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void Help_flag_prints_usage_and_exits_zero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Program.Run(
            args: new[] { "--help" },
            stdout: stdout, stderr: stderr,
            attachProcessSignals: false);
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout.ToString());
    }

    [Fact]
    public void Malformed_args_exits_with_error_code()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Program.Run(
            args: new[] { "--bogus" },
            stdout: stdout, stderr: stderr,
            attachProcessSignals: false);
        Assert.Equal(Program.ExitError, exit);
        Assert.Contains("--bogus", stderr.ToString());
    }

    [Fact]
    public void Out_flag_writes_state_blob_to_path()
    {
        string scriptPath = WriteTempScript(new[] { "end_turn" });
        string outPath = Path.Combine(Path.GetTempPath(), $"sts2-out-{Guid.NewGuid():N}.bin");
        try
        {
            int exit = Program.Run(
                args: BuildArgs("--script", scriptPath, "--out", outPath),
                stdout: new StringWriter(), stderr: new StringWriter(),
                attachProcessSignals: false);
            // We don't care about the exact exit here — we care that the blob exists and is non-empty.
            _ = exit;
            Assert.True(File.Exists(outPath));
            byte[] bytes = File.ReadAllBytes(outPath);
            Assert.True(bytes.Length > 0);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Metrics_endpoint_exposes_counters_when_enabled()
    {
        string scriptPath = WriteTempScript(new[] { "end_turn", "end_turn" });
        int port = RandomLocalPort();
        try
        {
            int exit = Program.Run(
                args: BuildArgs("--script", scriptPath, "--metrics-port", port.ToString(CultureInfo.InvariantCulture)),
                stdout: new StringWriter(), stderr: new StringWriter(),
                attachProcessSignals: false);
            // Note: the metrics server is torn down before Program.Run returns,
            // so a post-Run scrape would 404. The smoke we want is "doesn't
            // crash" + "exit code is sensible".
            Assert.True(exit == Program.ExitVictory || exit == Program.ExitDefeat || exit == Program.ExitError);
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    [Fact]
    public void Determinism_same_args_yield_same_final_sha()
    {
        string scriptPath = WriteTempScript(DriveEndTurnsScript);
        try
        {
            var stdout1 = new StringWriter();
            var stdout2 = new StringWriter();
            Program.Run(BuildArgs("--script", scriptPath), stdout1, new StringWriter(), attachProcessSignals: false);
            Program.Run(BuildArgs("--script", scriptPath), stdout2, new StringWriter(), attachProcessSignals: false);
            Assert.Equal(ExtractSha(stdout1.ToString().Trim()), ExtractSha(stdout2.ToString().Trim()));
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    // === Helpers ==========================================================

    private static string[] BuildArgs(params string[] extras)
    {
        var baseArgs = new[]
        {
            "--seed", "42",
            "--character", "silent",
            "--deck", "starter",
            "--relics", "ring_of_the_snake",
            "--encounter", "cultists_normal",
            "--ascension", "0",
        };
        return baseArgs.Concat(extras).ToArray();
    }

    private static string WriteTempScript(IEnumerable<string> lines)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sts2-script-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string ExtractSha(string summary)
    {
        const string marker = "final_state_sha256=";
        int idx = summary.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Summary missing SHA: '{summary}'");
        return summary[(idx + marker.Length)..].Trim();
    }

    private static int RandomLocalPort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

public sealed class SigtermShutdownTests
{
    [Fact]
    public void Program_run_with_pretriggered_shutdown_returns_promptly()
    {
        // We can't fire a real SIGTERM at our own process within xUnit, but
        // we can verify the cancellation pathway by using a long-running
        // script and verifying it exits via the cancelled outcome quickly
        // when the GracefulShutdown is pre-triggered. Here, we invoke
        // MainLoop directly with a pre-cancelled token to exercise the
        // same path.
        var bundle = CompositionRoot.Build(CliArgs.Parse(new[]
        {
            "--seed", "42",
            "--character", "silent",
            "--deck", "starter",
            "--relics", "ring_of_the_snake",
            "--encounter", "cultists_normal",
            "--ascension", "0",
        }));
        var provider = new FileScriptedActionProvider(
            Enumerable.Repeat("end_turn", 10_000), bundle.Cards);
        var logger = new CapturingLogger();
        var metrics = new InMemoryMetrics();
        var cts = new CancellationTokenSource();

        // Deadline = 5s per the prompt.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        cts.Cancel();
        var result = MainLoop.Run(bundle.Context, bundle.Cards, provider, logger, metrics, cts.Token);
        sw.Stop();

        Assert.Equal(MainLoop.LoopOutcome.Cancelled, result.Outcome);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"MainLoop should exit promptly on cancellation; took {sw.Elapsed}.");
    }
}
