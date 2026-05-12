using System.Security.Cryptography;

namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Deterministic, cross-process, cross-platform hash of an arbitrary byte
/// payload. The localization tool for the S13 determinism probe (per
/// Q1-ADR-007): when probe and Godot diverge at some checkpoint, the
/// canonical hash of the suspect state section is the first bisection lever.
///
/// Algorithm choice: SHA-256 via <see cref="SHA256.HashData(ReadOnlySpan{byte})"/>.
/// .NET's SHA-256 is a deterministic software implementation; hardware
/// acceleration when present must produce identical output by spec.
///
/// Output format: 64 lowercase hex characters (no prefix, no separators).
/// Lowercase is fixed so equality comparisons in logs and CI artefacts are
/// case-insensitive-by-construction rather than case-insensitive-by-policy.
///
/// IMPORTANT: callers must control input ordering. Hashing a
/// <c>Dictionary&lt;,&gt;</c>'s default enumeration produces nondeterministic
/// output (R6 risk in the S1 prompt). Callers serialize via
/// <see cref="IRngStateSerializer"/> first (which is ordered) and pass the
/// resulting bytes to this function.
/// </summary>
public static class CanonicalHash
{
    private const int Sha256HexLength = 64;

    public static string Sha256Hex(ReadOnlySpan<byte> input)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Length, in characters, of the lowercase-hex SHA-256 output from
    /// <see cref="Sha256Hex(ReadOnlySpan{byte})"/>. Exposed for callers that
    /// need to validate or allocate.
    /// </summary>
    public static int HexLength => Sha256HexLength;
}
