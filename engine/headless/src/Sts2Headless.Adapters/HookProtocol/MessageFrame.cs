// MessageFrame — fixed-layout protocol frame for one IPC message.
//
// Wire layout (little-endian, all fields tightly packed at fixed offsets):
//
//   offset  size  field
//   ------  ----  ----------------------------------------------------------
//        0     1  type             u8   (MessageType)
//        1     1  reserved         u8   (zero; padding to natural alignment)
//        2     2  schema           u16  (M2 schema version)
//        4     4  length           u32  (payload byte length; excludes header)
//        8     8  correlation_id   u64  (request/response pairing)
//       16   ...  payload          length bytes
//
// HeaderSize = 16 bytes. Total frame size is rounded up to the next multiple
// of 64 bytes on the wire (cache-line alignment so a frame's header and
// (typically) payload share at most a small number of cache lines, and the
// next frame starts on its own line). Padding bytes are zero.
//
// The 64-byte alignment is the Q1 ↔ Q8 wire-format contract. Both sides
// MUST treat the on-ring footprint as `Align64(16 + length)`. This keeps
// the producer's tail advance and the consumer's head advance always on a
// cache-line boundary, eliminating false-sharing between adjacent frames.
//
// MessageType (u8):
//   0x00  Reserved
//   0x01  ManifestRequest      Q1 -> Q8: handshake; payload = encoded Manifest
//   0x02  ManifestResponse     Q8 -> Q1: echo of the manifest (or error)
//   0x03  HookRequest          Q1 -> Q8: hook fire; payload = caller-defined
//   0x04  HookResponse         Q8 -> Q1: response to a HookRequest
//   0x05  Terminate            Either: graceful shutdown; payload empty
//   0x06  Error                Either: payload = (u16 code, utf8 message)
//   0x07..0xFF Reserved        Future use (must reject on receipt)
//
// Why this layout:
//   - All integer fields are naturally aligned for the read pattern.
//   - 16-byte header lets us read the full envelope in one cache-line touch.
//   - Reserved byte at offset 1 keeps `schema` 2-byte aligned without
//     introducing an extra field.
//
// All encode / decode operations are zero-allocation and operate on
// `Span<byte>` / `ReadOnlySpan<byte>`. The ring (T1) provides the byte stream;
// MessageFrame provides the typed view.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// Protocol-message type code. Stable u8; new values appended only.
/// </summary>
public enum MessageType : byte
{
    Reserved = 0x00,
    ManifestRequest = 0x01,
    ManifestResponse = 0x02,
    HookRequest = 0x03,
    HookResponse = 0x04,
    Terminate = 0x05,
    Error = 0x06,
}

/// <summary>
/// Fixed-layout protocol header. 16 bytes wire-size. The full frame on the
/// wire is <see cref="HeaderSize"/> + payload, rounded up to <see cref="WireAlignment"/>.
/// </summary>
public readonly struct MessageHeader : IEquatable<MessageHeader>
{
    /// <summary>Header size in bytes — fixed forever; bumping breaks the wire format.</summary>
    public const int HeaderSize = 16;

    /// <summary>On-wire alignment in bytes. Frames pad to this size.</summary>
    public const int WireAlignment = 64;

    /// <summary>Maximum payload length representable in a u32 length field.</summary>
    public const int MaxPayloadLength = int.MaxValue;

    public MessageType Type { get; }
    public ushort SchemaVersion { get; }
    public int PayloadLength { get; }
    public ulong CorrelationId { get; }

    public MessageHeader(
        MessageType type,
        ushort schemaVersion,
        int payloadLength,
        ulong correlationId
    )
    {
        if (payloadLength < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        Type = type;
        SchemaVersion = schemaVersion;
        PayloadLength = payloadLength;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Total wire-frame size in bytes for a payload of the given length:
    /// header + payload + padding-to-64.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WireFrameSize(int payloadLength)
    {
        if (payloadLength < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        long total = (long)HeaderSize + payloadLength;
        // Round up to multiple of WireAlignment.
        long aligned = (total + WireAlignment - 1) & ~((long)WireAlignment - 1);
        if (aligned > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "frame size overflow");
        return (int)aligned;
    }

    /// <summary>This frame's total wire size including header + payload + padding.</summary>
    public int WireSize => WireFrameSize(PayloadLength);

    /// <summary>Encode header bytes into <paramref name="dest"/> (must be ≥ HeaderSize).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Encode(Span<byte> dest)
    {
        if (dest.Length < HeaderSize)
            throw new ArgumentException("dest too small", nameof(dest));
        dest[0] = (byte)Type;
        dest[1] = 0; // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(2, 2), SchemaVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(4, 4), (uint)PayloadLength);
        BinaryPrimitives.WriteUInt64LittleEndian(dest.Slice(8, 8), CorrelationId);
    }

    /// <summary>
    /// Decode header bytes from <paramref name="src"/>. Throws
    /// <see cref="FormatException"/> on a structurally invalid header
    /// (negative length, reserved byte non-zero).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageHeader Decode(ReadOnlySpan<byte> src)
    {
        if (src.Length < HeaderSize)
            throw new ArgumentException("src too small", nameof(src));
        byte type = src[0];
        byte reserved = src[1];
        if (reserved != 0)
            throw new FormatException(
                $"MessageHeader reserved byte must be 0; observed 0x{reserved:X2}"
            );
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2, 2));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(4, 4));
        if (length > MaxPayloadLength)
            throw new FormatException(
                $"MessageHeader payload length {length} exceeds int.MaxValue"
            );
        ulong corr = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(8, 8));
        return new MessageHeader((MessageType)type, schema, (int)length, corr);
    }

    public bool Equals(MessageHeader other) =>
        Type == other.Type
        && SchemaVersion == other.SchemaVersion
        && PayloadLength == other.PayloadLength
        && CorrelationId == other.CorrelationId;

    public override bool Equals(object? obj) => obj is MessageHeader h && Equals(h);

    public override int GetHashCode() =>
        HashCode.Combine((byte)Type, SchemaVersion, PayloadLength, CorrelationId);

    public static bool operator ==(MessageHeader a, MessageHeader b) => a.Equals(b);

    public static bool operator !=(MessageHeader a, MessageHeader b) => !a.Equals(b);
}

/// <summary>
/// Frame writer: encodes a full wire frame (header + payload + padding) into
/// a scratch buffer caller provides. The caller then submits the scratch
/// buffer's first <see cref="MessageHeader.WireSize"/> bytes to the ring.
/// </summary>
public static class MessageFrame
{
    /// <summary>
    /// Encode a full wire frame into <paramref name="dest"/>. Padding bytes
    /// after the payload are zeroed. Returns the number of bytes written
    /// (== WireSize). Zero allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Encode(MessageHeader header, ReadOnlySpan<byte> payload, Span<byte> dest)
    {
        if (payload.Length != header.PayloadLength)
        {
            throw new ArgumentException(
                $"payload length {payload.Length} != header.PayloadLength {header.PayloadLength}",
                nameof(payload)
            );
        }
        int wire = header.WireSize;
        if (dest.Length < wire)
            throw new ArgumentException("dest too small", nameof(dest));
        header.Encode(dest);
        payload.CopyTo(dest.Slice(MessageHeader.HeaderSize, payload.Length));
        // Zero the alignment padding so debug-tooling sees clean bytes.
        int padStart = MessageHeader.HeaderSize + payload.Length;
        if (padStart < wire)
        {
            dest.Slice(padStart, wire - padStart).Clear();
        }
        return wire;
    }
}
