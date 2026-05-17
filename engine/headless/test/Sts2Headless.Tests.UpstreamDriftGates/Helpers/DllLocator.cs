using System;
using System.IO;
using System.Security.Cryptography;

namespace Sts2Headless.Tests.UpstreamDriftGates.Helpers;

/// <summary>
/// Resolves the canonical path to the upstream <c>sts2.dll</c> Steam install
/// and computes its SHA-256 digest for comparison against
/// <c>upstream-pin.json:pinned_dll_sha256</c>.
/// </summary>
internal static class DllLocator
{
    /// <summary>
    /// Returns the canonical Steam-install DLL path, or <see langword="null"/>
    /// if the install is not present on this machine (e.g., a GHA runner).
    /// </summary>
    public static string? TryGetDllPath()
    {
        string steamDir =
            Environment.GetEnvironmentVariable("STEAM_STS2_DIR")
            ?? Path.Combine(
                Environment.GetEnvironmentVariable("HOME") ?? string.Empty,
                "snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64"
            );
        string dllPath = Path.Combine(steamDir, "sts2.dll");
        return File.Exists(dllPath) ? dllPath : null;
    }

    /// <summary>
    /// Computes lowercase hex SHA-256 of the file at <paramref name="dllPath"/>.
    /// </summary>
    public static string ComputeSha256(string dllPath)
    {
        using FileStream fs = File.OpenRead(dllPath);
        byte[] hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
