using System.Globalization;
using System.Security.Cryptography;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// D6 (2026-05-12): registry-SHA round-trip regression guard.
///
/// <para>
/// <b>Contract.</b> When the Q1 host is booted against a registry file
/// (<c>--registry &lt;path&gt;</c>), the SHA-256 of the registry file's bytes
/// is stamped into the emitted state blob's <see cref="ManifestStamp.ContentHash"/>
/// (the wire slot for <c>state_blob.proto/registry_sha</c>). The read-and-stamp
/// path is single-source: exactly one read site, one SHA computation, one stamp.
/// </para>
///
/// <para>
/// <b>Regression guard.</b> Mutating one byte of the registry file MUST shift
/// the blob's stamped hash; restoring the byte MUST return it to the original
/// value. Any future split that introduces a parallel SHA computation (e.g. a
/// hard-coded mirror in tests, a second compute inside the codec) will fail
/// one of these three assertions.
/// </para>
///
/// <para>
/// <b>Hands-off contract.</b> The canonical
/// <c>contracts/registry/phase1-silent.json</c> is read-only at test time —
/// this test operates exclusively on a per-test temp-dir copy.
/// </para>
/// </summary>
public sealed class RegistryShaRoundtripTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly byte[] _originalBytes;

    public RegistryShaRoundtripTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"sts2-d6-registry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "phase1-silent.json");
        File.Copy(LocateCanonicalRegistry(), _registryPath);
        _originalBytes = File.ReadAllBytes(_registryPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; never fail a test because of leftover scratch.
        }
    }

    [Fact]
    public void RegistrySha_in_blob_matches_sha256_of_registry_bytes()
    {
        byte[] stampedSha = BootAndExtractRegistrySha(_registryPath);
        byte[] expected = SHA256.HashData(File.ReadAllBytes(_registryPath));

        Assert.Equal(expected, stampedSha);
    }

    [Fact]
    public void Mutating_one_byte_of_registry_shifts_blob_sha()
    {
        byte[] originalSha = BootAndExtractRegistrySha(_registryPath);

        // Flip exactly one byte. Find the first 'S' (likely in "StrikeSilent")
        // and swap it for 's' — keeps the file valid JSON, mutates content.
        byte[] mutated = (byte[])_originalBytes.Clone();
        int idx = Array.IndexOf(mutated, (byte)'S');
        Assert.True(idx >= 0, "expected ASCII 'S' somewhere in the registry");
        mutated[idx] = (byte)'s';
        File.WriteAllBytes(_registryPath, mutated);

        byte[] mutatedSha = BootAndExtractRegistrySha(_registryPath);

        Assert.NotEqual(originalSha, mutatedSha);
    }

    [Fact]
    public void Restoring_registry_bytes_restores_blob_sha()
    {
        byte[] originalSha = BootAndExtractRegistrySha(_registryPath);

        // Mutate, capture mutated sha (sanity), then restore.
        byte[] mutated = (byte[])_originalBytes.Clone();
        int idx = Array.IndexOf(mutated, (byte)'S');
        mutated[idx] = (byte)'s';
        File.WriteAllBytes(_registryPath, mutated);
        byte[] mutatedSha = BootAndExtractRegistrySha(_registryPath);
        Assert.NotEqual(originalSha, mutatedSha);

        File.WriteAllBytes(_registryPath, _originalBytes);
        byte[] restoredSha = BootAndExtractRegistrySha(_registryPath);

        Assert.Equal(originalSha, restoredSha);
    }

    // === Helpers ==========================================================

    /// <summary>
    /// Boot the Q1 host pointed at <paramref name="registryPath"/>, run a
    /// minimal smoke combat that emits a state blob to a temp file, then
    /// deserialize the blob and return its <see cref="ManifestStamp.ContentHash"/>
    /// bytes (the on-wire slot for <c>registry_sha</c>).
    /// </summary>
    private byte[] BootAndExtractRegistrySha(string registryPath)
    {
        string scriptPath = Path.Combine(_tempDir, $"script-{Guid.NewGuid():N}.txt");
        string outPath = Path.Combine(_tempDir, $"blob-{Guid.NewGuid():N}.bin");

        // Single end_turn is enough to advance through one decision boundary;
        // the blob is emitted at exit regardless of victory/defeat outcome.
        File.WriteAllLines(scriptPath, new[] { "end_turn" });

        string[] args =
        {
            "--seed", "42",
            "--character", "silent",
            "--deck", "starter",
            "--relics", "ring_of_the_snake",
            "--encounter", "cultists_normal",
            "--ascension", "0",
            "--registry", registryPath,
            "--script", scriptPath,
            "--out", outPath,
        };

        int exit = Program.Run(
            args, new StringWriter(), new StringWriter(), attachProcessSignals: false);
        Assert.True(
            exit == Program.ExitVictory || exit == Program.ExitDefeat || exit == Program.ExitError,
            $"Program.Run returned unexpected exit code {exit}");
        Assert.True(File.Exists(outPath), $"Expected state-blob at {outPath}");

        byte[] blob = File.ReadAllBytes(outPath);
        StateBlob decoded =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        return decoded.Stamp.ContentHash;
    }

    /// <summary>
    /// Locate <c>contracts/registry/phase1-silent.json</c> by walking up from
    /// the test assembly directory until the <c>contracts/</c> sibling is
    /// found. Avoids hard-coding a working-directory assumption.
    /// </summary>
    private static string LocateCanonicalRegistry()
    {
        string dir = AppContext.BaseDirectory;
        for (int hops = 0; hops < 12 && dir is not null; hops++)
        {
            string candidate = Path.Combine(dir, "contracts", "registry", "phase1-silent.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            DirectoryInfo? parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new FileNotFoundException(
            $"Could not locate contracts/registry/phase1-silent.json walking up from {AppContext.BaseDirectory}.");
    }
}
