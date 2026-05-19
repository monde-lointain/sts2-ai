using System.Collections.Immutable;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Codec round-trip gate for the Wave-26/Q1.B AddEnemies substrate.
///
/// <para>
/// Verifies that a post-spawn <see cref="CombatState"/> (produced by
/// <see cref="CombatState.WithSpawnedEnemies"/>) survives the
/// <c>Serialize → Deserialize → re-Serialize</c> cycle byte-identically —
/// the M1 hard contract from Q1-ADR-002. This complements
/// <see cref="BitIdenticalRoundtripTests"/> which covers the pre-canned corpus;
/// these tests specifically exercise states with 0→N, 1→3 enemy counts and
/// confirm the codec's i32-prefixed enemy-count encoding remains stable.
/// </para>
/// </summary>
public sealed class AddEnemiesCodecRoundtripTests
{
    private static readonly string DefaultGitSha = "deadbeefcafebabe1234567890abcdef12345678";
    private static readonly byte[] CanonicalHash = ManifestStamp.ContentHashFromIds(
        new[] { "CalcifiedCultist", "DampCultist", "GremlinMerc", "SneakyGremlin", "FatGremlin" }
    );

    private static ManifestStamp Stamp() => new(DefaultGitSha, "wave-26-Q1B-test", CanonicalHash);

    private static Creature MakePlayer() =>
        new(
            Id: 0u,
            Name: "Silent",
            CurrentHp: 70,
            MaxHp: 70,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: null,
            IsPlayer: true
        );

    private static Creature MakeEnemy(uint id, string name, int hp) =>
        new(
            Id: id,
            Name: name,
            CurrentHp: hp,
            MaxHp: hp,
            Block: 0,
            Powers: ImmutableList<PowerInstance>.Empty,
            Intent: new MonsterIntent(
                MonsterIntentKind.Attack,
                DamagePerHit: 5,
                HitCount: 1,
                AppliesPowers: ImmutableList<MonsterIntentPower>.Empty,
                MoveId: "attack"
            ),
            IsPlayer: false
        );

    private static CombatState BaseState(params Creature[] enemies) =>
        new(
            TurnCounter: 1,
            Phase: CombatPhase.PlayerActing,
            Player: MakePlayer(),
            Enemies: ImmutableList.CreateRange(enemies),
            Energy: 3,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.Empty,
            HandPile: CardPile.Empty,
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 0,
            MonsterRngCounter: 0
        );

    private static RunRngSet Rng() => new("wave-26-Q1B");
    private static PlayerRngSet PlayerRng() => new(7u);
    private static TokenMap Tokens() => new();

    private static void AssertBitIdentical(CombatState state)
    {
        byte[] first = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            Rng(),
            PlayerRng(),
            Tokens(),
            Stamp()
        );

        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(first);
        Assert.True(decoded.TrailerValidated, "trailer must validate");

        CombatState rebuilt = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);
        (RunRngSet rebuiltRun, PlayerRngSet rebuiltPlayer) =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToRngBundle(decoded);
        TokenMap rebuiltTokens =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToTokenMap(decoded);

        byte[] second = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            rebuilt,
            rebuiltRun,
            rebuiltPlayer,
            rebuiltTokens,
            decoded.Stamp
        );

        Assert.True(
            first.AsSpan().SequenceEqual(second),
            $"Bit-identical roundtrip failed: first={first.Length}B second={second.Length}B"
        );
    }

    // =========================================================================
    // Round-trip tests
    // =========================================================================

    [Fact]
    public void State_with_0_initial_enemies_spawning_2_roundtrips_bit_identical()
    {
        var state = BaseState();

        var allocator = new CreatureIdAllocator(state);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        var postSpawn = state.WithSpawnedEnemies(new[] { sneaky, fat });
        Assert.Equal(2, postSpawn.Enemies.Count);

        AssertBitIdentical(postSpawn);
    }

    [Fact]
    public void State_with_1_initial_enemy_spawning_2_yields_3_and_roundtrips()
    {
        var merc = MakeEnemy(1u, "GremlinMerc", 30);
        var state = BaseState(merc);

        var allocator = new CreatureIdAllocator(state);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        var postSpawn = state.WithSpawnedEnemies(new[] { sneaky, fat });
        Assert.Equal(3, postSpawn.Enemies.Count);

        AssertBitIdentical(postSpawn);
    }

    [Fact]
    public void Dead_merc_plus_2_spawns_3_enemy_state_roundtrips()
    {
        // Dead enemy (hp=0) stays in list per CombatState dead-enemy contract.
        var mercDead = MakeEnemy(1u, "GremlinMerc", 0) with { CurrentHp = 0 };
        var state = BaseState(mercDead);

        var allocator = new CreatureIdAllocator(state);
        var sneaky = MakeEnemy(allocator.Next(), "SneakyGremlin", 10);
        var fat = MakeEnemy(allocator.Next(), "FatGremlin", 13);

        var postSpawn = state.WithSpawnedEnemies(new[] { sneaky, fat });
        Assert.Equal(3, postSpawn.Enemies.Count);

        AssertBitIdentical(postSpawn);
    }

    [Fact]
    public void Allocator_determinism_same_state_same_ids_after_roundtrip()
    {
        // Simulate what happens after a real serialize/deserialize cycle.
        var merc = MakeEnemy(1u, "GremlinMerc", 30);
        var state = BaseState(merc);

        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            Rng(),
            PlayerRng(),
            Tokens(),
            Stamp()
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        CombatState restored = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);

        var alloc1 = new CreatureIdAllocator(state);
        var alloc2 = new CreatureIdAllocator(restored);

        // Both allocators must mint identical id sequences.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(alloc1.Next(), alloc2.Next());
        }
    }
}
