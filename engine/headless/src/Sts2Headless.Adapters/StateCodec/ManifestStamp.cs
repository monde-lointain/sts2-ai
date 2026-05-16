using System.Security.Cryptography;
using System.Text;

namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// The (git-sha, build-id, content-hash) triple stamped on every state blob.
/// Per Q1-ADR-005 and the M1 module spec, the manifest is informational at
/// the codec level — IPC handshake (M2) enforces cross-process equality, but
/// the codec itself never refuses to deserialize a mismatched stamp.
///
/// <para>
/// <b>GitSha:</b> source-tree commit hash of the Q1 binary, supplied by the
/// caller (M9 host) so the codec stays free of build-time globals. Stored as
/// a UTF-8 string; the length is constrained to fit in a u8 (max 255 bytes).
/// </para>
///
/// <para>
/// <b>BuildId:</b> arbitrary caller-supplied build label (e.g., "Q1-Phase1-
/// 2026-05-11-001"). UTF-8, max 65535 bytes (u16 length).
/// </para>
///
/// <para>
/// <b>ContentHash:</b> SHA-256 of the registered-content id set. The recipe
/// (<see cref="ContentHashFromIds"/>): sort the id strings ASCII-ordinal
/// ascending, join with single 0x00 bytes, UTF-8-encode, feed to SHA-256.
/// 32 bytes. The pre-sort makes the hash independent of insertion order so
/// content registration can shuffle without altering the manifest fingerprint.
/// </para>
/// </summary>
/// <param name="GitSha">Source-tree commit hash (UTF-8, max 255 bytes).</param>
/// <param name="BuildId">Arbitrary build label (UTF-8, max 65535 bytes).</param>
/// <param name="ContentHash">SHA-256 of the registered-content id set (32 bytes).</param>
public sealed record ManifestStamp(string GitSha, string BuildId, byte[] ContentHash)
{
    /// <summary>
    /// Compute the canonical content hash from a set of catalog ids. Recipe:
    /// sort ids ASCII-ordinal ascending, join with 0x00, UTF-8 → SHA-256.
    /// Same set of ids yields the same hash regardless of source ordering.
    /// </summary>
    public static byte[] ContentHashFromIds(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        // Stable sort (ASCII ordinal) — independent of culture/case.
        string[] sorted = ids.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] nullSep = new byte[] { 0x00 };
        for (int i = 0; i < sorted.Length; i++)
        {
            if (i > 0)
            {
                sha.AppendData(nullSep);
            }
            sha.AppendData(Encoding.UTF8.GetBytes(sorted[i]));
        }
        return sha.GetHashAndReset();
    }

    /// <summary>Override required because <see cref="ContentHash"/> is a reference type.</summary>
    public bool Equals(ManifestStamp? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (!string.Equals(GitSha, other.GitSha, StringComparison.Ordinal))
            return false;
        if (!string.Equals(BuildId, other.BuildId, StringComparison.Ordinal))
            return false;
        if (ContentHash.Length != other.ContentHash.Length)
            return false;
        return ContentHash.AsSpan().SequenceEqual(other.ContentHash);
    }

    /// <summary>Override required to match <see cref="Equals(ManifestStamp?)"/>.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        h.Add(GitSha, StringComparer.Ordinal);
        h.Add(BuildId, StringComparer.Ordinal);
        for (int i = 0; i < ContentHash.Length; i++)
        {
            h.Add(ContentHash[i]);
        }
        return h.ToHashCode();
    }
}
