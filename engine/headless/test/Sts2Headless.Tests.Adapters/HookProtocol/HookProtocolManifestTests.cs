// HookProtocolManifest tests.
//
// What we pin:
//   - Roundtrip encode/decode preserves all fields.
//   - LE byte layout of the manifest matches the spec at every offset.
//   - 32-byte content hash is enforced (other sizes rejected).
//   - Reserved bytes non-zero rejected.
//   - Truncated bytes rejected.
//   - IsCompatibleWith returns the right answers on each mismatch axis.
//   - DescribeMismatch names exactly the field that differs.
//   - Empty build_id is allowed.

using System;
using System.Buffers.Binary;
using System.Text;
using Sts2Headless.Adapters.HookProtocol;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

public class HookProtocolManifestTests
{
    private static byte[] FixedHash(byte b)
    {
        byte[] h = new byte[HookProtocolManifest.ContentHashSize];
        for (int i = 0; i < h.Length; i++)
            h[i] = (byte)(b + i);
        return h;
    }

    [Fact]
    public void Roundtrip_recovers_all_fields()
    {
        var m = new HookProtocolManifest(FixedHash(0x10), 7, 65536, "q1-build-abc123");
        byte[] buf = new byte[m.EncodedSize];
        int n = m.Encode(buf);
        Assert.Equal(m.EncodedSize, n);
        var r = HookProtocolManifest.Decode(buf);
        Assert.True(r.ContentHash.Span.SequenceEqual(m.ContentHash.Span));
        Assert.Equal(7, r.SchemaVersion);
        Assert.Equal(65536, r.RingCapacity);
        Assert.Equal("q1-build-abc123", r.BuildId);
    }

    [Fact]
    public void Wire_layout_is_exact()
    {
        byte[] hash = FixedHash(0xA0);
        var m = new HookProtocolManifest(hash, 0x1234, 0x00010000, "X");
        byte[] buf = new byte[m.EncodedSize];
        m.Encode(buf);
        // Bytes 0..31: hash.
        for (int i = 0; i < 32; i++)
            Assert.Equal(hash[i], buf[i]);
        // Bytes 32..33: schema u16 LE = 0x1234
        Assert.Equal(0x34, buf[32]);
        Assert.Equal(0x12, buf[33]);
        // Bytes 34..35: reserved zero.
        Assert.Equal(0, buf[34]);
        Assert.Equal(0, buf[35]);
        // Bytes 36..39: ring_capacity u32 LE = 0x00010000
        Assert.Equal(0x00, buf[36]);
        Assert.Equal(0x00, buf[37]);
        Assert.Equal(0x01, buf[38]);
        Assert.Equal(0x00, buf[39]);
        // Bytes 40..43: build_id_length u32 LE = 1
        Assert.Equal(0x01, buf[40]);
        Assert.Equal(0x00, buf[41]);
        Assert.Equal(0x00, buf[42]);
        Assert.Equal(0x00, buf[43]);
        // Byte 44: 'X'
        Assert.Equal((byte)'X', buf[44]);
    }

    [Fact]
    public void Empty_build_id_supported()
    {
        var m = new HookProtocolManifest(FixedHash(1), 1, 1024, "");
        byte[] buf = new byte[m.EncodedSize];
        m.Encode(buf);
        var r = HookProtocolManifest.Decode(buf);
        Assert.Equal("", r.BuildId);
    }

    [Fact]
    public void Hash_wrong_size_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new HookProtocolManifest(new byte[16], 1, 1024, "x")
        );
    }

    [Fact]
    public void Decode_truncated_bytes_rejected()
    {
        var m = new HookProtocolManifest(FixedHash(2), 1, 1024, "abc");
        byte[] buf = new byte[m.EncodedSize];
        m.Encode(buf);
        // Truncate by one byte; build_id_length says 3 but only 2 remain.
        Assert.Throws<FormatException>(() =>
            HookProtocolManifest.Decode(buf.AsSpan(0, buf.Length - 1))
        );
    }

    [Fact]
    public void Decode_reserved_nonzero_rejected()
    {
        var m = new HookProtocolManifest(FixedHash(3), 1, 1024, "x");
        byte[] buf = new byte[m.EncodedSize];
        m.Encode(buf);
        buf[34] = 0xFF; // corrupt the reserved byte
        Assert.Throws<FormatException>(() => HookProtocolManifest.Decode(buf));
    }

    [Fact]
    public void Decode_oversized_buildid_length_rejected()
    {
        var m = new HookProtocolManifest(FixedHash(4), 1, 1024, "x");
        byte[] buf = new byte[m.EncodedSize];
        m.Encode(buf);
        // Overwrite the build_id_length field with a value past the buffer end.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(40, 4), 1024u);
        Assert.Throws<FormatException>(() => HookProtocolManifest.Decode(buf));
    }

    [Fact]
    public void IsCompatibleWith_true_on_identical_fields()
    {
        var a = new HookProtocolManifest(FixedHash(5), 3, 4096, "q1-x");
        var b = new HookProtocolManifest(FixedHash(5), 3, 4096, "q1-x");
        Assert.True(a.IsCompatibleWith(b));
        Assert.Null(a.DescribeMismatch(b));
    }

    [Fact]
    public void IsCompatibleWith_false_on_schema_mismatch()
    {
        var a = new HookProtocolManifest(FixedHash(5), 3, 4096, "q1-x");
        var b = new HookProtocolManifest(FixedHash(5), 4, 4096, "q1-x");
        Assert.False(a.IsCompatibleWith(b));
        Assert.Contains("schema_version", a.DescribeMismatch(b)!, StringComparison.Ordinal);
    }

    [Fact]
    public void IsCompatibleWith_false_on_ring_capacity_mismatch()
    {
        var a = new HookProtocolManifest(FixedHash(5), 3, 4096, "q1-x");
        var b = new HookProtocolManifest(FixedHash(5), 3, 8192, "q1-x");
        Assert.False(a.IsCompatibleWith(b));
        Assert.Contains("ring_capacity", a.DescribeMismatch(b)!, StringComparison.Ordinal);
    }

    [Fact]
    public void IsCompatibleWith_false_on_content_hash_mismatch()
    {
        var a = new HookProtocolManifest(FixedHash(5), 3, 4096, "q1-x");
        var b = new HookProtocolManifest(FixedHash(6), 3, 4096, "q1-y");
        Assert.False(a.IsCompatibleWith(b));
        Assert.Contains("content_hash", a.DescribeMismatch(b)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_id_difference_is_informational_only()
    {
        // Different build IDs but same hash/schema/capacity -> COMPATIBLE.
        var a = new HookProtocolManifest(FixedHash(7), 3, 4096, "q1-build-a");
        var b = new HookProtocolManifest(FixedHash(7), 3, 4096, "q1-build-b");
        Assert.True(a.IsCompatibleWith(b));
    }
}
