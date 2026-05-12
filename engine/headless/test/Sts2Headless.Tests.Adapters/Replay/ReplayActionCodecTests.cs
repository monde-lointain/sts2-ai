using System.Buffers.Binary;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// Verify the wire shape of every supported <see cref="PlayerAction"/>
/// variant and that the encoder/decoder pair roundtrips.
/// </summary>
public class ReplayActionCodecTests
{
    [Fact]
    public void EndTurn_roundtrips_with_empty_payload()
    {
        var (type, data) = ReplayActionCodec.Encode(PlayerAction.EndTurn.Instance);
        Assert.Equal(ReplayActionType.EndTurn, type);
        Assert.Empty(data);
        Assert.Same(PlayerAction.EndTurn.Instance, ReplayActionCodec.Decode(type, data));
    }

    [Fact]
    public void PlayCard_with_target_uses_9_byte_payload()
    {
        var pc = new PlayerAction.PlayCard(0x01020304u, 0x05060708u);
        var (type, data) = ReplayActionCodec.Encode(pc);
        Assert.Equal(ReplayActionType.PlayCard, type);
        Assert.Equal(9, data.Length);
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)));
        Assert.Equal((byte)1, data[4]);
        Assert.Equal(0x05060708u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5, 4)));
        Assert.Equal(pc, ReplayActionCodec.Decode(type, data));
    }

    [Fact]
    public void PlayCard_without_target_uses_5_byte_payload()
    {
        var pc = new PlayerAction.PlayCard(42u, null);
        var (type, data) = ReplayActionCodec.Encode(pc);
        Assert.Equal(ReplayActionType.PlayCard, type);
        Assert.Equal(5, data.Length);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)));
        Assert.Equal((byte)0, data[4]);
        Assert.Equal(pc, ReplayActionCodec.Decode(type, data));
    }

    [Fact]
    public void Decode_rejects_short_PlayCard_payload()
    {
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.PlayCard, new byte[3]));
    }

    [Fact]
    public void Decode_rejects_PlayCard_NoTarget_with_excess_bytes()
    {
        byte[] tooLongNoTarget = new byte[9];
        tooLongNoTarget[4] = 0;
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.PlayCard, tooLongNoTarget));
    }

    [Fact]
    public void Decode_rejects_PlayCard_HasTarget_with_short_payload()
    {
        byte[] hasTargetButShort = new byte[5];
        hasTargetButShort[4] = 1;
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.PlayCard, hasTargetButShort));
    }

    [Fact]
    public void Decode_rejects_bad_HasTarget_flag()
    {
        byte[] badFlag = new byte[5];
        badFlag[4] = 0xFF;
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.PlayCard, badFlag));
    }

    [Fact]
    public void Decode_rejects_EndTurn_with_payload()
    {
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.EndTurn, new byte[] { 0x00 }));
    }

    [Fact]
    public void Decode_rejects_EnemyMove_as_PlayerAction()
    {
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode(ReplayActionType.EnemyMove, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Decode_rejects_unknown_action_type()
    {
        Assert.Throws<ReplayException>(() => ReplayActionCodec.Decode((ReplayActionType)0xEE, ReadOnlySpan<byte>.Empty));
    }
}
