using System.Security.Cryptography;

namespace Sts2Headless.Host;

/// <summary>
/// D6 (2026-05-12): single source-of-truth for the Q4 token-registry SHA
/// stamped into every Q1 state-blob's <c>ManifestStamp.ContentHash</c> slot
/// (the wire position for <c>state_blob.proto/registry_sha</c>).
///
/// <para>
/// <b>Single-source contract.</b> This is the ONE place in the Q1 process
/// that reads <c>contracts/registry/phase1-silent.json</c> at boot and feeds
/// its bytes to SHA-256. Any future Q1 path that needs the registry SHA MUST
/// route through <see cref="ReadRegistryShaBytes(string)"/> — adding a
/// second read/compute site is a regression caught by
/// <c>RegistryShaRoundtripTests</c>.
/// </para>
///
/// <para>
/// <b>Stateless.</b> The helper does not cache across calls; the boot path
/// invokes it exactly once. Per-test isolation requires that successive
/// <see cref="ReadRegistryShaBytes(string)"/> calls observe the file as-of
/// the call (no stale-cache bug), which the round-trip test verifies.
/// </para>
/// </summary>
public static class RegistryShaProvider
{
    /// <summary>
    /// Read the registry file at <paramref name="path"/> and return the
    /// SHA-256 of its bytes (32 bytes). Throws
    /// <see cref="FileNotFoundException"/> if the path is missing.
    /// </summary>
    public static byte[] ReadRegistryShaBytes(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"RegistryShaProvider: registry file not found at '{path}'.", path);
        }
        byte[] bytes = File.ReadAllBytes(path);
        return SHA256.HashData(bytes);
    }
}
