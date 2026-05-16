using System.Collections.Immutable;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Per-section serializer tests. Each test covers one shape: header+stamp,
/// Rng envelope, Tokens iteration, CombatState body. Then a basic full
/// roundtrip seals "writers and readers see the same wire format" before
/// the heavy bit-identical-roundtrip gate in <see cref="BitIdenticalRoundtripTests"/>.
/// </summary>
public class SectionWriterTests
{
    private static ManifestStamp BuildStamp()
    {
        byte[] contentHash = new byte[32];
        for (int i = 0; i < 32; i++)
            contentHash[i] = (byte)i;
        return new ManifestStamp("abc123def", "build-XYZ-001", contentHash);
    }

    private static CombatState BuildMinimalState() =>
        new CombatState(
            TurnCounter: 0,
            Phase: CombatPhase.CombatStart,
            Player: new Creature(
                0,
                "Silent",
                70,
                70,
                0,
                ImmutableList<PowerInstance>.Empty,
                null,
                IsPlayer: true
            ),
            Enemies: ImmutableList<Creature>.Empty,
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

    [Fact]
    public void Serialize_minimal_state_roundtrips()
    {
        CombatState state = BuildMinimalState();
        RunRngSet runRng = new RunRngSet("test-seed");
        PlayerRngSet playerRng = new PlayerRngSet(42u);
        TokenMap tokens = new();
        tokens.GetOrAddId("StrikeSilent");
        tokens.GetOrAddId("DefendSilent");

        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            tokens,
            BuildStamp()
        );

        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        Assert.Equal(StateCodecConstants.SchemaVersion, decoded.SchemaVersion);
        Assert.True(decoded.TrailerValidated);
        Assert.Equal("abc123def", decoded.Stamp.GitSha);
        Assert.Equal("build-XYZ-001", decoded.Stamp.BuildId);
        Assert.Equal(BuildStamp().ContentHash, decoded.Stamp.ContentHash);

        CombatState recovered = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(
            decoded
        );
        Assert.Equal(state, recovered);

        TokenMap recoveredTokens = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToTokenMap(
            decoded
        );
        Assert.Equal(2, recoveredTokens.Count);
        Assert.True(recoveredTokens.TryGetId("StrikeSilent", out int id1));
        Assert.Equal(0, id1);
        Assert.True(recoveredTokens.TryGetId("DefendSilent", out int id2));
        Assert.Equal(1, id2);
    }

    [Fact]
    public void Serialize_state_with_enemies_and_powers_roundtrips()
    {
        Creature player = new(
            0,
            "Silent",
            70,
            80,
            5,
            ImmutableList.Create(new PowerInstance("StrengthPower", 2, 0u, false)),
            null,
            IsPlayer: true
        );
        Creature enemy = new(
            1,
            "CalcifiedCultist",
            50,
            50,
            3,
            ImmutableList.Create(
                new PowerInstance("RitualPower", 3, 1u, false),
                new PowerInstance("PoisonPower", 7, 0u, true)
            ),
            new MonsterIntent(
                MonsterIntentKind.Attack,
                6,
                1,
                ImmutableList<MonsterIntentPower>.Empty
            ),
            IsPlayer: false
        );

        CardInstance card1 = new(10u, "StrikeSilent", 0, null);
        CardInstance card2 = new(11u, "StrikeSilent", 1, null);
        CardInstance card3 = new(12u, "DefendSilent", 0, CostOverride: 2);

        CombatState state = new(
            TurnCounter: 3,
            Phase: CombatPhase.PlayerActing,
            Player: player,
            Enemies: ImmutableList.Create(enemy),
            Energy: 2,
            BaseEnergyPerTurn: 3,
            HandDrawSize: 5,
            DrawPile: CardPile.OfRange(new[] { card1 }),
            HandPile: CardPile.OfRange(new[] { card2, card3 }),
            DiscardPile: CardPile.Empty,
            ExhaustPile: CardPile.Empty,
            PlayerRngCounter: 7,
            MonsterRngCounter: 11
        );

        RunRngSet runRng = new("seed");
        PlayerRngSet playerRng = new(123u);
        TokenMap tokens = new();

        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            tokens,
            BuildStamp()
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        CombatState recovered = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(
            decoded
        );
        Assert.Equal(state, recovered);
    }

    [Fact]
    public void Serialize_with_empty_tokens_roundtrips()
    {
        CombatState state = BuildMinimalState();
        RunRngSet runRng = new("seed");
        PlayerRngSet playerRng = new(0u);
        TokenMap tokens = new();

        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            tokens,
            BuildStamp()
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        TokenMap rec = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToTokenMap(decoded);
        Assert.Equal(0, rec.Count);
    }

    [Fact]
    public void Tokens_section_order_is_stable_with_same_insertions()
    {
        TokenMap a = new();
        a.GetOrAddId("B");
        a.GetOrAddId("A");
        a.GetOrAddId("C");
        TokenMap b = new();
        b.GetOrAddId("B");
        b.GetOrAddId("A");
        b.GetOrAddId("C");

        CombatState state = BuildMinimalState();
        RunRngSet runRng = new("s");
        PlayerRngSet playerRng = new(1u);

        byte[] blobA = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            a,
            BuildStamp()
        );
        byte[] blobB = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            b,
            BuildStamp()
        );
        Assert.Equal(blobA, blobB);
    }

    [Fact]
    public void Rng_section_recovers_runrngset_counters()
    {
        // Bump the counter so the M5-restore path is non-trivial.
        RunRngSet runRng = new("seed-for-counters");
        runRng.Shuffle.NextInt();
        runRng.Shuffle.NextInt();
        int before = runRng.GetCounter(RunRngType.Shuffle);

        PlayerRngSet playerRng = new(99u);
        playerRng.Rewards.NextInt();
        int playerBefore = playerRng.GetCounter(PlayerRngType.Rewards);

        CombatState state = BuildMinimalState();
        TokenMap tokens = new();

        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            tokens,
            BuildStamp()
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        (RunRngSet recoveredRun, PlayerRngSet recoveredPlayer) =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToRngBundle(decoded);

        Assert.Equal(before, recoveredRun.GetCounter(RunRngType.Shuffle));
        Assert.Equal(playerBefore, recoveredPlayer.GetCounter(PlayerRngType.Rewards));
        Assert.Equal(runRng.StringSeed, recoveredRun.StringSeed);
        Assert.Equal(playerRng.Seed, recoveredPlayer.Seed);
    }
}
