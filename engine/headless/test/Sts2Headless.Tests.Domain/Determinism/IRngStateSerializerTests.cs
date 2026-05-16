// Tests for the M5 RNG state codec. Per Q1-ADR-003, M5 owns the RNG state
// schema and exposes a generic byte-blob codec; M1 treats the result as
// opaque bytes.
//
// Bit-identical roundtrip is a HARD requirement (risk R2 in the S1 prompt):
//   Serialize(Deserialize(Serialize(x))) == Serialize(x)   (byte-for-byte)
//   and the deserialized RNG must produce a stream byte-equal to the
//   original's continued stream.

using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class IRngStateSerializerTests
{
    private static IRngStateSerializer NewSerializer() => new RngStateSerializerV1();

    // === Rng ===

    [Fact]
    public void Rng_Roundtrip_RestoredStreamMatchesOriginalContinued()
    {
        var ser = NewSerializer();
        var rng = new Rng(seed: 12345u);
        for (int i = 0; i < 17; i++)
            _ = rng.NextInt(1000);

        byte[] state = ser.SerializeRng(rng);
        Rng restored = ser.DeserializeRng(state);

        Assert.Equal(rng.Seed, restored.Seed);
        Assert.Equal(rng.Counter, restored.Counter);

        // Continued stream is byte-equal.
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rng.NextInt(1_000_000), restored.NextInt(1_000_000));
        }
    }

    [Fact]
    public void Rng_BitIdenticalRoundtrip()
    {
        var ser = NewSerializer();
        var rng = new Rng(seed: 999u);
        for (int i = 0; i < 25; i++)
            _ = rng.NextBool();

        byte[] a = ser.SerializeRng(rng);
        Rng restored = ser.DeserializeRng(a);
        byte[] b = ser.SerializeRng(restored);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Rng_FreshRng_HasMinimalState()
    {
        var ser = NewSerializer();
        var rng = new Rng(seed: 0u);
        byte[] state = ser.SerializeRng(rng);
        Rng restored = ser.DeserializeRng(state);
        Assert.Equal(0u, restored.Seed);
        Assert.Equal(0, restored.Counter);
    }

    // === PlayerRngSet ===

    [Fact]
    public void PlayerRngSet_Roundtrip_RestoredStreamMatchesOriginalContinued()
    {
        var ser = NewSerializer();
        var set = new PlayerRngSet(seed: 7u);
        // Advance each subsystem a different number of times.
        for (int i = 0; i < 3; i++)
            _ = set.Rewards.NextInt(1000);
        for (int i = 0; i < 11; i++)
            _ = set.Shops.NextInt(1000);
        for (int i = 0; i < 19; i++)
            _ = set.Transformations.NextInt(1000);

        byte[] state = ser.SerializePlayerRngSet(set);
        PlayerRngSet restored = ser.DeserializePlayerRngSet(state);

        Assert.Equal(set.Seed, restored.Seed);
        foreach (PlayerRngType t in Enum.GetValues<PlayerRngType>())
        {
            Assert.Equal(set.GetCounter(t), restored.GetCounter(t));
        }

        for (int i = 0; i < 30; i++)
        {
            Assert.Equal(set.Rewards.NextInt(1_000_000), restored.Rewards.NextInt(1_000_000));
            Assert.Equal(set.Shops.NextInt(1_000_000), restored.Shops.NextInt(1_000_000));
            Assert.Equal(
                set.Transformations.NextInt(1_000_000),
                restored.Transformations.NextInt(1_000_000)
            );
        }
    }

    [Fact]
    public void PlayerRngSet_BitIdenticalRoundtrip()
    {
        var ser = NewSerializer();
        var set = new PlayerRngSet(seed: 31337u);
        for (int i = 0; i < 5; i++)
            _ = set.Shops.NextDouble();

        byte[] a = ser.SerializePlayerRngSet(set);
        var restored = ser.DeserializePlayerRngSet(a);
        byte[] b = ser.SerializePlayerRngSet(restored);
        Assert.Equal(a, b);
    }

    // === RunRngSet ===

    [Fact]
    public void RunRngSet_Roundtrip_RestoredStreamMatchesOriginalContinued()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "ABCDEF");
        for (int i = 0; i < 4; i++)
            _ = set.UpFront.NextInt(1000);
        for (int i = 0; i < 13; i++)
            _ = set.Shuffle.NextInt(1000);
        for (int i = 0; i < 7; i++)
            _ = set.MonsterAi.NextInt(1000);
        for (int i = 0; i < 21; i++)
            _ = set.CombatCardGeneration.NextInt(1000);

        byte[] state = ser.SerializeRunRngSet(set);
        RunRngSet restored = ser.DeserializeRunRngSet(state);

        Assert.Equal(set.StringSeed, restored.StringSeed);
        Assert.Equal(set.Seed, restored.Seed);
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
        {
            Assert.Equal(set.GetCounter(t), restored.GetCounter(t));
        }

        for (int i = 0; i < 20; i++)
        {
            foreach (RunRngType t in Enum.GetValues<RunRngType>())
            {
                Assert.Equal(set[t].NextInt(1_000_000), restored[t].NextInt(1_000_000));
            }
        }
    }

    [Fact]
    public void RunRngSet_BitIdenticalRoundtrip()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "round-trip-me");
        for (int i = 0; i < 9; i++)
            _ = set.TreasureRoomRelics.NextFloat();

        byte[] a = ser.SerializeRunRngSet(set);
        var restored = ser.DeserializeRunRngSet(a);
        byte[] b = ser.SerializeRunRngSet(restored);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RunRngSet_EmptyStringSeedRoundtrips()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: string.Empty);
        byte[] state = ser.SerializeRunRngSet(set);
        var restored = ser.DeserializeRunRngSet(state);
        Assert.Equal(string.Empty, restored.StringSeed);
        Assert.Equal(set.Seed, restored.Seed);
    }

    [Fact]
    public void RunRngSet_UnicodeStringSeedRoundtrips()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "spire-Ω-2");
        byte[] state = ser.SerializeRunRngSet(set);
        var restored = ser.DeserializeRunRngSet(state);
        Assert.Equal("spire-Ω-2", restored.StringSeed);
        Assert.Equal(set.Seed, restored.Seed);
    }

    // === Schema version + tamper detection ===

    [Fact]
    public void Rng_DeserializeRejectsTruncatedInput()
    {
        var ser = NewSerializer();
        var rng = new Rng(seed: 1u);
        byte[] state = ser.SerializeRng(rng);
        byte[] truncated = state.AsSpan(0, state.Length - 1).ToArray();
        Assert.ThrowsAny<Exception>(() => ser.DeserializeRng(truncated));
    }

    [Fact]
    public void Rng_DeserializeRejectsWrongMagic()
    {
        var ser = NewSerializer();
        var rng = new Rng(seed: 1u);
        byte[] state = ser.SerializeRng(rng);
        state[0] ^= 0xFF;
        Assert.ThrowsAny<Exception>(() => ser.DeserializeRng(state));
    }
}
