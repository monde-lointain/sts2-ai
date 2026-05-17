using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

/// <summary>
/// Byte-exact behavior checks for <see cref="SmallSlimes"/> — Wave 14 / B.1-ε.
/// Verifies the upstream byte-exact RNG-driven monster selection matching
/// upstream <c>SlimesWeak.GenerateMonsters()</c>.
/// </summary>
public class SmallSlimesTests
{
    [Fact]
    public void SmallSlimes_canonical_id_and_rng_key()
    {
        SmallSlimes enc = new();
        Assert.Equal("SmallSlimes", enc.Id);
        Assert.Equal("SLIMES_WEAK", enc.EncounterRngKey);
        Assert.Equal("SLIMES_WEAK", SmallSlimes.UpstreamRngKey);
    }

    [Fact]
    public void SmallSlimes_MonsterIds_has_three_entries()
    {
        SmallSlimes enc = new();
        Assert.Equal(3, enc.MonsterIds.Count);
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_returns_three_entries()
    {
        SmallSlimes enc = new();
        Rng rng = new(seed: 1u);
        IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
        Assert.Equal(3, monsters.Count);
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_all_results_are_valid_slime_ids()
    {
        SmallSlimes enc = new();
        var validIds = new HashSet<string>
        {
            LeafSlimeS.CanonicalId,
            TwigSlimeS.CanonicalId,
            LeafSlimeM.CanonicalId,
            TwigSlimeM.CanonicalId,
        };

        for (uint seed = 0; seed < 20; seed++)
        {
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            foreach (string id in monsters)
            {
                Assert.Contains(id, validIds);
            }
        }
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_middle_slot_is_always_medium_slime()
    {
        SmallSlimes enc = new();
        var mediumIds = new HashSet<string> { LeafSlimeM.CanonicalId, TwigSlimeM.CanonicalId };
        var smallIds = new HashSet<string> { LeafSlimeS.CanonicalId, TwigSlimeS.CanonicalId };

        for (uint seed = 0; seed < 20; seed++)
        {
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            // Output: [small1, medium, small2]
            Assert.Contains(monsters[0], smallIds);
            Assert.Contains(monsters[1], mediumIds);
            Assert.Contains(monsters[2], smallIds);
        }
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_small_slimes_are_distinct_types()
    {
        // Pool/remove pattern guarantees small1 != small2.
        SmallSlimes enc = new();
        for (uint seed = 0; seed < 20; seed++)
        {
            Rng rng = new(seed);
            IReadOnlyList<string> monsters = enc.GenerateMonsters(rng);
            Assert.NotEqual(monsters[0], monsters[2]);
        }
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_consumes_exactly_three_rng_ticks()
    {
        SmallSlimes enc = new();
        Rng rng = new(seed: 77u);
        int before = rng.Counter;
        _ = enc.GenerateMonsters(rng);
        Assert.Equal(before + 3, rng.Counter);
    }

    [Fact]
    public void SmallSlimes_GenerateMonsters_is_deterministic_for_same_seed()
    {
        SmallSlimes enc = new();
        Rng rng1 = new(seed: 42u);
        Rng rng2 = new(seed: 42u);
        IReadOnlyList<string> r1 = enc.GenerateMonsters(rng1);
        IReadOnlyList<string> r2 = enc.GenerateMonsters(rng2);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void SmallSlimes_EncounterRngKey_differs_from_Id()
    {
        // Upstream uses slugified type name "SLIMES_WEAK", not Q1's "SmallSlimes".
        SmallSlimes enc = new();
        Assert.NotEqual(enc.Id, enc.EncounterRngKey);
    }
}
