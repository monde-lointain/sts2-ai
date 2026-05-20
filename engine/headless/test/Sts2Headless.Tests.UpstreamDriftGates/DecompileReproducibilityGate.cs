using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Sts2Headless.Tests.UpstreamDriftGates;

/// <summary>
/// A.1 drift gate (opt-in): verifies that <c>tools/upstream-sync extract</c>
/// is byte-stable — running it twice produces identical output for all
/// non-artifact source files.
///
/// <para>
/// Per A.0.1 spike outcome: DETERMINISTIC. Two consecutive runs of
/// <c>upstream_sync.cli extract</c> produce identical output (modulo the
/// <c>--yz__</c> / <c>--z__</c> artifact prefixed files, which are excluded).
/// </para>
///
/// <para>
/// <b>Opt-in:</b> this test is <b>skipped by default</b> (it runs
/// <c>tools/upstream-sync</c> twice, which is slow). Enable it by setting
/// <c>DRIFT_GATES_REPRO=1</c> in the environment, or by running the test with
/// the <c>Reproducibility</c> trait filter:
/// <code>
/// dotnet test --filter "Category=Reproducibility"
/// </code>
/// </para>
///
/// <para>
/// <c>make drift-gates-ci</c> does NOT set <c>DRIFT_GATES_REPRO=1</c> unless
/// overridden with <c>DRIFT_GATES_REPRO=1 make drift-gates-ci</c>.
/// </para>
/// </summary>
public sealed class DecompileReproducibilityGate
{
    // Matches artifact-prefixed filenames (--yz__*, --z__*) per A.0 convention.
    // These are excluded from the byte-diff because they contain timestamps or
    // random suffixes by design.
    private static readonly Regex ArtifactPrefix = new(
        @"^--[yz]__",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Run <c>tools/upstream-sync extract</c> twice into temp directories and
    /// byte-diff all non-artifact <c>.cs</c> files. Fails if any file drifts
    /// between runs.
    ///
    /// <para>Skipped unless <c>DRIFT_GATES_REPRO=1</c> is set.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Reproducibility")]
    public void ExtractTwice_ProducesIdenticalOutput()
    {
        bool optIn = string.Equals(
            Environment.GetEnvironmentVariable("DRIFT_GATES_REPRO"),
            "1",
            StringComparison.Ordinal
        );

        if (!optIn)
        {
            // Marked skip (not silent pass). The caller sees "SKIPPED" in test output.
            // Xunit.SkippableFact: throw SkipException to signal skip to runner.
            throw new Xunit.SkipException(
                "DecompileReproducibilityGate is opt-in. "
                    + "Set DRIFT_GATES_REPRO=1 or use --filter Category=Reproducibility."
            );
        }

        string repoRoot = LocateRepoRoot();
        string venv = Path.Combine(repoRoot, ".venv");

        // Create two separate temp output dirs.
        string run1Dir = Path.Combine(Path.GetTempPath(), $"drift-repro-a-{Guid.NewGuid():N}");
        string run2Dir = Path.Combine(Path.GetTempPath(), $"drift-repro-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(run1Dir);
        Directory.CreateDirectory(run2Dir);

        try
        {
            RunExtract(venv, repoRoot, run1Dir);
            RunExtract(venv, repoRoot, run2Dir);

            IReadOnlyList<string> driftFiles = ByteDiffNonArtifact(run1Dir, run2Dir);

            if (driftFiles.Count > 0)
            {
                string listing = string.Join("\n  ", driftFiles.Take(20));
                Assert.Fail(
                    $"REPRODUCIBILITY DRIFT: {driftFiles.Count} file(s) differ between two extract runs.\n"
                        + $"  {listing}\n\n"
                        + $"Run 1 dir: {run1Dir}\n"
                        + $"Run 2 dir: {run2Dir}\n"
                        + $"Per A.0.1 spike, extract MUST be deterministic. "
                        + $"Investigate upstream-sync extract for non-determinism sources "
                        + $"(timestamps, ordering, GDRE flags)."
                );
            }
        }
        finally
        {
            // Best-effort cleanup.
            TryDeleteDirectory(run1Dir);
            TryDeleteDirectory(run2Dir);
        }
    }

    private static void RunExtract(string venv, string repoRoot, string outputDir)
    {
        string pythonExe = Path.Combine(venv, "bin", "python");
        if (!File.Exists(pythonExe))
        {
            throw new Xunit.SkipException(
                $"Python venv not found at {venv} — cannot run upstream-sync. "
                    + "Create the venv or set DRIFT_GATES_REPRO=0 to skip."
            );
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-m upstream_sync.cli extract --out {outputDir}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using System.Diagnostics.Process proc =
            System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start upstream-sync extract process."
            );

        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            Assert.Fail(
                $"tools/upstream-sync extract exited with code {proc.ExitCode}.\n"
                    + $"Stderr:\n{stderr}\n"
                    + $"Is the upstream sts2.dll present and Steam installed?"
            );
        }
    }

    /// <summary>
    /// Returns the list of relative paths (from the output dirs) where run1 and
    /// run2 produce different bytes, excluding artifact-prefixed files.
    /// </summary>
    private static IReadOnlyList<string> ByteDiffNonArtifact(string dir1, string dir2)
    {
        var files1 = EnumerateSourceFiles(dir1)
            .ToDictionary(f => Path.GetRelativePath(dir1, f), f => f);
        var files2 = EnumerateSourceFiles(dir2)
            .ToDictionary(f => Path.GetRelativePath(dir2, f), f => f);

        var drift = new List<string>();

        foreach (var (rel, path1) in files1)
        {
            if (!files2.TryGetValue(rel, out string? path2))
            {
                drift.Add($"{rel}  [missing in run 2]");
                continue;
            }
            if (!FilesAreEqual(path1, path2))
            {
                drift.Add(rel);
            }
        }

        foreach (string rel in files2.Keys.Except(files1.Keys))
        {
            drift.Add($"{rel}  [missing in run 1]");
        }

        return drift;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string dir)
    {
        if (!Directory.Exists(dir))
            return Enumerable.Empty<string>();

        return Directory
            .EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ArtifactPrefix.IsMatch(Path.GetFileName(f)));
    }

    private static bool FilesAreEqual(string path1, string path2)
    {
        FileInfo fi1 = new(path1);
        FileInfo fi2 = new(path2);
        if (fi1.Length != fi2.Length)
            return false;

        using FileStream s1 = File.OpenRead(path1);
        using FileStream s2 = File.OpenRead(path2);
        Span<byte> buf1 = stackalloc byte[4096];
        Span<byte> buf2 = stackalloc byte[4096];
        int n;
        while ((n = s1.Read(buf1)) > 0)
        {
            int n2 = s2.Read(buf2);
            if (n != n2)
                return false;
            if (!buf1[..n].SequenceEqual(buf2[..n]))
                return false;
        }
        return true;
    }

    private static string LocateRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (
                Directory.Exists(Path.Combine(dir, "engine", "headless"))
                && Directory.Exists(Path.Combine(dir, "tools"))
            )
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Could not locate repo root (expected engine/headless/ + tools/ siblings)."
        );
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }
}
