// HookProtocolManifest — the GameVersionManifest (Q1-ADR-005) used in the
// session-establish handshake. Both Q1 and Q8 send a manifest; both reject
// any mismatch with an explicit error. Never silently coerce.
//
// Wire payload (after the standard 16-byte MessageHeader):
//
//   offset  size  field
//   ------  ----  -------------------------------------------------------------
//        0    32  content_hash      sha256 / blake of the Q4 content manifest
//       32     2  schema_version    u16   (state-codec schema version)
//       34     2  reserved          (zero)
//       36     4  ring_capacity     u32   (bytes; informational; must match)
//       40     4  build_id_length   u32   (utf-8 byte length)
//       44   ...  build_id_bytes    utf-8 string identifying the Q1 build
//
// Why include ring_capacity:
//   The peer attaches the same shared-memory segment by path; both sides must
//   independently know capacity to construct a valid SpscRingBuffer. Echoing
//   it back catches a class of cross-build mistakes where the peer was
//   compiled against a different default.
//
// Why include build_id:
//   For operator/log clarity, not for safety. content_hash + schema_version
//   are sufficient for correctness; build_id is a human-readable tag the
//   supervisor surfaces in logs when a mismatch occurs.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// Bidirectional handshake manifest (Q1-ADR-005). Content-hash mismatch or
/// schema-version mismatch terminates the session at handshake time.
/// </summary>
public sealed class HookProtocolManifest : IEquatable<HookProtocolManifest>
{
    /// <summary>Fixed size of the content hash in bytes (32 = SHA-256-sized).</summary>
    public const int ContentHashSize = 32;

    public ReadOnlyMemory<byte> ContentHash { get; }
    public ushort SchemaVersion { get; }
    public int RingCapacity { get; }
    public string BuildId { get; }

    public HookProtocolManifest(
        ReadOnlyMemory<byte> contentHash,
        ushort schemaVersion,
        int ringCapacity,
        string buildId
    )
    {
        if (contentHash.Length != ContentHashSize)
        {
            throw new ArgumentException(
                $"contentHash must be exactly {ContentHashSize} bytes; got {contentHash.Length}",
                nameof(contentHash)
            );
        }
        if (ringCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(ringCapacity));
        ArgumentNullException.ThrowIfNull(buildId);
        ContentHash = contentHash;
        SchemaVersion = schemaVersion;
        RingCapacity = ringCapacity;
        BuildId = buildId;
    }

    /// <summary>Encoded payload size for this manifest (excludes the MessageHeader envelope).</summary>
    public int EncodedSize
    {
        get
        {
            int idBytes = Encoding.UTF8.GetByteCount(BuildId);
            return ContentHashSize + 2 + 2 + 4 + 4 + idBytes;
        }
    }

    /// <summary>Encode this manifest's payload bytes into <paramref name="dest"/>.</summary>
    public int Encode(Span<byte> dest)
    {
        int size = EncodedSize;
        if (dest.Length < size)
            throw new ArgumentException("dest too small", nameof(dest));

        ContentHash.Span.CopyTo(dest);
        int off = ContentHashSize;
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(off, 2), SchemaVersion);
        off += 2;
        // reserved (zero)
        dest[off] = 0;
        dest[off + 1] = 0;
        off += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(off, 4), (uint)RingCapacity);
        off += 4;
        int idBytes = Encoding.UTF8.GetBytes(BuildId, dest.Slice(off + 4));
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(off, 4), (uint)idBytes);
        return off + 4 + idBytes;
    }

    /// <summary>
    /// Decode a manifest payload. Throws <see cref="FormatException"/> on
    /// malformed bytes (truncated, reserved-byte non-zero, oversized build_id_length).
    /// </summary>
    public static HookProtocolManifest Decode(ReadOnlySpan<byte> src)
    {
        if (src.Length < ContentHashSize + 2 + 2 + 4 + 4)
        {
            throw new FormatException(
                $"Manifest payload truncated: {src.Length} bytes (< minimum {ContentHashSize + 12})"
            );
        }
        byte[] hash = src.Slice(0, ContentHashSize).ToArray();
        int off = ContentHashSize;
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(off, 2));
        off += 2;
        if (src[off] != 0 || src[off + 1] != 0)
        {
            throw new FormatException("Manifest reserved bytes must be zero");
        }
        off += 2;
        uint cap = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(off, 4));
        if (cap == 0 || cap > int.MaxValue)
        {
            throw new FormatException($"Manifest ring_capacity {cap} out of range");
        }
        off += 4;
        uint idLen = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(off, 4));
        off += 4;
        if (idLen > int.MaxValue - off || off + idLen > src.Length)
        {
            throw new FormatException(
                $"Manifest build_id_length {idLen} exceeds remaining buffer ({src.Length - off})"
            );
        }
        string id = idLen == 0 ? string.Empty : Encoding.UTF8.GetString(src.Slice(off, (int)idLen));
        return new HookProtocolManifest(hash, schema, (int)cap, id);
    }

    /// <summary>
    /// Compare two manifests for safety-relevant equality: content hash and
    /// schema version MUST match exactly. Ring capacity must also match
    /// (otherwise neither side can construct a valid ring). Build id is
    /// informational and not compared.
    /// </summary>
    public bool IsCompatibleWith(HookProtocolManifest other)
    {
        if (other is null)
            return false;
        if (SchemaVersion != other.SchemaVersion)
            return false;
        if (RingCapacity != other.RingCapacity)
            return false;
        if (!ContentHash.Span.SequenceEqual(other.ContentHash.Span))
            return false;
        return true;
    }

    /// <summary>
    /// Produce a one-line description of what's incompatible (for error
    /// messages). Returns null if <see cref="IsCompatibleWith"/> is true.
    /// </summary>
    public string? DescribeMismatch(HookProtocolManifest other)
    {
        if (other is null)
            return "peer manifest is null";
        if (SchemaVersion != other.SchemaVersion)
        {
            return $"schema_version mismatch: local={SchemaVersion} peer={other.SchemaVersion}";
        }
        if (RingCapacity != other.RingCapacity)
        {
            return $"ring_capacity mismatch: local={RingCapacity} peer={other.RingCapacity}";
        }
        if (!ContentHash.Span.SequenceEqual(other.ContentHash.Span))
        {
            return $"content_hash mismatch: local={HashHex(ContentHash.Span)} peer={HashHex(other.ContentHash.Span)} (build_id local='{BuildId}' peer='{other.BuildId}')";
        }
        return null;
    }

    private static string HashHex(ReadOnlySpan<byte> b)
    {
        // Trim to 8 bytes for log compactness; full hash is in audit-quality
        // logs upstream. We never use hash for routing — only equality.
        return Convert.ToHexString(b[..Math.Min(8, b.Length)]);
    }

    public bool Equals(HookProtocolManifest? other)
    {
        if (other is null)
            return false;
        return IsCompatibleWith(other) && BuildId == other.BuildId;
    }

    public override bool Equals(object? obj) => Equals(obj as HookProtocolManifest);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, RingCapacity, BuildId);
}

/// <summary>
/// Raised when the handshake fails — peer manifest mismatch, protocol-level
/// framing error, or schema rejection.
/// </summary>
public sealed class HookProtocolHandshakeException : Exception
{
    public HookProtocolHandshakeException(string message)
        : base(message) { }

    public HookProtocolHandshakeException(string message, Exception inner)
        : base(message, inner) { }
}
