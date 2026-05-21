using System.Buffers.Binary;
using System.Security.Cryptography;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Tests for the on-wire layout: magic constants, schema version, section
/// table terminator, trailer hash. These tests cover the bytes-as-bytes
/// contract — the bit-identical-roundtrip gate sits on top.
/// </summary>
public class StateCodecFormatTests
{
    [Fact]
    public void Format_constants_match_spec()
    {
        // The constants are part of the wire contract; pinning them here so a
        // typo-rename doesn't silently bump the schema.
        Assert.Equal(0x53435443u, StateCodecConstants.HeaderMagic); // "STCT" little-endian when read forward
        // Wave-38/B: bumped 3 -> 4 for MonsterIntentPower.Target (i32 per entry)
        // and MonsterIntent.SelfBlockGain (i32 after applies-loop).
        Assert.Equal((ushort)4, StateCodecConstants.SchemaVersion);
        Assert.Equal(0x53544354u, StateCodecConstants.TrailerMagic);
        Assert.Equal((ushort)0xFFFF, StateCodecConstants.SectionTerminator);
    }

    [Fact]
    public void Section_ids_match_spec()
    {
        Assert.Equal((ushort)0, (ushort)SectionId.Rng);
        Assert.Equal((ushort)1, (ushort)SectionId.Tokens);
        Assert.Equal((ushort)2, (ushort)SectionId.CombatState);
        // Sections may add new variants in future stages; the value-pin matters,
        // the variant-count is allowed to grow.
    }

    [Fact]
    public void Trailer_size_is_36_bytes()
    {
        // trailer_magic (u32) + sha256 (32 bytes) = 36
        Assert.Equal(36, StateCodecConstants.TrailerSizeBytes);
    }
}
