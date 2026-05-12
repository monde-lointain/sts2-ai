using System.Buffers.Binary;
using System.Text;

namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// Growable little-endian byte writer for the state codec hot path. Backed by
/// a managed byte array that doubles on overflow. Lifetime is one
/// Serialize call — the writer is allocated, filled, drained to a byte[]
/// once via <see cref="ToArray"/>, and discarded.
///
/// <para>
/// <b>Endianness:</b> all multi-byte integers are little-endian to match
/// <c>RngStateSerializerV1</c> (the format M5 already pins). Cross-platform
/// consumers see the same bytes regardless of host endianness.
/// </para>
///
/// <para>
/// <b>Strings:</b> UTF-8 with a 4-byte little-endian length prefix in bytes
/// (not chars). Maximum string length is 2^31 - 1 bytes.
/// </para>
/// </summary>
internal sealed class ByteWriter
{
    private byte[] _buffer;
    private int _position;

    public ByteWriter(int initialCapacity = 256)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }
        _buffer = new byte[Math.Max(initialCapacity, 16)];
        _position = 0;
    }

    public int Position => _position;

    public void WriteU8(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position] = value;
        _position += 1;
    }

    public void WriteU16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position, 2), value);
        _position += 2;
    }

    public void WriteU32(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position, 4), value);
        _position += 4;
    }

    public void WriteI32(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), value);
        _position += 4;
    }

    public void WriteU64(ulong value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position, 8), value);
        _position += 8;
    }

    public void WriteBool(bool value)
    {
        WriteU8(value ? (byte)1 : (byte)0);
    }

    public void WriteRawBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_position, bytes.Length));
        _position += bytes.Length;
    }

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> bytes)
    {
        // We use i32 length (signed) to match M5's RunRngSet/PlayerRngSet pattern
        // and to surface "negative length" as a corruption signal on read.
        WriteI32(bytes.Length);
        WriteRawBytes(bytes);
    }

    public void WriteLengthPrefixedString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteI32(byteCount);
        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position, byteCount));
        _position += byteCount;
    }

    public byte[] ToArray()
    {
        byte[] result = new byte[_position];
        Array.Copy(_buffer, result, _position);
        return result;
    }

    /// <summary>
    /// Return a read-only view of the bytes written so far without copying.
    /// Used by the trailer pass to hash the body before appending the trailer.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    private void EnsureCapacity(int additional)
    {
        long required = (long)_position + additional;
        if (required <= _buffer.Length)
        {
            return;
        }
        long newCap = _buffer.Length;
        while (newCap < required)
        {
            newCap = newCap == 0 ? 16 : newCap * 2;
        }
        if (newCap > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"ByteWriter capacity overflow: required {required} bytes.");
        }
        byte[] grown = new byte[newCap];
        Array.Copy(_buffer, grown, _position);
        _buffer = grown;
    }
}
