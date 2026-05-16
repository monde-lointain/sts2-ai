using System.Text.Json;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

/// <summary>
/// Tests for the <c>--probe-out</c> flag added in S8-T8 (reactive from S13).
/// The flag pipes per-step canonical-hash records to a JSON-line file used by
/// the S13 determinism probe.
/// </summary>
public sealed class ProbeOutTests
{
    private static readonly string[] MinimalSmokeArgs = new[]
    {
        "--seed",
        "42",
        "--character",
        "silent",
        "--deck",
        "starter",
        "--relics",
        "ring_of_the_snake",
        "--encounter",
        "cultists_normal",
        "--ascension",
        "0",
    };

    [Fact]
    public void Probe_out_flag_parses_into_CliArgs()
    {
        var args = MinimalSmokeArgs.Concat(new[] { "--probe-out", "/tmp/p.jsonl" }).ToArray();
        CliArgs parsed = CliArgs.Parse(args);
        Assert.Equal("/tmp/p.jsonl", parsed.ProbeOutPath);
    }

    [Fact]
    public void Probe_out_writes_jsonl_records_during_smoke_run()
    {
        string scriptPath = WriteTempScript(new[] { "end_turn", "end_turn", "end_turn" });
        string probePath = TempPath(".jsonl");
        try
        {
            int exit = Program.Run(
                args: MinimalSmokeArgs
                    .Concat(new[] { "--script", scriptPath, "--probe-out", probePath })
                    .ToArray(),
                stdout: new StringWriter(),
                stderr: new StringWriter(),
                attachProcessSignals: false
            );
            _ = exit;

            Assert.True(File.Exists(probePath), $"probe output not written to {probePath}");
            string[] lines = File.ReadAllLines(probePath);
            Assert.NotEmpty(lines);
            // First record is combat_start.
            using var doc = JsonDocument.Parse(lines[0]);
            Assert.Equal("combat_start", doc.RootElement.GetProperty("event").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("step").GetInt32());
            Assert.Equal(64, doc.RootElement.GetProperty("hash").GetString()!.Length);
        }
        finally
        {
            File.Delete(scriptPath);
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    [Fact]
    public void Probe_out_is_deterministic_same_seed_same_hashes()
    {
        string scriptPath = WriteTempScript(new[] { "end_turn", "end_turn", "end_turn" });
        string p1 = TempPath(".jsonl");
        string p2 = TempPath(".jsonl");
        try
        {
            Program.Run(
                args: MinimalSmokeArgs
                    .Concat(new[] { "--script", scriptPath, "--probe-out", p1 })
                    .ToArray(),
                stdout: new StringWriter(),
                stderr: new StringWriter(),
                attachProcessSignals: false
            );
            Program.Run(
                args: MinimalSmokeArgs
                    .Concat(new[] { "--script", scriptPath, "--probe-out", p2 })
                    .ToArray(),
                stdout: new StringWriter(),
                stderr: new StringWriter(),
                attachProcessSignals: false
            );

            string c1 = File.ReadAllText(p1);
            string c2 = File.ReadAllText(p2);
            Assert.Equal(c1, c2);
        }
        finally
        {
            File.Delete(scriptPath);
            if (File.Exists(p1))
                File.Delete(p1);
            if (File.Exists(p2))
                File.Delete(p2);
        }
    }

    [Fact]
    public void Probe_out_emits_combat_start_for_structural_probe_with_no_script()
    {
        // When no script is supplied the loop exits at script_exhausted after
        // emitting combat_start + turn_start. The probe uses this for the
        // structural / initial-state coverage on all 22 encounters.
        string probePath = TempPath(".jsonl");
        try
        {
            int exit = Program.Run(
                args: MinimalSmokeArgs.Concat(new[] { "--probe-out", probePath }).ToArray(),
                stdout: new StringWriter(),
                stderr: new StringWriter(),
                attachProcessSignals: false
            );
            _ = exit;
            Assert.True(File.Exists(probePath));
            string[] lines = File.ReadAllLines(probePath);
            Assert.True(
                lines.Length >= 2,
                $"expected at least combat_start + turn_start, got {lines.Length}"
            );

            using var firstDoc = JsonDocument.Parse(lines[0]);
            Assert.Equal("combat_start", firstDoc.RootElement.GetProperty("event").GetString());

            using var secondDoc = JsonDocument.Parse(lines[1]);
            Assert.Equal("turn_start", secondDoc.RootElement.GetProperty("event").GetString());
        }
        finally
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    [Fact]
    public void Probe_out_works_for_non_smoke_encounters()
    {
        // Phase-1 catalogs are activated when the encounter id is not the smoke
        // one. ChompersNormal is one of the 22 Phase-1 encounters.
        string probePath = TempPath(".jsonl");
        try
        {
            int exit = Program.Run(
                args: new[]
                {
                    "--seed",
                    "42",
                    "--character",
                    "silent",
                    "--deck",
                    "starter",
                    "--relics",
                    "ring_of_the_snake",
                    "--encounter",
                    "ChompersNormal",
                    "--ascension",
                    "0",
                    "--probe-out",
                    probePath,
                },
                stdout: new StringWriter(),
                stderr: new StringWriter(),
                attachProcessSignals: false
            );
            _ = exit;
            Assert.True(File.Exists(probePath));
            string[] lines = File.ReadAllLines(probePath);
            Assert.NotEmpty(lines);
            using var doc = JsonDocument.Parse(lines[0]);
            Assert.Equal("combat_start", doc.RootElement.GetProperty("event").GetString());
            // Chomper encounter has 2 enemies per Phase1Encounters.cs.
            Assert.Equal(2, doc.RootElement.GetProperty("enemy_count").GetInt32());
        }
        finally
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    // === Helpers ==========================================================

    private static string WriteTempScript(IEnumerable<string> lines)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sts2-script-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), $"sts2-probe-{Guid.NewGuid():N}{ext}");
}
