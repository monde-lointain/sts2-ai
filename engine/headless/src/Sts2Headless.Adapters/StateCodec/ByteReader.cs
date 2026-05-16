using System.Buffers.Binary;
using System.Text;

namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// Little-endian span-based byte reader for the state codec deserialize path.
/// All bounds-check failures throw <see cref="StateCodecException"/> so the
/// caller can blanket-catch "load failed" without distinguishing primitive
/// versus structural errors.
/// </summary>
internal ref struct ByteReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public ByteReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public int Position => _position;
    public int Length => _buffer.Length;
    public int Remaining => _buffer.Length - _position;
    public bool IsAtEnd => _position >= _buffer.Length;

    public byte ReadU8()
    {
        EnsureAvailable(1, "u8");
        byte v = _buffer[_position];
        _position += 1;
        return v;
    }

    public ushort ReadU16()
    {
        EnsureAvailable(2, "u16");
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_position, 2));
        _position += 2;
        return v;
    }

    public uint ReadU32()
    {
        EnsureAvailable(4, "u32");
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return v;
    }

    public int ReadI32()
    {
        EnsureAvailable(4, "i32");
        int v = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return v;
    }

    public ulong ReadU64()
    {
        EnsureAvailable(8, "u64");
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_position, 8));
        _position += 8;
        return v;
    }

    public bool ReadBool()
    {
        byte v = ReadU8();
        // We accept only 0/1 — anything else signals corruption.
        return v switch
        {
            0 => false,
            1 => true,
            _ => throw new StateCodecException(
                $"ByteReader: invalid bool byte 0x{v:X2} at offset {_position - 1}."
            ),
        };
    }

    public ReadOnlySpan<byte> ReadRawBytes(int count)
    {
        if (count < 0)
        {
            throw new StateCodecException($"ByteReader: negative read count {count}.");
        }
        EnsureAvailable(count, "raw bytes");
        ReadOnlySpan<byte> slice = _buffer.Slice(_position, count);
        _position += count;
        return slice;
    }

    public byte[] ReadLengthPrefixedBytes()
    {
        int length = ReadI32();
        if (length < 0)
        {
            throw new StateCodecException(
                $"ByteReader: negative length {length} for length-prefixed bytes."
            );
        }
        return ReadRawBytes(length).ToArray();
    }

    public string ReadLengthPrefixedString()
    {
        int length = ReadI32();
        if (length < 0)
        {
            throw new StateCodecException(
                $"ByteReader: negative length {length} for length-prefixed string."
            );
        }
        ReadOnlySpan<byte> bytes = ReadRawBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private void EnsureAvailable(int wanted, string field)
    {
        // Long-arithmetic to dodge int overflow on hostile/large length prefixes.
        if (wanted < 0 || (long)_position + wanted > _buffer.Length)
        {
            throw new StateCodecException(
                $"ByteReader: unexpected EOF reading {field}: "
                    + $"wanted {wanted} bytes at offset {_position}, "
                    + $"buffer length {_buffer.Length}."
            );
        }
    }
}
