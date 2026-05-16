using System.Buffers.Binary;
using System.Text;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// TDD-first tests for the low-level byte writers/readers used by every
/// state-codec section. Endianness is little-endian (matches M5's
/// RngStateSerializerV1). All operations are span-based so the section
/// serializers can chain them without intermediate allocations.
/// </summary>
public class ByteCodecPrimitivesTests
{
    [Fact]
    public void Writer_writes_u8_le()
    {
        ByteWriter w = new();
        w.WriteU8(0x42);
        w.WriteU8(0xFF);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x42, 0xFF }, bytes);
    }

    [Fact]
    public void Writer_writes_u16_le()
    {
        ByteWriter w = new();
        w.WriteU16(0x1234);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x34, 0x12 }, bytes);
    }

    [Fact]
    public void Writer_writes_u32_le()
    {
        ByteWriter w = new();
        w.WriteU32(0xDEADBEEF);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBE, 0xAD, 0xDE }, bytes);
    }

    [Fact]
    public void Writer_writes_i32_le_negative()
    {
        ByteWriter w = new();
        w.WriteI32(-1);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, bytes);
    }

    [Fact]
    public void Writer_writes_u64_le()
    {
        ByteWriter w = new();
        w.WriteU64(0x0102030405060708UL);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, bytes);
    }

    [Fact]
    public void Writer_writes_bool_as_one_byte()
    {
        ByteWriter w = new();
        w.WriteBool(true);
        w.WriteBool(false);
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x01, 0x00 }, bytes);
    }

    [Fact]
    public void Writer_writes_lengthprefixed_string_utf8()
    {
        ByteWriter w = new();
        w.WriteLengthPrefixedString("ab");
        byte[] bytes = w.ToArray();
        // u32 length=2 LE + "ab" utf8
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x61, 0x62 }, bytes);
    }

    [Fact]
    public void Writer_writes_lengthprefixed_string_unicode()
    {
        ByteWriter w = new();
        w.WriteLengthPrefixedString("é"); // utf8: C3 A9
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00, 0xC3, 0xA9 }, bytes);
    }

    [Fact]
    public void Writer_writes_lengthprefixed_bytes()
    {
        ByteWriter w = new();
        w.WriteLengthPrefixedBytes(new byte[] { 0xAA, 0xBB });
        byte[] bytes = w.ToArray();
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00, 0xAA, 0xBB }, bytes);
    }

    [Fact]
    public void Writer_grows_buffer_past_initial_capacity()
    {
        ByteWriter w = new(initialCapacity: 4);
        for (int i = 0; i < 100; i++)
        {
            w.WriteU32((uint)i);
        }
        byte[] bytes = w.ToArray();
        Assert.Equal(400, bytes.Length);
        for (int i = 0; i < 100; i++)
        {
            uint val = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
            Assert.Equal((uint)i, val);
        }
    }

    [Fact]
    public void Reader_reads_u8()
    {
        byte[] data = { 0x42, 0xFF };
        ByteReader r = new(data);
        Assert.Equal((byte)0x42, r.ReadU8());
        Assert.Equal((byte)0xFF, r.ReadU8());
        Assert.True(r.IsAtEnd);
    }

    [Fact]
    public void Reader_reads_u16_le()
    {
        byte[] data = { 0x34, 0x12 };
        ByteReader r = new(data);
        Assert.Equal((ushort)0x1234, r.ReadU16());
    }

    [Fact]
    public void Reader_reads_u32_le()
    {
        byte[] data = { 0xEF, 0xBE, 0xAD, 0xDE };
        ByteReader r = new(data);
        Assert.Equal(0xDEADBEEFu, r.ReadU32());
    }

    [Fact]
    public void Reader_reads_i32_le()
    {
        byte[] data = { 0xFF, 0xFF, 0xFF, 0xFF };
        ByteReader r = new(data);
        Assert.Equal(-1, r.ReadI32());
    }

    [Fact]
    public void Reader_reads_u64_le()
    {
        byte[] data = { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
        ByteReader r = new(data);
        Assert.Equal(0x0102030405060708UL, r.ReadU64());
    }

    [Fact]
    public void Reader_reads_bool()
    {
        byte[] data = { 0x01, 0x00 };
        ByteReader r = new(data);
        Assert.True(r.ReadBool());
        Assert.False(r.ReadBool());
    }

    [Fact]
    public void Reader_reads_lengthprefixed_string_utf8()
    {
        byte[] data = { 0x02, 0x00, 0x00, 0x00, 0x61, 0x62 };
        ByteReader r = new(data);
        Assert.Equal("ab", r.ReadLengthPrefixedString());
    }

    [Fact]
    public void Reader_reads_lengthprefixed_bytes()
    {
        byte[] data = { 0x02, 0x00, 0x00, 0x00, 0xAA, 0xBB };
        ByteReader r = new(data);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, r.ReadLengthPrefixedBytes());
    }

    // ByteReader is a ref-struct so we can't capture it in Assert.Throws lambdas;
    // these helpers do the call eagerly and report whether the expected throw fired.
    private static StateCodecException? CatchOnReadU32(byte[] data)
    {
        try
        {
            ByteReader r = new(data);
            _ = r.ReadU32();
            return null;
        }
        catch (StateCodecException ex)
        {
            return ex;
        }
    }

    private static StateCodecException? CatchOnReadString(byte[] data)
    {
        try
        {
            ByteReader r = new(data);
            _ = r.ReadLengthPrefixedString();
            return null;
        }
        catch (StateCodecException ex)
        {
            return ex;
        }
    }

    private static StateCodecException? CatchOnReadBytes(byte[] data)
    {
        try
        {
            ByteReader r = new(data);
            _ = r.ReadLengthPrefixedBytes();
            return null;
        }
        catch (StateCodecException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Reader_throws_on_unexpected_eof_u32()
    {
        byte[] data = { 0x01, 0x02 };
        Assert.NotNull(CatchOnReadU32(data));
    }

    [Fact]
    public void Reader_throws_on_unexpected_eof_string()
    {
        byte[] data = { 0xFF, 0xFF, 0xFF, 0x7F, 0x00 }; // claims 2GB string, 1 byte available
        Assert.NotNull(CatchOnReadString(data));
    }

    [Fact]
    public void Reader_throws_on_negative_length()
    {
        byte[] data = { 0xFF, 0xFF, 0xFF, 0xFF }; // length = -1 if read as i32
        Assert.NotNull(CatchOnReadBytes(data));
    }

    [Fact]
    public void Roundtrip_string_round_trips()
    {
        string source = "hello, world — έχω αυτό 🎲";
        ByteWriter w = new();
        w.WriteLengthPrefixedString(source);
        ByteReader r = new(w.ToArray());
        Assert.Equal(source, r.ReadLengthPrefixedString());
        Assert.True(r.IsAtEnd);
    }

    [Fact]
    public void Roundtrip_mixed_primitives_round_trip()
    {
        ByteWriter w = new();
        w.WriteU8(0xAB);
        w.WriteU16(0xCAFE);
        w.WriteU32(0xDEADBEEF);
        w.WriteI32(-12345);
        w.WriteU64(0xFEEDFACEDEADBEEFUL);
        w.WriteBool(true);
        w.WriteLengthPrefixedString("hi");
        w.WriteLengthPrefixedBytes(new byte[] { 1, 2, 3 });

        ByteReader r = new(w.ToArray());
        Assert.Equal((byte)0xAB, r.ReadU8());
        Assert.Equal((ushort)0xCAFE, r.ReadU16());
        Assert.Equal(0xDEADBEEFu, r.ReadU32());
        Assert.Equal(-12345, r.ReadI32());
        Assert.Equal(0xFEEDFACEDEADBEEFUL, r.ReadU64());
        Assert.True(r.ReadBool());
        Assert.Equal("hi", r.ReadLengthPrefixedString());
        Assert.Equal(new byte[] { 1, 2, 3 }, r.ReadLengthPrefixedBytes());
        Assert.True(r.IsAtEnd);
    }

    [Fact]
    public void Empty_string_round_trips()
    {
        ByteWriter w = new();
        w.WriteLengthPrefixedString(string.Empty);
        ByteReader r = new(w.ToArray());
        Assert.Equal(string.Empty, r.ReadLengthPrefixedString());
    }

    [Fact]
    public void Writer_position_reports_byte_count()
    {
        ByteWriter w = new();
        Assert.Equal(0, w.Position);
        w.WriteU32(1);
        Assert.Equal(4, w.Position);
        w.WriteU8(1);
        Assert.Equal(5, w.Position);
    }
}
