// Tests for EncounterModel.GenerateMonsters(Rng) — B.1-ε scaffold (Wave 3).
//
// Three invariants:
//   1. Default impl returns the static spawn list for existing encounters.
//   2. Default impl does NOT consume (advance) the Rng counter.
//   3. RunRngSet.ForEncounter seed derivation is stable across calls.

using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

public class GenerateMonsters_DefaultImpl_ReturnsStaticSpawnList
{
    [Fact]
    public void CultistsNormal_returns_same_as_MonsterIds()
    {
        var enc = new CultistsNormal();
        var rng = new Rng(seed: 1u);
        IReadOnlyList<string> result = enc.GenerateMonsters(rng);
        Assert.Equal(enc.MonsterIds, result);
    }

    [Fact]
    public void ChompersNormal_returns_same_as_MonsterIds()
    {
        var enc = new ChompersNormal();
        var rng = new Rng(seed: 2u);
        IReadOnlyList<string> result = enc.GenerateMonsters(rng);
        Assert.Equal(enc.MonsterIds, result);
    }

    [Fact]
    public void BowlbugsTrio_returns_same_as_MonsterIds()
    {
        var enc = new BowlbugsTrio();
        var rng = new Rng(seed: 3u);
        IReadOnlyList<string> result = enc.GenerateMonsters(rng);
        Assert.Equal(enc.MonsterIds, result);
    }
}

public class GenerateMonsters_DefaultImpl_DoesNotConsumeRng
{
    [Fact]
    public void CultistsNormal_counter_unchanged_after_call()
    {
        var enc = new CultistsNormal();
        var rng = new Rng(seed: 42u);
        // Advance to a non-zero counter to make regression visible.
        _ = rng.NextInt(100);
        int counterBefore = rng.Counter;
        uint seedBefore = rng.Seed;

        _ = enc.GenerateMonsters(rng);

        Assert.Equal(counterBefore, rng.Counter);
        Assert.Equal(seedBefore, rng.Seed);
    }

    [Fact]
    public void BowlbugsTrio_counter_unchanged_after_call()
    {
        var enc = new BowlbugsTrio();
        var rng = new Rng(seed: 99u);
        int counterBefore = rng.Counter;

        _ = enc.GenerateMonsters(rng);

        Assert.Equal(counterBefore, rng.Counter);
    }

    [Fact]
    public void FreshRng_counter_is_zero_after_call()
    {
        var enc = new ChompersNormal();
        var rng = new Rng(seed: 0u);
        _ = enc.GenerateMonsters(rng);
        Assert.Equal(0, rng.Counter);
    }
}

public class EncounterSpawnRng_SeedDerivation_IsStable
{
    [Fact]
    public void ForEncounter_same_inputs_yields_same_first_output()
    {
        var set = new RunRngSet(stringSeed: "stable-seed-test");
        Rng rng1 = set.ForEncounter(totalFloor: 3, encounterId: CultistsNormal.CanonicalId);
        Rng rng2 = set.ForEncounter(totalFloor: 3, encounterId: CultistsNormal.CanonicalId);

        Assert.Equal(rng1.Seed, rng2.Seed);
        Assert.Equal(rng1.NextInt(int.MaxValue), rng2.NextInt(int.MaxValue));
    }

    [Fact]
    public void ForEncounter_different_floor_yields_different_seed()
    {
        var set = new RunRngSet(stringSeed: "floor-diverge");
        Rng rng3 = set.ForEncounter(totalFloor: 3, encounterId: CultistsNormal.CanonicalId);
        Rng rng7 = set.ForEncounter(totalFloor: 7, encounterId: CultistsNormal.CanonicalId);
        Assert.NotEqual(rng3.Seed, rng7.Seed);
    }

    [Fact]
    public void ForEncounter_different_encounter_yields_different_seed()
    {
        var set = new RunRngSet(stringSeed: "enc-diverge");
        Rng rngC = set.ForEncounter(totalFloor: 1, encounterId: CultistsNormal.CanonicalId);
        Rng rngB = set.ForEncounter(totalFloor: 1, encounterId: ChompersNormal.CanonicalId);
        Assert.NotEqual(rngC.Seed, rngB.Seed);
    }

    [Fact]
    public void ForEncounter_does_not_advance_any_subsystem_rng()
    {
        var set = new RunRngSet(stringSeed: "no-advance");
        int shuffleBefore = set.Shuffle.Counter;
        int upFrontBefore = set.UpFront.Counter;
        _ = set.ForEncounter(totalFloor: 5, encounterId: CultistsNormal.CanonicalId);
        Assert.Equal(shuffleBefore, set.Shuffle.Counter);
        Assert.Equal(upFrontBefore, set.UpFront.Counter);
    }
}
