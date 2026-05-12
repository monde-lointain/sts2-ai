using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// Verify <c>ManifestStampCodec</c> encode/decode roundtrip and that the byte
/// shape exactly matches the documented layout (u8 git_sha_len + utf8 + u16
/// build_id_len + utf8 + 32 content_hash). The on-wire shape mirrors S7's
/// StateCodec stamp encoding.
/// </summary>
public class ManifestStampCodecTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    [Fact]
    public void Encode_then_Decode_roundtrips_simple_ascii()
    {
        ManifestStamp s = new("deadbeef", "Q1-Phase1", ZeroHash);
        ManifestStamp d = ManifestStampCodec.Decode(ManifestStampCodec.Encode(s));
        Assert.Equal(s, d);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_unicode()
    {
        ManifestStamp s = new("sha-éπ-1234", "build-世界-007", ZeroHash);
        ManifestStamp d = ManifestStampCodec.Decode(ManifestStampCodec.Encode(s));
        Assert.Equal(s, d);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_empty_strings()
    {
        ManifestStamp s = new("", "", ZeroHash);
        ManifestStamp d = ManifestStampCodec.Decode(ManifestStampCodec.Encode(s));
        Assert.Equal(s, d);
    }

    [Fact]
    public void Encode_uses_documented_byte_layout()
    {
        ManifestStamp s = new("AB", "CDE", ZeroHash);
        byte[] enc = ManifestStampCodec.Encode(s);

        // Expected: u8 git_sha_len=2, "AB", u16 build_id_len=3, "CDE", 32 zero bytes.
        Assert.Equal(2, enc[0]);
        Assert.Equal((byte)'A', enc[1]);
        Assert.Equal((byte)'B', enc[2]);
        Assert.Equal((byte)3, enc[3]);    // u16 LSB
        Assert.Equal((byte)0, enc[4]);    // u16 MSB
        Assert.Equal((byte)'C', enc[5]);
        Assert.Equal((byte)'D', enc[6]);
        Assert.Equal((byte)'E', enc[7]);
        for (int i = 8; i < 8 + 32; i++) Assert.Equal((byte)0, enc[i]);
        Assert.Equal(8 + 32, enc.Length);
    }

    [Fact]
    public void Decode_rejects_trailing_garbage()
    {
        ManifestStamp s = new("x", "y", ZeroHash);
        byte[] enc = ManifestStampCodec.Encode(s);
        byte[] withGarbage = new byte[enc.Length + 1];
        Array.Copy(enc, withGarbage, enc.Length);
        withGarbage[^1] = 0x42;
        Assert.Throws<ReplayException>(() => ManifestStampCodec.Decode(withGarbage));
    }

    [Fact]
    public void Decode_rejects_short_buffer()
    {
        Assert.Throws<ReplayException>(() => ManifestStampCodec.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Rejects_oversize_git_sha()
    {
        string huge = new string('x', 256);
        Assert.Throws<ArgumentException>(() =>
            ManifestStampCodec.Encode(new ManifestStamp(huge, "", ZeroHash)));
    }

    [Fact]
    public void Rejects_oversize_content_hash()
    {
        byte[] wrong = new byte[31];
        Assert.Throws<ArgumentException>(() =>
            ManifestStampCodec.Encode(new ManifestStamp("", "", wrong)));
    }
}
