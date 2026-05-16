using System.Buffers.Binary;
using System.Collections.Immutable;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Negative-path tests: every fail mode of Deserialize must throw
/// <see cref="StateCodecException"/> with a clear message. These tests are
/// the contract for callers using try/catch.
/// </summary>
public class DeserializeFailureTests
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

    private static byte[] SerializeMinimal()
    {
        return global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            BuildMinimalState(),
            new RunRngSet("s"),
            new PlayerRngSet(1u),
            new TokenMap(),
            BuildStamp()
        );
    }

    [Fact]
    public void Deserialize_throws_on_blob_too_short()
    {
        byte[] tiny = new byte[10];
        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(tiny)
        );
        Assert.Contains("too short", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_on_header_magic_mismatch()
    {
        byte[] blob = SerializeMinimal();
        blob[0] ^= 0xFF; // corrupt magic
        // First trailer hash will fail; check message captures hash mismatch.
        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob)
        );
        Assert.Contains("hash mismatch", ex.Message);
    }

    /// <summary>
    /// B.1-alpha-T3: pre-v2 blobs (schema=1) MUST be rejected. Stream B's
    /// MonsterIntent.MoveId + AttacksPlayedThisTurn + CardsDrawnThisCombat
    /// additions made v1 wire-incompatible without a corresponding version
    /// bump (the Stream-B-merge omission). The bump to v2 documents that;
    /// this test pins the rejection contract so older blobs can't slip in
    /// silently.
    /// </summary>
    [Fact]
    public void Deserialize_rejects_legacy_v1_blob_with_clear_message()
    {
        // Hand-craft the smallest possible v1 blob: header (magic + schema=1
        // + minimal stamp body) + section terminator + trailer.
        ByteWriter w = new();
        w.WriteU32(StateCodecConstants.HeaderMagic);
        w.WriteU16(1); // v1 — pre-Stream-B schema
        ByteWriter stampBody = new();
        stampBody.WriteU8(0);
        stampBody.WriteU16(0);
        for (int i = 0; i < 32; i++)
            stampBody.WriteU8(0);
        byte[] sb = stampBody.ToArray();
        w.WriteU16((ushort)sb.Length);
        w.WriteRawBytes(sb);
        w.WriteU16(StateCodecConstants.SectionTerminator);

        byte[] body = w.ToArray();
        Span<byte> sha = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(body, sha);
        ByteWriter full = new();
        full.WriteRawBytes(body);
        full.WriteU32(StateCodecConstants.TrailerMagic);
        full.WriteRawBytes(sha);

        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(full.ToArray())
        );
        // Error message must mention BOTH the rejected schema (1) and the
        // current schema (so an operator reading a log line knows where
        // the gap is).
        Assert.Contains("unsupported schema version 1", ex.Message);
        Assert.Contains($"this codec supports {StateCodecConstants.SchemaVersion}", ex.Message);
        Assert.Equal(3, StateCodecConstants.SchemaVersion);
    }

    [Fact]
    public void Deserialize_throws_on_schema_mismatch_fabricated_blob()
    {
        // Fabricate a blob with schema=99 and a correct trailer (so the schema
        // check is what fails, not the trailer).
        ByteWriter w = new();
        w.WriteU32(StateCodecConstants.HeaderMagic);
        w.WriteU16(99); // bogus schema
        // Build a minimal stamp body (gitSha empty, buildId empty, content_hash 32 zero bytes).
        ByteWriter stampBody = new();
        stampBody.WriteU8(0);
        stampBody.WriteU16(0);
        for (int i = 0; i < 32; i++)
            stampBody.WriteU8(0);
        byte[] sb = stampBody.ToArray();
        w.WriteU16((ushort)sb.Length);
        w.WriteRawBytes(sb);

        // No sections — just the terminator.
        w.WriteU16(StateCodecConstants.SectionTerminator);

        // Trailer.
        byte[] body = w.ToArray();
        Span<byte> sha = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(body, sha);
        ByteWriter full = new();
        full.WriteRawBytes(body);
        full.WriteU32(StateCodecConstants.TrailerMagic);
        full.WriteRawBytes(sha);

        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(full.ToArray())
        );
        Assert.Contains("unsupported schema version", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_on_body_byte_tampered()
    {
        byte[] blob = SerializeMinimal();
        // Flip a byte in the middle of the body (not in trailer).
        int mid = blob.Length / 2;
        blob[mid] ^= 0x01;
        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob)
        );
        Assert.Contains("hash mismatch", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_on_trailer_hash_byte_tampered()
    {
        byte[] blob = SerializeMinimal();
        // Trailer is the last 36 bytes; tamper the last byte (inside the SHA).
        blob[blob.Length - 1] ^= 0x01;
        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob)
        );
        Assert.Contains("hash mismatch", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_on_trailer_magic_tampered()
    {
        byte[] blob = SerializeMinimal();
        int trailerStart = blob.Length - StateCodecConstants.TrailerSizeBytes;
        blob[trailerStart] ^= 0xFF; // corrupt trailer magic
        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob)
        );
        Assert.Contains("magic mismatch", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_when_combatstate_section_absent()
    {
        // Build a valid blob with header + terminator + trailer; no sections.
        ByteWriter w = new();
        w.WriteU32(StateCodecConstants.HeaderMagic);
        w.WriteU16(StateCodecConstants.SchemaVersion);
        ByteWriter stampBody = new();
        stampBody.WriteU8(0);
        stampBody.WriteU16(0);
        for (int i = 0; i < 32; i++)
            stampBody.WriteU8(0);
        byte[] sb = stampBody.ToArray();
        w.WriteU16((ushort)sb.Length);
        w.WriteRawBytes(sb);
        w.WriteU16(StateCodecConstants.SectionTerminator);

        byte[] body = w.ToArray();
        Span<byte> sha = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(body, sha);
        ByteWriter full = new();
        full.WriteRawBytes(body);
        full.WriteU32(StateCodecConstants.TrailerMagic);
        full.WriteRawBytes(sha);

        byte[] blob = full.ToArray();
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        Assert.Empty(decoded.Sections);

        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded)
        );
        Assert.Contains("missing the CombatState section", ex.Message);
    }

    [Fact]
    public void Deserialize_throws_on_extra_bytes_between_terminator_and_trailer()
    {
        // Build a blob with extra garbage after the section terminator.
        ByteWriter w = new();
        w.WriteU32(StateCodecConstants.HeaderMagic);
        w.WriteU16(StateCodecConstants.SchemaVersion);
        ByteWriter stampBody = new();
        stampBody.WriteU8(0);
        stampBody.WriteU16(0);
        for (int i = 0; i < 32; i++)
            stampBody.WriteU8(0);
        byte[] sb = stampBody.ToArray();
        w.WriteU16((ushort)sb.Length);
        w.WriteRawBytes(sb);
        w.WriteU16(StateCodecConstants.SectionTerminator);
        // Garbage between terminator and trailer.
        w.WriteRawBytes(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        byte[] body = w.ToArray();
        Span<byte> sha = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(body, sha);
        ByteWriter full = new();
        full.WriteRawBytes(body);
        full.WriteU32(StateCodecConstants.TrailerMagic);
        full.WriteRawBytes(sha);

        var ex = Assert.Throws<StateCodecException>(() =>
            global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(full.ToArray())
        );
        Assert.Contains("garbage", ex.Message);
    }

    [Fact]
    public void Manifest_stamp_round_trips_byte_equal()
    {
        ManifestStamp stamp = BuildStamp();
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            BuildMinimalState(),
            new RunRngSet("seed"),
            new PlayerRngSet(1u),
            new TokenMap(),
            stamp
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        Assert.Equal(stamp.GitSha, decoded.Stamp.GitSha);
        Assert.Equal(stamp.BuildId, decoded.Stamp.BuildId);
        Assert.Equal(stamp.ContentHash, decoded.Stamp.ContentHash);
        Assert.Equal(stamp, decoded.Stamp);
    }

    [Fact]
    public void StateBlob_to_CombatState_returns_equal_record()
    {
        CombatState state = BuildMinimalState();
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            new RunRngSet("seed"),
            new PlayerRngSet(1u),
            new TokenMap(),
            BuildStamp()
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        CombatState recovered = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(
            decoded
        );
        Assert.True(state.Equals(recovered));
    }
}
