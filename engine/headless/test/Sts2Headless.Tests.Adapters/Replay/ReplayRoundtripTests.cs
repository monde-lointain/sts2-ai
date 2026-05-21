using System.Security.Cryptography;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// THE HARD GATE for the M3 replay recorder — Q1-ADR-002 / Q1-ADR-007
/// compatibility with the M1 State Codec.
///
/// <para>
/// <b>Contract:</b> for each step entry recorded by
/// <see cref="ReplayRecorder"/>, the <c>post_hash</c> field decoded by
/// <see cref="ReplayReader"/> equals the SHA-256 of the post-step
/// <see cref="CombatState"/>'s State-Codec section bytes — i.e., the
/// canonical fingerprint of the post-step state.
/// </para>
///
/// <para>
/// <b>Determinism:</b> recording the same input sequence twice produces
/// byte-identical replay files. This is the gate the S13 probe relies on:
/// the file is a deterministic function of its inputs.
/// </para>
///
/// <para>
/// <b>Fixtures:</b> the S7 corpus (<see cref="StateCodecFixtures.GenerateAll"/>)
/// provides ~20 (state, rng, tokens, stamp) tuples. Each fixture's state
/// becomes a record entry; we synthesize a varied action sequence to cover
/// PlayCard/EndTurn variants across the corpus.
/// </para>
/// </summary>
public class ReplayRoundtripTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    private static ManifestStamp MakeStamp() =>
        new("deadbeefcafebabe1234567890abcdef12345678", "Q1-Phase1-replay-roundtrip", ZeroHash);

    /// <summary>
    /// Synthesize a deterministic per-fixture <see cref="PlayerAction"/>.
    /// We cycle through (EndTurn, PlayCard-with-target, PlayCard-no-target)
    /// so every variant is exercised across the corpus.
    /// </summary>
    private static PlayerAction ActionFor(int idx)
    {
        return (idx % 3) switch
        {
            0 => PlayerAction.EndTurn.Instance,
            1 => new PlayerAction.PlayCard(checked((uint)(100 + idx)), new global::Sts2Headless.Domain.Combat.CreatureId(1u)),
            _ => new PlayerAction.PlayCard(checked((uint)(200 + idx)), null),
        };
    }

    /// <summary>
    /// Compute the expected post_hash for a fixture: serialize the full
    /// (state, rng, tokens, stamp) tuple via S7's StateCodec, decode it to
    /// extract the CombatState section bytes, then SHA-256 those bytes.
    /// </summary>
    private static byte[] ExpectedPostHash(StateCodecFixture f)
    {
        // The recorder uses a throwaway zero-stamp internally; we use the
        // same recipe so the bytes match. The CombatState section's bytes
        // are independent of the stamp, so the digest is stamp-independent.
        ManifestStamp throwaway = new("", "", new byte[32]);
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            f.State,
            f.RunRng,
            f.PlayerRng,
            f.Tokens,
            throwaway
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        byte[] csBytes = decoded.CombatStateBytes!;
        return SHA256.HashData(csBytes);
    }

    private static byte[] RecordCorpus(List<StateCodecFixture> fixtures, uint initialSeed)
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed);
        for (int i = 0; i < fixtures.Count; i++)
        {
            StateCodecFixture f = fixtures[i];
            rec.AppendStep(f.State, ActionFor(i), f.RunRng, f.PlayerRng, f.Tokens);
        }
        rec.Close();
        return ms.ToArray();
    }

    // ============================================================
    // Hard gate: post_hash matches CanonicalHash of post-state
    // ============================================================

    [Fact]
    public void HARD_GATE_each_entry_post_hash_equals_CanonicalHash_of_post_state()
    {
        List<StateCodecFixture> fixtures = StateCodecFixtures.GenerateAll();
        byte[] bytes = RecordCorpus(fixtures, initialSeed: 0xCAFEBABEu);
        ReplayBlob blob = ReplayReader.Decode(bytes);

        Assert.Equal(fixtures.Count, blob.Entries.Count);

        for (int i = 0; i < fixtures.Count; i++)
        {
            byte[] expected = ExpectedPostHash(fixtures[i]);
            byte[] recorded = blob.Entries[i].PostHash;

            Assert.True(
                expected.AsSpan().SequenceEqual(recorded),
                $"[fixture #{i} {fixtures[i].Name}] post_hash mismatch: "
                    + $"expected={Convert.ToHexString(expected)}, "
                    + $"recorded={Convert.ToHexString(recorded)}"
            );
        }
    }

    // ============================================================
    // Same-seed determinism: record-then-replay byte-identical
    // ============================================================

    [Fact]
    public void HARD_GATE_record_twice_yields_byte_identical_replay()
    {
        List<StateCodecFixture> fixtures = StateCodecFixtures.GenerateAll();
        byte[] first = RecordCorpus(fixtures, initialSeed: 0xCAFEBABEu);
        // Regenerate the fixtures from scratch (GenerateAll allocates fresh
        // objects each call); the second record must produce identical bytes.
        List<StateCodecFixture> fixtures2 = StateCodecFixtures.GenerateAll();
        byte[] second = RecordCorpus(fixtures2, initialSeed: 0xCAFEBABEu);

        Assert.True(
            first.AsSpan().SequenceEqual(second),
            $"record-then-replay must be byte-identical: first={first.Length}B second={second.Length}B"
        );
    }

    // ============================================================
    // Reader round-trip integrity for action data
    // ============================================================

    [Fact]
    public void HARD_GATE_decoded_actions_match_recorded_actions()
    {
        List<StateCodecFixture> fixtures = StateCodecFixtures.GenerateAll();
        byte[] bytes = RecordCorpus(fixtures, initialSeed: 0u);
        ReplayBlob blob = ReplayReader.Decode(bytes);

        Assert.Equal(fixtures.Count, blob.Entries.Count);
        for (int i = 0; i < fixtures.Count; i++)
        {
            ReplayEntry entry = blob.Entries[i];
            PlayerAction expected = ActionFor(i);
            PlayerAction decoded = ReplayActionCodec.Decode(entry.ActionType, entry.ActionData);
            Assert.Equal(expected, decoded);
        }
    }

    // ============================================================
    // Header roundtrip: every field preserved
    // ============================================================

    [Fact]
    public void Header_roundtrip_preserves_stamp_and_seed()
    {
        ManifestStamp stamp = new("abcdef0123456789", "Q1-Phase1-h", ZeroHash);
        StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, stamp, initialSeed: 0xDEADBEEFu);
        rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        rec.Close();

        ReplayBlob blob = ReplayReader.Decode(ms.ToArray());
        Assert.Equal(stamp, blob.ManifestStamp);
        Assert.Equal(0xDEADBEEFu, blob.InitialSeed);
        Assert.Equal((ushort)1, blob.SchemaVersion);
    }

    // ============================================================
    // Forward-compat: differing RNG/tokens but same CombatState yields
    // same post_hash (the hash is state-only).
    // ============================================================

    [Fact]
    public void Post_hash_is_independent_of_rng_and_tokens()
    {
        StateCodecFixture f0 = StateCodecFixtures.GenerateAll()[0];

        // Two recorders, same state but different RngBundle / TokenMap.
        using MemoryStream msA = new();
        ReplayRecorder a = new();
        a.OpenStream(msA, MakeStamp(), 0u);
        a.AppendStep(
            f0.State,
            PlayerAction.EndTurn.Instance,
            new RunRngSet("seed-A"),
            new PlayerRngSet(1u),
            new TokenMap()
        );
        a.Close();

        using MemoryStream msB = new();
        ReplayRecorder b = new();
        b.OpenStream(msB, MakeStamp(), 0u);
        // Different RNG seeds and a populated TokenMap.
        TokenMap tm = new();
        tm.GetOrAddId("foo");
        tm.GetOrAddId("bar");
        b.AppendStep(
            f0.State,
            PlayerAction.EndTurn.Instance,
            new RunRngSet("seed-B"),
            new PlayerRngSet(2u),
            tm
        );
        b.Close();

        ReplayBlob blobA = ReplayReader.Decode(msA.ToArray());
        ReplayBlob blobB = ReplayReader.Decode(msB.ToArray());

        Assert.True(
            blobA.Entries[0].PostHash.AsSpan().SequenceEqual(blobB.Entries[0].PostHash),
            "post_hash must be a function of CombatState alone (Rng/TokenMap don't enter)"
        );
    }
}
