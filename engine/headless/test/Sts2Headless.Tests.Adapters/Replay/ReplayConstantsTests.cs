using System.Buffers.Binary;
using Sts2Headless.Adapters.Replay;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// Pin the on-wire magic / version / sentinel constants. These bytes are part
/// of the schema contract; changes here must coincide with a SchemaVersion
/// bump and a documented migration.
/// </summary>
public class ReplayConstantsTests
{
    [Fact]
    public void HeaderMagic_is_RPLY_little_endian()
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, ReplayConstants.HeaderMagic);
        Assert.Equal((byte)'R', buf[0]);
        Assert.Equal((byte)'P', buf[1]);
        Assert.Equal((byte)'L', buf[2]);
        Assert.Equal((byte)'Y', buf[3]);
    }

    [Fact]
    public void TrailerMagic_is_RPLT_little_endian()
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, ReplayConstants.TrailerMagic);
        Assert.Equal((byte)'R', buf[0]);
        Assert.Equal((byte)'P', buf[1]);
        Assert.Equal((byte)'L', buf[2]);
        Assert.Equal((byte)'T', buf[3]);
    }

    [Fact]
    public void SchemaVersion_is_one()
    {
        Assert.Equal((ushort)1, ReplayConstants.SchemaVersion);
    }

    [Fact]
    public void EntryTerminator_is_uint_max()
    {
        Assert.Equal(0xFFFFFFFFu, ReplayConstants.EntryTerminator);
    }

    [Fact]
    public void TrailerSizeBytes_is_36()
    {
        Assert.Equal(36, ReplayConstants.TrailerSizeBytes);
    }
}
