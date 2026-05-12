using System.Buffers.Binary;
using System.Text;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Serialize/deserialize a <see cref="ManifestStamp"/> using the same byte
/// layout as S7's State Codec header stamp block:
/// <code>
///   u8  git_sha_len
///   utf8 git_sha
///   u16 build_id_len (little-endian)
///   utf8 build_id
///   32  content_hash bytes
/// </code>
///
/// <para>
/// The on-wire shape is identical to S7's stamp encoding; this class is a
/// separate implementation only to keep the Replay namespace independent of
/// the State Codec's internal byte primitives. The byte output for the same
/// stamp must equal S7's encoding byte-for-byte — verified by tests.
/// </para>
/// </summary>
internal static class ManifestStampCodec
{
    public const int Sha256ByteLength = 32;

    /// <summary>Encode the stamp to a fresh byte array.</summary>
    public static byte[] Encode(ManifestStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);

        byte[] gitShaBytes = Encoding.UTF8.GetBytes(stamp.GitSha);
        if (gitShaBytes.Length > byte.MaxValue)
        {
            throw new ArgumentException(
                $"ManifestStamp.GitSha exceeds 255 UTF-8 bytes ({gitShaBytes.Length}).", nameof(stamp));
        }
        byte[] buildIdBytes = Encoding.UTF8.GetBytes(stamp.BuildId);
        if (buildIdBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"ManifestStamp.BuildId exceeds 65535 UTF-8 bytes ({buildIdBytes.Length}).", nameof(stamp));
        }
        if (stamp.ContentHash.Length != Sha256ByteLength)
        {
            throw new ArgumentException(
                $"ManifestStamp.ContentHash must be {Sha256ByteLength} bytes (got {stamp.ContentHash.Length}).",
                nameof(stamp));
        }

        int totalLen = 1 + gitShaBytes.Length + 2 + buildIdBytes.Length + Sha256ByteLength;
        byte[] result = new byte[totalLen];
        int pos = 0;
        result[pos] = (byte)gitShaBytes.Length;
        pos += 1;
        gitShaBytes.AsSpan().CopyTo(result.AsSpan(pos, gitShaBytes.Length));
        pos += gitShaBytes.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(pos, 2), (ushort)buildIdBytes.Length);
        pos += 2;
        buildIdBytes.AsSpan().CopyTo(result.AsSpan(pos, buildIdBytes.Length));
        pos += buildIdBytes.Length;
        stamp.ContentHash.AsSpan().CopyTo(result.AsSpan(pos, Sha256ByteLength));
        return result;
    }

    /// <summary>
    /// Decode <paramref name="bytes"/> as a <see cref="ManifestStamp"/>. The
    /// span must contain exactly the encoded stamp body — extra trailing
    /// bytes are a protocol violation and throw.
    /// </summary>
    public static ManifestStamp Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 1)
        {
            throw new ReplayException("ManifestStampCodec.Decode: buffer too short for git_sha_len.");
        }
        int pos = 0;
        byte gitShaLen = bytes[pos];
        pos += 1;
        if (pos + gitShaLen > bytes.Length)
        {
            throw new ReplayException(
                $"ManifestStampCodec.Decode: git_sha_len={gitShaLen} exceeds buffer (remaining {bytes.Length - pos}).");
        }
        string gitSha = Encoding.UTF8.GetString(bytes.Slice(pos, gitShaLen));
        pos += gitShaLen;

        if (pos + 2 > bytes.Length)
        {
            throw new ReplayException("ManifestStampCodec.Decode: buffer too short for build_id_len.");
        }
        ushort buildIdLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(pos, 2));
        pos += 2;
        if (pos + buildIdLen > bytes.Length)
        {
            throw new ReplayException(
                $"ManifestStampCodec.Decode: build_id_len={buildIdLen} exceeds buffer (remaining {bytes.Length - pos}).");
        }
        string buildId = Encoding.UTF8.GetString(bytes.Slice(pos, buildIdLen));
        pos += buildIdLen;

        if (pos + Sha256ByteLength > bytes.Length)
        {
            throw new ReplayException(
                $"ManifestStampCodec.Decode: buffer too short for content_hash (remaining {bytes.Length - pos}).");
        }
        byte[] contentHash = bytes.Slice(pos, Sha256ByteLength).ToArray();
        pos += Sha256ByteLength;

        if (pos != bytes.Length)
        {
            throw new ReplayException(
                $"ManifestStampCodec.Decode: {bytes.Length - pos} trailing bytes after content_hash.");
        }
        return new ManifestStamp(gitSha, buildId, contentHash);
    }
}
