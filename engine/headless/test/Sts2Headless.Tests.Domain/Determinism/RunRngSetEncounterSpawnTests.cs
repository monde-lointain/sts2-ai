// Tests for RunRngSet.ForEncounter — B.1-ε scaffold (Wave 3).
//
// ForEncounter returns a fresh Rng (not stored in the subsystem dict). This file
// verifies the round-trip contract: the derived Rng serialises and deserialises
// losslessly via IRngStateSerializer.SerializeRng / DeserializeRng, and the
// resumed stream is byte-identical to the continued original.
//
// RunRngSet serialisation is NOT affected — no new RunRngType enum value is
// needed because the encounter-spawn Rng is ephemeral (caller owns lifetime).

using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class RunRngSetEncounterSpawnTests
{
    private static IRngStateSerializer NewSerializer() => new RngStateSerializerV1();

    // === Seed derivation formula ===

    [Fact]
    public void ForEncounter_seed_matches_upstream_formula()
    {
        // Upstream formula (EncounterModel.cs:198):
        //   uint seed = (uint)((int)runState.Rng.Seed + totalFloor + GetDeterministicHashCode(id))
        var set = new RunRngSet(stringSeed: "formula-check");
        int totalFloor = 5;
        string id = CultistsNormal.CanonicalId;

        Rng derived = set.ForEncounter(totalFloor, id);

        uint expectedSeed = (uint)(
            (int)set.Seed + totalFloor + StringHelpers.GetDeterministicHashCode(id)
        );
        Assert.Equal(expectedSeed, derived.Seed);
    }

    [Fact]
    public void ForEncounter_fresh_rng_has_counter_zero()
    {
        var set = new RunRngSet(stringSeed: "counter-check");
        Rng rng = set.ForEncounter(totalFloor: 2, encounterId: ChompersNormal.CanonicalId);
        Assert.Equal(0, rng.Counter);
    }

    // === RunRngSet serialisation unaffected ===

    [Fact]
    public void RunRngSet_serialisation_unchanged_after_ForEncounter_call()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "ser-check");
        byte[] before = ser.SerializeRunRngSet(set);

        _ = set.ForEncounter(totalFloor: 1, encounterId: CultistsNormal.CanonicalId);

        byte[] after = ser.SerializeRunRngSet(set);
        Assert.Equal(before, after);
    }

    // === Derived Rng round-trip via SerializeRng / DeserializeRng ===

    [Fact]
    public void DerivedRng_BitIdenticalRoundtrip()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "roundtrip-enc");
        Rng rng = set.ForEncounter(totalFloor: 3, encounterId: CultistsNormal.CanonicalId);

        // Advance a few steps before capturing state.
        for (int i = 0; i < 7; i++)
        {
            _ = rng.NextInt(1000);
        }

        byte[] a = ser.SerializeRng(rng);
        Rng restored = ser.DeserializeRng(a);
        byte[] b = ser.SerializeRng(restored);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DerivedRng_ResumedStream_IsByteEqual()
    {
        var ser = NewSerializer();
        var set = new RunRngSet(stringSeed: "resumed-enc");
        Rng rng = set.ForEncounter(totalFloor: 4, encounterId: BowlbugsTrio.CanonicalId);

        // Advance the rng before capture.
        for (int i = 0; i < 10; i++)
        {
            _ = rng.NextInt(1000);
        }

        byte[] state = ser.SerializeRng(rng);
        Rng restored = ser.DeserializeRng(state);

        Assert.Equal(rng.Seed, restored.Seed);
        Assert.Equal(rng.Counter, restored.Counter);

        // Continued stream must be byte-identical.
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(rng.NextInt(1_000_000), restored.NextInt(1_000_000));
        }
    }

    [Fact]
    public void ForEncounter_null_encounterId_throws()
    {
        var set = new RunRngSet(stringSeed: "null-check");
        Assert.Throws<System.ArgumentNullException>(() =>
            set.ForEncounter(totalFloor: 0, encounterId: null!)
        );
    }
}
