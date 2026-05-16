using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Sts2Headless.Tests.Tools.Fixtures;

namespace Sts2Headless.Tests.Tools.ConsoleHost;

/// <summary>
/// <para>
/// P-1.5-1.α smoke test for the <c>Sts2Q1ConsoleHost</c> tool. Spawns the
/// console host as a subprocess (so the test exercises the same
/// <c>AssemblyLoadContext</c> path the real probe driver will use in δ),
/// captures stdout, and asserts the <c>upstream_bound</c> sentinel JSON
/// is present and well-formed.
/// </para>
///
/// <para>
/// <b>Skip semantics:</b> the Steam install path is not portable. Worktree
/// builds without Steam (or CI hosts missing the install) should pass
/// cleanly — the test reports a documented skip reason via xUnit's runtime
/// skip mechanism (return early after recording the reason via
/// <see cref="Assert.Skip"/> when the binary or runtime deps are absent).
/// xUnit 2.x has no programmatic Skip API, so we surface it by recording
/// a sentinel value and treating absent-Steam as "no-op passing test".
/// </para>
/// </summary>
public class ConsoleHostSmokeTests
{
    /// <summary>Canonical Steam install dir on the Q1 build host.</summary>
    private static readonly string SteamSts2Dir = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? "",
        "snap",
        "steam",
        "common",
        ".local",
        "share",
        "Steam",
        "steamapps",
        "common",
        "Slay the Spire 2",
        "data_sts2_linuxbsd_x86_64"
    );

    private static string Sts2DllPath => Path.Combine(SteamSts2Dir, "sts2.dll");

    /// <summary>
    /// Spawn the console host with <c>--sts2-dll</c> pointing at the Steam
    /// install, assert exit 0, and parse <c>upstream_bound</c> from stdout.
    /// Auto-skips if the Steam install is absent (worktree builds without
    /// Steam stay green).
    /// </summary>
    [Fact]
    public void Sts2Q1ConsoleHost_loads_upstream_sts2_dll()
    {
        if (!File.Exists(Sts2DllPath))
        {
            // Steam install absent — gracefully skip. xUnit 2.x's API doesn't
            // expose programmatic Skip; we exit early so the test passes
            // without doing the work. The skip reason is surfaced via
            // Console.Out so a human running `dotnet test -v n` sees it.
            Console.Out.WriteLine(
                $"[skip] ConsoleHostSmokeTests: Steam install not found at '{Sts2DllPath}'."
            );
            return;
        }

        string consoleHostProject = Path.Combine(
            FixtureLocator.RepoRoot,
            "tools",
            "Sts2Q1ConsoleHost",
            "Sts2Q1ConsoleHost.csproj"
        );
        Assert.True(
            File.Exists(consoleHostProject),
            $"console host project missing at '{consoleHostProject}'."
        );

        string outPath = Path.Combine(Path.GetTempPath(), $"p151a-smoke-{Guid.NewGuid():N}.jsonl");
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--project");
            psi.ArgumentList.Add(consoleHostProject);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Debug");
            psi.ArgumentList.Add("--no-build");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add("--sts2-dll");
            psi.ArgumentList.Add(Sts2DllPath);
            psi.ArgumentList.Add("--seed");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("--encounter");
            psi.ArgumentList.Add("CultistsNormal");
            psi.ArgumentList.Add("--out");
            psi.ArgumentList.Add(outPath);

            using var process =
                Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException(
                    "Console host did not exit within 60s — load path may be hung."
                );
            }

            Assert.True(
                process.ExitCode == 0,
                $"Sts2Q1ConsoleHost exit={process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}"
            );

            // Find the upstream_bound line. `dotnet run` may emit
            // restore/build chatter ahead of our stdout when --no-build is
            // ignored (it's a hint, not a guarantee), so scan all lines.
            string? upstreamBoundLine = null;
            foreach (string raw in stdout.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] != '{')
                    continue;
                if (line.Contains("\"event\":\"upstream_bound\"", StringComparison.Ordinal))
                {
                    upstreamBoundLine = line;
                    break;
                }
            }
            Assert.NotNull(upstreamBoundLine);

            using var doc = JsonDocument.Parse(upstreamBoundLine!);
            JsonElement root = doc.RootElement;
            Assert.Equal("upstream_bound", root.GetProperty("event").GetString());
            Assert.Equal(Sts2DllPath, root.GetProperty("sts2_dll").GetString());
            Assert.Equal("sts2", root.GetProperty("assembly_name").GetString());
            Assert.Equal(
                "MegaCrit.Sts2.Core.Combat.CombatManager",
                root.GetProperty("combat_manager_type").GetString()
            );
            Assert.Equal(
                "MegaCrit.Sts2.Core.Entities.Players.Player",
                root.GetProperty("player_type").GetString()
            );

            // α opens (creates / truncates) the out file. δ populates it. So
            // we expect the file to exist (possibly empty) after α succeeds.
            Assert.True(File.Exists(outPath), $"--out file not created at '{outPath}'.");
        }
        finally
        {
            if (File.Exists(outPath))
            {
                try
                {
                    File.Delete(outPath);
                }
                catch
                { /* best-effort cleanup */
                }
            }
        }
    }
}
