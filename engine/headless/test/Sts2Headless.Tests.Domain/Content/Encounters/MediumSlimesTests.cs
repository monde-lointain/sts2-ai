using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

/// <summary>
/// Byte-exact behavior checks for <see cref="MediumSlimes"/> — Wave 14 / B.1-ε.
/// Verifies the upstream byte-exact RNG-driven monster selection matching
/// upstream <c>SlimesNormal.GenerateMonsters()</c>.
/// </summary>
public class MediumSlimesTests
{
    [Fact]
    public void MediumSlimes_canonical_id_and_rng_key()
    {
        MediumSlimes enc = new();
        Assert.Equal("MediumSlimes", enc.Id);
        Assert.Equal("SLIMES_NORMAL", enc.EncounterRngKey);
        Assert.Equal("SLIMES_NORMAL", MediumSlimes.UpstreamRngKey);
    }

    [Fact]
    public void MediumSlimes_MonsterIds_has_four_entries()
    {
        MediumSlimes enc = new();
        Assert.Equal(4, enc.MonsterIds.Count);
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_returns_four_entries()
    {
        MediumSlimes enc = new();
        Rng rng = new(seed: 1u);
        IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
        Assert.Equal(4, monsters.Count);
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_first_two_are_always_TwigSlimeM_LeafSlimeM()
    {
        MediumSlimes enc = new();
        for (uint seed = 0; seed < 20; seed++)
        {
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            Assert.Equal(TwigSlimeM.CanonicalId, monsters[0]);
            Assert.Equal(LeafSlimeM.CanonicalId, monsters[1]);
        }
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_last_two_are_small_slimes_of_opposite_types()
    {
        // flag=true  → [LeafSlimeS, TwigSlimeS]
        // flag=false → [TwigSlimeS, LeafSlimeS]
        // Either way the two small slots contain one of each.
        MediumSlimes enc = new();
        var smallPair = new HashSet<string> { LeafSlimeS.CanonicalId, TwigSlimeS.CanonicalId };
        for (uint seed = 0; seed < 20; seed++)
        {
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            Assert.Contains(monsters[2], smallPair);
            Assert.Contains(monsters[3], smallPair);
            Assert.NotEqual(monsters[2], monsters[3]);
        }
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_consumes_exactly_one_rng_tick()
    {
        MediumSlimes enc = new();
        Rng rng = new(seed: 77u);
        int before = rng.Counter;
        _ = enc.GenerateMonsters(rng);
        Assert.Equal(before + 1, rng.Counter);
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_is_deterministic_for_same_seed()
    {
        MediumSlimes enc = new();
        Rng rng1 = new(seed: 42u);
        Rng rng2 = new(seed: 42u);
        IReadOnlyList<string> r1 = enc.GenerateMonsters(rng1);
        IReadOnlyList<string> r2 = enc.GenerateMonsters(rng2);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void MediumSlimes_EncounterRngKey_differs_from_Id()
    {
        // Upstream uses slugified type name "SLIMES_NORMAL", not Q1's "MediumSlimes".
        MediumSlimes enc = new();
        Assert.NotEqual(enc.Id, enc.EncounterRngKey);
    }

    [Fact]
    public void MediumSlimes_GenerateMonsters_flag_true_puts_LeafSlimeS_first()
    {
        // Find a seed where NextBool returns true and verify LeafSlimeS is small1.
        MediumSlimes enc = new();
        bool foundTrue = false;
        for (uint seed = 0; seed < 100 && !foundTrue; seed++)
        {
            Rng probe = new(seed);
            bool flag = probe.NextBool();
            if (!flag)
                continue;
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            Assert.Equal(LeafSlimeS.CanonicalId, monsters[2]);
            Assert.Equal(TwigSlimeS.CanonicalId, monsters[3]);
            foundTrue = true;
        }
        Assert.True(foundTrue, "No seed produced NextBool()=true in 100 tries.");
    }
}
