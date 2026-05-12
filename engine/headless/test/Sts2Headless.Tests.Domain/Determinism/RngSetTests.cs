// Tests for the run-scope and player-scope RNG fan-out sets.
//
// Each set instantiates one independent Rng per enumerated subsystem, seeded
// deterministically from the set's master seed + the snake_case subsystem
// name. The seed derivation is the upstream contract — verified byte-for-byte
// against the upstream-emitted golden corpus in RngDifferentialParityTests
// (which exercises the name-seeded Rng ctor).
//
// These tests cover the set-level invariants:
//   - Every subsystem listed in the enum has an Rng instance.
//   - Per-subsystem RNGs are independent: pulling from one does not advance
//     any other.
//   - Two sets with the same master seed produce byte-identical sequences for
//     the same subsystem.
//   - RunRngSet derives its uint seed from the string seed via
//     GetDeterministicHashCode (so the same string seed reproduces).

using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class RngSetTests
{
    [Fact]
    public void PlayerRngSet_HasAnInstanceForEverySubsystem()
    {
        var set = new PlayerRngSet(seed: 42u);
        foreach (PlayerRngType t in Enum.GetValues<PlayerRngType>())
        {
            Rng rng = set[t];
            Assert.NotNull(rng);
        }
    }

    [Fact]
    public void PlayerRngSet_PublicAccessorsMatchEnumLookup()
    {
        var set = new PlayerRngSet(seed: 7u);
        Assert.Same(set[PlayerRngType.Rewards], set.Rewards);
        Assert.Same(set[PlayerRngType.Shops], set.Shops);
        Assert.Same(set[PlayerRngType.Transformations], set.Transformations);
    }

    [Fact]
    public void PlayerRngSet_PerSubsystemRngIsIndependent()
    {
        var set = new PlayerRngSet(seed: 100u);
        int rewardsCounterBefore = set.Rewards.Counter;
        int shopsCounterBefore = set.Shops.Counter;
        _ = set.Rewards.NextInt(1000);
        Assert.Equal(rewardsCounterBefore + 1, set.Rewards.Counter);
        Assert.Equal(shopsCounterBefore, set.Shops.Counter);
    }

    [Fact]
    public void PlayerRngSet_SameSeedReproducesSameStream()
    {
        var a = new PlayerRngSet(seed: 12345u);
        var b = new PlayerRngSet(seed: 12345u);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(a.Rewards.NextInt(1_000_000), b.Rewards.NextInt(1_000_000));
            Assert.Equal(a.Shops.NextInt(1_000_000), b.Shops.NextInt(1_000_000));
            Assert.Equal(a.Transformations.NextInt(1_000_000), b.Transformations.NextInt(1_000_000));
        }
    }

    [Fact]
    public void PlayerRngSet_DifferentSubsystemsHaveDifferentSeeds()
    {
        // Per upstream: seed = masterSeed + GetDeterministicHashCode(snake_case(name)).
        // Different names -> different derived seeds (with overwhelming likelihood).
        var set = new PlayerRngSet(seed: 0u);
        Assert.NotEqual(set.Rewards.Seed, set.Shops.Seed);
        Assert.NotEqual(set.Shops.Seed, set.Transformations.Seed);
    }

    [Fact]
    public void RunRngSet_HasAnInstanceForEverySubsystem()
    {
        var set = new RunRngSet(stringSeed: "test-seed");
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
        {
            Rng rng = set[t];
            Assert.NotNull(rng);
        }
    }

    [Fact]
    public void RunRngSet_DerivesUintSeedFromStringSeed()
    {
        // The contract: uint Seed == (uint)GetDeterministicHashCode(stringSeed).
        // We assert via the public-property-exposed Seed and verify behavioral
        // consequence rather than re-implementing the hash here (that's
        // covered by the differential parity gate's name-seeded section).
        var s1 = new RunRngSet(stringSeed: "abc");
        var s2 = new RunRngSet(stringSeed: "abc");
        Assert.Equal(s1.Seed, s2.Seed);
        Assert.Equal("abc", s1.StringSeed);
    }

    [Fact]
    public void RunRngSet_PublicAccessorsMatchEnumLookup()
    {
        var set = new RunRngSet(stringSeed: "x");
        Assert.Same(set[RunRngType.UpFront], set.UpFront);
        Assert.Same(set[RunRngType.Shuffle], set.Shuffle);
        Assert.Same(set[RunRngType.UnknownMapPoint], set.UnknownMapPoint);
        Assert.Same(set[RunRngType.CombatCardGeneration], set.CombatCardGeneration);
        Assert.Same(set[RunRngType.CombatPotionGeneration], set.CombatPotionGeneration);
        Assert.Same(set[RunRngType.CombatCardSelection], set.CombatCardSelection);
        Assert.Same(set[RunRngType.CombatEnergyCosts], set.CombatEnergyCosts);
        Assert.Same(set[RunRngType.CombatTargets], set.CombatTargets);
        Assert.Same(set[RunRngType.MonsterAi], set.MonsterAi);
        Assert.Same(set[RunRngType.Niche], set.Niche);
        Assert.Same(set[RunRngType.CombatOrbs], set.CombatOrbGeneration);
        Assert.Same(set[RunRngType.TreasureRoomRelics], set.TreasureRoomRelics);
    }

    [Fact]
    public void RunRngSet_PerSubsystemRngIsIndependent()
    {
        var set = new RunRngSet(stringSeed: "ind");
        int upBefore = set.UpFront.Counter;
        int shuffleBefore = set.Shuffle.Counter;
        _ = set.UpFront.NextInt(1000);
        Assert.Equal(upBefore + 1, set.UpFront.Counter);
        Assert.Equal(shuffleBefore, set.Shuffle.Counter);
    }

    [Fact]
    public void RunRngSet_SameStringSeedReproducesSameStream()
    {
        var a = new RunRngSet(stringSeed: "deterministic");
        var b = new RunRngSet(stringSeed: "deterministic");
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
        {
            for (int i = 0; i < 30; i++)
            {
                Assert.Equal(a[t].NextInt(1_000_000), b[t].NextInt(1_000_000));
            }
        }
    }
}
