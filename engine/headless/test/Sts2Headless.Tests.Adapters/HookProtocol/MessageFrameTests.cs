// MessageFrame & MessageHeader unit tests.
//
// What we pin:
//   - HeaderSize is 16; WireAlignment is 64.
//   - LE byte layout matches the spec at every offset.
//   - Roundtrip encode/decode for representative correlation IDs / lengths.
//   - WireFrameSize correctly rounds up to 64.
//   - Reserved-byte non-zero is rejected (corruption signal).
//   - Frame Encode pads with zeros and copies payload verbatim.
//   - Encode/Decode allocate zero bytes on the hot path.

using System;
using Sts2Headless.Adapters.HookProtocol;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

public class MessageFrameTests
{
    [Fact]
    public void Header_size_is_16_and_alignment_is_64()
    {
        Assert.Equal(16, MessageHeader.HeaderSize);
        Assert.Equal(64, MessageHeader.WireAlignment);
    }

    [Fact]
    public void Encode_writes_le_bytes_at_fixed_offsets()
    {
        var hdr = new MessageHeader(
            MessageType.HookRequest,
            0x1234,
            0x10203040,
            0xCAFEBABEDEADBEEFul
        );
        byte[] buf = new byte[MessageHeader.HeaderSize];
        hdr.Encode(buf);

        // type (u8) = 0x03 (HookRequest)
        Assert.Equal(0x03, buf[0]);
        // reserved (u8) = 0
        Assert.Equal(0x00, buf[1]);
        // schema (u16 LE) = 0x1234
        Assert.Equal(0x34, buf[2]);
        Assert.Equal(0x12, buf[3]);
        // length (u32 LE) = 0x10203040
        Assert.Equal(0x40, buf[4]);
        Assert.Equal(0x30, buf[5]);
        Assert.Equal(0x20, buf[6]);
        Assert.Equal(0x10, buf[7]);
        // correlation_id (u64 LE) = 0xCAFEBABEDEADBEEFul
        Assert.Equal(0xEF, buf[8]);
        Assert.Equal(0xBE, buf[9]);
        Assert.Equal(0xAD, buf[10]);
        Assert.Equal(0xDE, buf[11]);
        Assert.Equal(0xBE, buf[12]);
        Assert.Equal(0xBA, buf[13]);
        Assert.Equal(0xFE, buf[14]);
        Assert.Equal(0xCA, buf[15]);
    }

    [Fact]
    public void Decode_recovers_all_fields()
    {
        var original = new MessageHeader(MessageType.ManifestResponse, 0x0001, 1024, 0xABCDul);
        byte[] buf = new byte[MessageHeader.HeaderSize];
        original.Encode(buf);
        var recovered = MessageHeader.Decode(buf);
        Assert.Equal(original, recovered);
    }

    [Theory]
    [InlineData(0, 64)] // header only, 16 bytes, rounds to 64
    [InlineData(48, 64)] // 16+48=64, exact
    [InlineData(49, 128)] // 16+49=65, rounds to 128
    [InlineData(112, 128)] // 16+112=128, exact
    [InlineData(113, 192)] // rounds up
    [InlineData(64 * 100, 64 * 100 + 64)]
    public void WireFrameSize_rounds_up_to_64(int payloadLen, int expectedWire)
    {
        Assert.Equal(expectedWire, MessageHeader.WireFrameSize(payloadLen));
        var hdr = new MessageHeader(MessageType.HookRequest, 1, payloadLen, 0);
        Assert.Equal(expectedWire, hdr.WireSize);
    }

    [Fact]
    public void Decode_rejects_nonzero_reserved_byte()
    {
        byte[] buf = new byte[MessageHeader.HeaderSize];
        buf[0] = (byte)MessageType.HookRequest;
        buf[1] = 0xFF; // corruption!
        Assert.Throws<FormatException>(() => MessageHeader.Decode(buf));
    }

    [Fact]
    public void Encode_negative_payload_length_rejected_at_construction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MessageHeader(MessageType.HookRequest, 1, -1, 0)
        );
    }

    [Fact]
    public void Frame_Encode_copies_payload_and_zero_pads()
    {
        var hdr = new MessageHeader(MessageType.HookResponse, 1, 5, 42);
        byte[] payload = { 1, 2, 3, 4, 5 };
        byte[] dest = new byte[hdr.WireSize];
        // Pre-fill with junk so we can confirm padding is zeroed.
        Array.Fill(dest, (byte)0xAA);
        int written = MessageFrame.Encode(hdr, payload, dest);
        Assert.Equal(hdr.WireSize, written);
        // First 16 bytes = header. Next 5 bytes = payload. Rest = zero padding.
        for (int i = 16; i < 21; i++)
            Assert.Equal(payload[i - 16], dest[i]);
        for (int i = 21; i < written; i++)
            Assert.Equal((byte)0, dest[i]);
    }

    [Fact]
    public void Frame_Encode_rejects_payload_length_mismatch()
    {
        var hdr = new MessageHeader(MessageType.HookRequest, 1, 10, 0);
        byte[] payload = new byte[5]; // claims 10 but only has 5
        byte[] dest = new byte[hdr.WireSize];
        Assert.Throws<ArgumentException>(() => MessageFrame.Encode(hdr, payload, dest));
    }

    [Fact]
    public void Hot_path_encode_decode_does_not_allocate()
    {
        var hdr = new MessageHeader(MessageType.HookRequest, 1, 32, 0xDEADBEEFul);
        byte[] payload = new byte[32];
        byte[] dest = new byte[hdr.WireSize];
        // Warm-up.
        for (int i = 0; i < 4; i++)
        {
            MessageFrame.Encode(hdr, payload, dest);
            _ = MessageHeader.Decode(dest);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
        {
            MessageFrame.Encode(hdr, payload, dest);
            _ = MessageHeader.Decode(dest);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        Assert.True(delta < 4096, $"Allocated {delta} bytes on hot path (expected ~0).");
    }

    [Fact]
    public void WireSize_overflow_is_rejected()
    {
        // A payload of int.MaxValue won't construct a header normally (it would),
        // but WireFrameSize must guard against silent wraparound.
        Assert.Throws<ArgumentOutOfRangeException>(() => MessageHeader.WireFrameSize(int.MaxValue));
    }

    [Fact]
    public void MessageType_values_are_stable()
    {
        // Pin enum codes; they are part of the wire-format contract.
        Assert.Equal(0x00, (byte)MessageType.Reserved);
        Assert.Equal(0x01, (byte)MessageType.ManifestRequest);
        Assert.Equal(0x02, (byte)MessageType.ManifestResponse);
        Assert.Equal(0x03, (byte)MessageType.HookRequest);
        Assert.Equal(0x04, (byte)MessageType.HookResponse);
        Assert.Equal(0x05, (byte)MessageType.Terminate);
        Assert.Equal(0x06, (byte)MessageType.Error);
    }
}
