using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class CompositionRootTests
{
    private static CliArgs MinimalArgs() =>
        CliArgs.Parse(
            new[]
            {
                "--seed",
                "42",
                "--character",
                "silent",
                "--deck",
                "starter",
                "--relics",
                "ring_of_the_snake",
                "--encounter",
                "cultists_normal",
                "--ascension",
                "0",
            }
        );

    [Fact]
    public void Build_returns_wired_bundle_with_populated_catalogs()
    {
        var bundle = CompositionRoot.Build(MinimalArgs());
        Assert.NotNull(bundle.Context);
        Assert.NotNull(bundle.Clock);
        Assert.NotNull(bundle.Rng);

        // Catalogs contain the smoke ids.
        Assert.True(bundle.Cards.Contains(StrikeSilent.CanonicalId));
        Assert.True(bundle.Cards.Contains(DefendSilent.CanonicalId));
        Assert.True(bundle.Relics.Contains(RingOfTheSnake.CanonicalId));
        Assert.True(bundle.Monsters.Contains(CalcifiedCultist.CanonicalId));
        Assert.True(bundle.Encounters.Contains(CultistsNormal.CanonicalId));
    }

    [Fact]
    public void Build_bootstraps_combat_into_PlayerActing()
    {
        var bundle = CompositionRoot.Build(MinimalArgs());
        Assert.Equal(CombatPhase.PlayerActing, bundle.Context.State.Phase);
        Assert.Equal(1, bundle.Context.State.TurnCounter);
        // Two cultists in the smoke encounter.
        Assert.Equal(2, bundle.Context.State.Enemies.Count);
        // Hand drawn: 5 base + 2 from RingOfTheSnake.
        Assert.Equal(7, bundle.Context.State.HandPile.Cards.Count);
    }

    /// <summary>
    /// RC-1 pin (B.1-beta-T2): the Silent starter deck must be 12 cards
    /// (5 StrikeSilent + 5 DefendSilent + Neutralize + Survivor) — matching
    /// upstream <c>~/development/projects/godot/sts2/src/Core/Models/Characters/Silent.cs</c>'s
    /// <c>StartingDeck</c> byte-for-byte. Q1 previously added DeadlyPoison +
    /// Backflip (14-card invention); the upstream-comparison probe flagged
    /// this as a universal divergence affecting 120/120 entries.
    /// </summary>
    [Fact]
    public void Build_resolves_starter_deck_to_12_cards()
    {
        var bundle = CompositionRoot.Build(MinimalArgs());
        // After Build: deck is shuffled into DrawPile, then HandDrawSize cards
        // are drawn into HandPile. DrawPile + HandPile == initial deck.
        int totalDeck =
            bundle.Context.State.DrawPile.Cards.Count + bundle.Context.State.HandPile.Cards.Count;
        Assert.Equal(12, totalDeck);

        // Composition: 5 Strike + 5 Defend + 1 Neutralize + 1 Survivor.
        var allCards = bundle
            .Context.State.DrawPile.Cards.Concat(bundle.Context.State.HandPile.Cards)
            .GroupBy(c => c.ModelId)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(5, allCards[StrikeSilent.CanonicalId]);
        Assert.Equal(5, allCards[DefendSilent.CanonicalId]);
        Assert.Equal(1, allCards[Neutralize.CanonicalId]);
        Assert.Equal(1, allCards[Survivor.CanonicalId]);
        // No DeadlyPoison, no Backflip — the RC-1 invented additions.
        Assert.False(allCards.ContainsKey(DeadlyPoison.CanonicalId));
        Assert.False(allCards.ContainsKey(Backflip.CanonicalId));
    }

    [Fact]
    public void Build_with_unsupported_character_throws()
    {
        var args = MinimalArgs() with { Character = "ironclad" };
        var ex = Assert.Throws<CompositionException>(() => CompositionRoot.Build(args));
        Assert.Contains("ironclad", ex.Message);
    }

    [Fact]
    public void Build_with_unsupported_deck_throws()
    {
        var args = MinimalArgs() with { Deck = "shiv" };
        Assert.Throws<CompositionException>(() => CompositionRoot.Build(args));
    }

    [Fact]
    public void Build_with_unsupported_encounter_throws()
    {
        var args = MinimalArgs() with { Encounter = "boss" };
        Assert.Throws<CompositionException>(() => CompositionRoot.Build(args));
    }

    [Fact]
    public void Build_with_unsupported_relic_throws()
    {
        var args = MinimalArgs() with { Relics = new[] { "bogus_relic" } };
        Assert.Throws<CompositionException>(() => CompositionRoot.Build(args));
    }

    [Fact]
    public void Build_with_nonzero_ascension_throws()
    {
        var args = MinimalArgs() with { Ascension = 5 };
        Assert.Throws<CompositionException>(() => CompositionRoot.Build(args));
    }

    [Fact]
    public void Build_is_deterministic_for_same_seed()
    {
        var bundle1 = CompositionRoot.Build(MinimalArgs());
        var bundle2 = CompositionRoot.Build(MinimalArgs());
        // Same seed → same shuffle → same draw pile order.
        Assert.Equal(
            bundle1.Context.State.DrawPile.Cards.Select(c => c.InstanceId),
            bundle2.Context.State.DrawPile.Cards.Select(c => c.InstanceId)
        );
        Assert.Equal(
            bundle1.Context.State.HandPile.Cards.Select(c => c.InstanceId),
            bundle2.Context.State.HandPile.Cards.Select(c => c.InstanceId)
        );
    }

    /// <summary>
    /// B.1-alpha-T1 (RC-2): the kernel RNG must be seeded by hashing the
    /// upstream-shaped string <c>"seed-{N}"</c> rather than passing the raw
    /// uint from <c>--seed N</c>. The pinned constant is the byte-exact
    /// <see cref="StringHelpers.GetDeterministicHashCode(string)"/> of
    /// <c>"seed-42"</c> — computed once and pinned forever. Drift in this
    /// value would mean the kernel diverges from upstream's seed plumbing.
    /// </summary>
    [Fact]
    public void Build_derives_master_seed_via_string_hash_of_seed_dash_N()
    {
        // The hash of "seed-42" is fully determined by the byte-exact M5
        // GetDeterministicHashCode port. Pinning the value here catches both
        // (a) a regression in CompositionRoot.Build's seed derivation and
        // (b) any future change to StringHelpers that breaks the port.
        const uint ExpectedSeed42 = 3903233884u; // = (uint)(int -391733412) = 0xE8A69F5C
        Assert.Equal(ExpectedSeed42, (uint)StringHelpers.GetDeterministicHashCode("seed-42"));

        var bundle = CompositionRoot.Build(MinimalArgs());
        // The kernel RNG must report Seed == hash("seed-42"), not the raw 42u.
        Assert.Equal(ExpectedSeed42, bundle.RunRng.Seed);
        Assert.Equal("seed-42", bundle.RunRng.StringSeed);
    }

    [Fact]
    public void Token_resolution_is_case_insensitive()
    {
        var args = MinimalArgs() with
        {
            Character = "SILENT",
            Deck = "Starter",
            Encounter = "CULTISTS_NORMAL",
            Relics = new[] { "Ring_Of_The_Snake" },
        };
        var bundle = CompositionRoot.Build(args);
        Assert.NotNull(bundle.Context);
    }
}
