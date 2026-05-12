using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// The Q1 binary state codec. Serializes the (CombatState, RngBundle,
/// TokenMap, ManifestStamp) tuple into a single byte blob that round-trips
/// bit-identically — the CI gate per Q1-ADR-002 and pipeline
/// scaling-strategy §4.1 #4.
///
/// <para>
/// <b>On-wire layout</b> (little-endian, all multi-byte integers; see
/// <see cref="StateCodecConstants"/> for pinned magic/version values):
/// </para>
/// <code>
///   HEADER:
///     u32 magic, u16 schema, u16 header_size
///     stamp:
///       u8 git_sha_len, utf8 git_sha
///       u16 build_id_len, utf8 build_id
///       32 bytes content_hash
///   SECTIONS (canonical order: Rng → Tokens → CombatState):
///     for each:  u16 section_id, u32 section_size, bytes
///     terminator: u16 0xFFFF
///   TRAILER:
///     u32 trailer_magic, 32 bytes sha256(everything before trailer)
/// </code>
///
/// <para>
/// <b>Determinism contract:</b> for any fixed
/// (state, rng-bundle, tokens, stamp) tuple, <c>Serialize</c> must produce
/// byte-identical output across processes / platforms / runs. Every nested
/// section enforces this by emitting iterators in declaration order (no
/// Dictionary enumeration) and by length-prefixing variable-width fields.
/// </para>
///
/// <para>
/// <b>Failure modes (all surface as <see cref="StateCodecException"/>):</b>
/// schema-version mismatch, unknown section id, malformed bytes, EOF, trailer
/// SHA-256 mismatch (tamper detection), inconsistent section_size vs
/// available bytes.
/// </para>
/// </summary>
public static class StateCodec
{
    // ============================================================
    // Serialize
    // ============================================================

    /// <summary>
    /// Serialize a complete state tuple to a single byte blob. The output is
    /// bit-identical for the same input across processes and platforms —
    /// this is the codec's core contract.
    /// </summary>
    public static byte[] Serialize(
        CombatState state,
        RunRngSet runRng,
        PlayerRngSet playerRng,
        TokenMap tokens,
        ManifestStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(playerRng);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(stamp);

        // Pre-allocate something reasonable; will grow on overflow.
        ByteWriter w = new(initialCapacity: 1024);

        WriteHeader(w, stamp);

        // Sections — canonical order: Rng, Tokens, CombatState.
        WriteSection(w, SectionId.Rng, sw => WriteRngSection(sw, runRng, playerRng));
        WriteSection(w, SectionId.Tokens, sw => WriteTokensSection(sw, tokens));
        WriteSection(w, SectionId.CombatState, sw => WriteCombatState(sw, state));

        // Section terminator.
        w.WriteU16(StateCodecConstants.SectionTerminator);

        // Trailer = u32 magic + sha256 over preceding bytes.
        Span<byte> sha = stackalloc byte[StateCodecConstants.Sha256ByteLength];
        SHA256.HashData(w.WrittenSpan, sha);
        w.WriteU32(StateCodecConstants.TrailerMagic);
        w.WriteRawBytes(sha);

        return w.ToArray();
    }

    // ============================================================
    // Deserialize
    // ============================================================

    /// <summary>
    /// Decode a state blob into a <see cref="StateBlob"/>. Throws
    /// <see cref="StateCodecException"/> on schema-version mismatch, trailer
    /// hash mismatch (tamper), unknown section id, malformed bytes, or EOF.
    /// The returned blob always has <c>TrailerValidated=true</c>.
    /// </summary>
    public static StateBlob Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 /* magic */ + 2 /* schema */ + 2 /* header_size */
                          + StateCodecConstants.TrailerSizeBytes)
        {
            throw new StateCodecException(
                $"StateCodec.Deserialize: blob too short ({bytes.Length} bytes) to contain header+trailer.");
        }

        // Trailer first: split off the last 36 bytes, validate magic, verify SHA-256.
        int bodyLength = bytes.Length - StateCodecConstants.TrailerSizeBytes;
        ReadOnlySpan<byte> body = bytes[..bodyLength];
        ReadOnlySpan<byte> trailer = bytes[bodyLength..];
        ValidateTrailer(body, trailer);

        // Header.
        ByteReader r = new(body);
        uint magic = r.ReadU32();
        if (magic != StateCodecConstants.HeaderMagic)
        {
            throw new StateCodecException(
                $"StateCodec.Deserialize: header magic mismatch — got 0x{magic:X8}, expected 0x{StateCodecConstants.HeaderMagic:X8}.");
        }
        ushort schema = r.ReadU16();
        if (schema != StateCodecConstants.SchemaVersion)
        {
            throw new StateCodecException(
                $"StateCodec.Deserialize: unsupported schema version {schema}; this codec supports {StateCodecConstants.SchemaVersion}.");
        }
        ushort headerSize = r.ReadU16();
        ManifestStamp stamp = ReadStamp(ref r, headerSize);

        // Sections.
        var sections = ImmutableList.CreateBuilder<StateSection>();
        while (true)
        {
            if (r.Remaining < 2)
            {
                throw new StateCodecException(
                    "StateCodec.Deserialize: ran out of bytes before section terminator.");
            }
            ushort id = r.ReadU16();
            if (id == StateCodecConstants.SectionTerminator)
            {
                break;
            }
            uint size = r.ReadU32();
            if (size > int.MaxValue)
            {
                throw new StateCodecException(
                    $"StateCodec.Deserialize: section_size {size} exceeds int.MaxValue.");
            }
            ReadOnlySpan<byte> payload = r.ReadRawBytes((int)size);
            sections.Add(new StateSection((SectionId)id, payload.ToArray()));
        }

        // Anything after the section terminator (before the trailer) is a
        // protocol violation — the body length must match exactly.
        if (!r.IsAtEnd)
        {
            throw new StateCodecException(
                $"StateCodec.Deserialize: {r.Remaining} bytes of garbage between section terminator and trailer.");
        }

        return new StateBlob(schema, stamp, sections.ToImmutable(), TrailerValidated: true);
    }

    /// <summary>
    /// Convenience extractor: parse the CombatState section bytes back into
    /// a record. Throws if the section is absent or malformed.
    /// </summary>
    public static CombatState ToCombatState(StateBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        byte[]? cs = blob.CombatStateBytes;
        if (cs is null)
        {
            throw new StateCodecException(
                "StateCodec.ToCombatState: blob is missing the CombatState section.");
        }
        ByteReader r = new(cs);
        CombatState state = ReadCombatState(ref r);
        if (!r.IsAtEnd)
        {
            throw new StateCodecException(
                $"StateCodec.ToCombatState: trailing garbage in CombatState section ({r.Remaining} bytes).");
        }
        return state;
    }

    /// <summary>
    /// Convenience extractor: parse the TokenMap section bytes back into a
    /// fresh <see cref="TokenMap"/> with the same (token, id) entries in the
    /// same insertion order.
    /// </summary>
    public static TokenMap ToTokenMap(StateBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        byte[]? tb = blob.TokensBytes;
        if (tb is null)
        {
            throw new StateCodecException(
                "StateCodec.ToTokenMap: blob is missing the Tokens section.");
        }
        ByteReader r = new(tb);
        TokenMap map = ReadTokens(ref r);
        if (!r.IsAtEnd)
        {
            throw new StateCodecException(
                $"StateCodec.ToTokenMap: trailing garbage in Tokens section ({r.Remaining} bytes).");
        }
        return map;
    }

    /// <summary>
    /// Convenience extractor: parse the Rng section back into
    /// (RunRngSet, PlayerRngSet) using the M5 codec for the inner blobs.
    /// </summary>
    public static (RunRngSet RunRng, PlayerRngSet PlayerRng) ToRngBundle(StateBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        byte[]? rb = blob.RngBytes;
        if (rb is null)
        {
            throw new StateCodecException(
                "StateCodec.ToRngBundle: blob is missing the Rng section.");
        }
        ByteReader r = new(rb);
        (RunRngSet runRng, PlayerRngSet playerRng) = ReadRngBundle(ref r);
        if (!r.IsAtEnd)
        {
            throw new StateCodecException(
                $"StateCodec.ToRngBundle: trailing garbage in Rng section ({r.Remaining} bytes).");
        }
        return (runRng, playerRng);
    }

    // ============================================================
    // Section writers
    // ============================================================

    /// <summary>
    /// Helper that writes one section entry: u16 id + u32 size + body. The
    /// body is produced by <paramref name="writeBody"/>; the helper measures
    /// its byte-length and patches the size word once the body finishes.
    /// </summary>
    private static void WriteSection(ByteWriter w, SectionId id, Action<ByteWriter> writeBody)
    {
        w.WriteU16((ushort)id);
        // We need section_size BEFORE writing the body. Write body to a fresh
        // ByteWriter, measure it, then emit u32 length + bytes. This keeps
        // the wire format unambiguous and the reader stateless.
        ByteWriter inner = new(initialCapacity: 256);
        writeBody(inner);
        ReadOnlySpan<byte> body = inner.WrittenSpan;
        w.WriteU32((uint)body.Length);
        w.WriteRawBytes(body);
    }

    private static void WriteHeader(ByteWriter w, ManifestStamp stamp)
    {
        w.WriteU32(StateCodecConstants.HeaderMagic);
        w.WriteU16(StateCodecConstants.SchemaVersion);

        // Stamp body — measure first, write header_size, then body.
        ByteWriter stampBody = new(initialCapacity: 96);
        byte[] gitShaBytes = Encoding.UTF8.GetBytes(stamp.GitSha);
        if (gitShaBytes.Length > byte.MaxValue)
        {
            throw new ArgumentException(
                $"ManifestStamp.GitSha exceeds 255 UTF-8 bytes ({gitShaBytes.Length}).", nameof(stamp));
        }
        stampBody.WriteU8((byte)gitShaBytes.Length);
        stampBody.WriteRawBytes(gitShaBytes);

        byte[] buildIdBytes = Encoding.UTF8.GetBytes(stamp.BuildId);
        if (buildIdBytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException(
                $"ManifestStamp.BuildId exceeds 65535 UTF-8 bytes ({buildIdBytes.Length}).", nameof(stamp));
        }
        stampBody.WriteU16((ushort)buildIdBytes.Length);
        stampBody.WriteRawBytes(buildIdBytes);

        if (stamp.ContentHash.Length != StateCodecConstants.Sha256ByteLength)
        {
            throw new ArgumentException(
                $"ManifestStamp.ContentHash must be {StateCodecConstants.Sha256ByteLength} bytes (got {stamp.ContentHash.Length}).",
                nameof(stamp));
        }
        stampBody.WriteRawBytes(stamp.ContentHash);

        ReadOnlySpan<byte> body = stampBody.WrittenSpan;
        if (body.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"ManifestStamp body exceeds 65535 bytes ({body.Length}).");
        }
        w.WriteU16((ushort)body.Length);
        w.WriteRawBytes(body);
    }

    /// <summary>
    /// Rng section envelope:
    /// <code>
    ///   u8  run_blob_present = 1
    ///   u32 run_blob_len
    ///   bytes M5-RngStateSerializerV1.SerializeRunRngSet output
    ///   u8  player_blob_present = 1
    ///   u32 player_blob_len
    ///   bytes M5-RngStateSerializerV1.SerializePlayerRngSet output
    /// </code>
    /// The <c>_present</c> flags exist so a future stage can omit either set
    /// (e.g., during a partial restore) without bumping the schema.
    /// </summary>
    private static void WriteRngSection(ByteWriter w, RunRngSet runRng, PlayerRngSet playerRng)
    {
        IRngStateSerializer m5 = new RngStateSerializerV1();
        byte[] runBlob = m5.SerializeRunRngSet(runRng);
        byte[] playerBlob = m5.SerializePlayerRngSet(playerRng);

        w.WriteU8(1); // run_blob_present
        w.WriteU32((uint)runBlob.Length);
        w.WriteRawBytes(runBlob);
        w.WriteU8(1); // player_blob_present
        w.WriteU32((uint)playerBlob.Length);
        w.WriteRawBytes(playerBlob);
    }

    /// <summary>
    /// Tokens section: u32 count + count×(length-prefixed-utf8-string + u32 id).
    /// Order is <see cref="TokenMap.Enumerate"/>'s insertion order — the same
    /// instance always produces the same byte sequence.
    /// </summary>
    private static void WriteTokensSection(ByteWriter w, TokenMap tokens)
    {
        int count = tokens.Count;
        w.WriteU32((uint)count);
        foreach ((string tok, int id) in tokens.Enumerate())
        {
            w.WriteLengthPrefixedString(tok);
            w.WriteI32(id);
        }
    }

    // ============================================================
    // CombatState — recursive serialize
    // ============================================================

    /// <summary>
    /// CombatState body. Byte layout (in field-declaration order, per the
    /// XML doc on <c>CombatState</c>):
    /// <code>
    ///   i32 TurnCounter
    ///   i32 Phase (cast from CombatPhase)
    ///   Creature Player
    ///   i32 EnemyCount
    ///   EnemyCount * Creature
    ///   i32 Energy
    ///   i32 BaseEnergyPerTurn
    ///   i32 HandDrawSize
    ///   CardPile DrawPile
    ///   CardPile HandPile
    ///   CardPile DiscardPile
    ///   CardPile ExhaustPile
    ///   i32 PlayerRngCounter
    ///   i32 MonsterRngCounter
    ///   i32 AttacksPlayedThisTurn   (Stream-B-T4 addition)
    ///   i32 CardsDrawnThisCombat    (Stream-B-T4 addition)
    ///   i32 LastSpentEnergy         (B.1-gamma-T5 addition)
    ///   i32 ExhaustedShivCount      (B.1-gamma-T5 addition)
    /// </code>
    /// Future additive fields are appended at the end; the
    /// <see cref="StateCodecConstants.SchemaVersion"/> bump documents the
    /// addition.
    /// </summary>
    private static void WriteCombatState(ByteWriter w, CombatState state)
    {
        w.WriteI32(state.TurnCounter);
        w.WriteI32((int)state.Phase);

        WriteCreature(w, state.Player);

        w.WriteI32(state.Enemies.Count);
        for (int i = 0; i < state.Enemies.Count; i++)
        {
            WriteCreature(w, state.Enemies[i]);
        }

        w.WriteI32(state.Energy);
        w.WriteI32(state.BaseEnergyPerTurn);
        w.WriteI32(state.HandDrawSize);

        WriteCardPile(w, state.DrawPile);
        WriteCardPile(w, state.HandPile);
        WriteCardPile(w, state.DiscardPile);
        WriteCardPile(w, state.ExhaustPile);

        w.WriteI32(state.PlayerRngCounter);
        w.WriteI32(state.MonsterRngCounter);

        // Stream-B-T4 additive fields. Order is part of the wire schema; do
        // not reorder.
        w.WriteI32(state.AttacksPlayedThisTurn);
        w.WriteI32(state.CardsDrawnThisCombat);
        // B.1-gamma-T5 additive fields (X-cost snapshot + Shiv-exhaust counter).
        w.WriteI32(state.LastSpentEnergy);
        w.WriteI32(state.ExhaustedShivCount);
    }

    /// <summary>
    /// Creature byte layout (field-declaration order):
    /// <code>
    ///   u32 Id
    ///   string Name (length-prefixed utf8)
    ///   i32 CurrentHp
    ///   i32 MaxHp
    ///   i32 Block
    ///   i32 PowerCount, PowerCount * PowerInstance
    ///   u8 IntentPresent (0/1), if 1: MonsterIntent
    ///   bool IsPlayer
    /// </code>
    /// </summary>
    private static void WriteCreature(ByteWriter w, Creature c)
    {
        w.WriteU32(c.Id);
        w.WriteLengthPrefixedString(c.Name);
        w.WriteI32(c.CurrentHp);
        w.WriteI32(c.MaxHp);
        w.WriteI32(c.Block);

        w.WriteI32(c.Powers.Count);
        for (int i = 0; i < c.Powers.Count; i++)
        {
            WritePowerInstance(w, c.Powers[i]);
        }

        if (c.Intent is null)
        {
            w.WriteU8(0);
        }
        else
        {
            w.WriteU8(1);
            WriteMonsterIntent(w, c.Intent);
        }

        w.WriteBool(c.IsPlayer);
    }

    /// <summary>
    /// PowerInstance byte layout (field-declaration order):
    /// <code>
    ///   string ModelId, i32 Stacks, u32 SourceCreatureId, bool JustApplied
    /// </code>
    /// </summary>
    private static void WritePowerInstance(ByteWriter w, PowerInstance p)
    {
        w.WriteLengthPrefixedString(p.ModelId);
        w.WriteI32(p.Stacks);
        w.WriteU32(p.SourceCreatureId);
        w.WriteBool(p.JustApplied);
    }

    /// <summary>
    /// MonsterIntent byte layout (field-declaration order):
    /// <code>
    ///   i32 Kind, i32 DamagePerHit, i32 HitCount,
    ///   i32 AppliesCount, AppliesCount * (string PowerId, i32 Stacks),
    ///   string MoveId
    /// </code>
    /// <para>
    /// <b>Stream-B-T3 schema bump:</b> <c>MoveId</c> appended as the last field.
    /// Roundtrip stability preserved: pre-T3 blobs are not compatible (the
    /// stream codec versions in lockstep with the S6 state shape).
    /// </para>
    /// </summary>
    private static void WriteMonsterIntent(ByteWriter w, MonsterIntent m)
    {
        w.WriteI32((int)m.Kind);
        w.WriteI32(m.DamagePerHit);
        w.WriteI32(m.HitCount);
        w.WriteI32(m.AppliesPowers.Count);
        for (int i = 0; i < m.AppliesPowers.Count; i++)
        {
            MonsterIntentPower mp = m.AppliesPowers[i];
            w.WriteLengthPrefixedString(mp.PowerId);
            w.WriteI32(mp.Stacks);
        }
        w.WriteLengthPrefixedString(m.MoveId);
    }

    /// <summary>
    /// CardPile byte layout:
    /// <code>
    ///   i32 Count, Count * CardInstance
    /// </code>
    /// </summary>
    private static void WriteCardPile(ByteWriter w, CardPile pile)
    {
        w.WriteI32(pile.Cards.Count);
        for (int i = 0; i < pile.Cards.Count; i++)
        {
            WriteCardInstance(w, pile.Cards[i]);
        }
    }

    /// <summary>
    /// CardInstance byte layout (field-declaration order):
    /// <code>
    ///   u32 InstanceId, string ModelId, i32 UpgradeLevel,
    ///   u8 CostOverridePresent, if 1: i32 CostOverride
    /// </code>
    /// </summary>
    private static void WriteCardInstance(ByteWriter w, CardInstance c)
    {
        w.WriteU32(c.InstanceId);
        w.WriteLengthPrefixedString(c.ModelId);
        w.WriteI32(c.UpgradeLevel);
        if (c.CostOverride is null)
        {
            w.WriteU8(0);
        }
        else
        {
            w.WriteU8(1);
            w.WriteI32(c.CostOverride.Value);
        }
    }

    // ============================================================
    // CombatState — recursive deserialize (mirrors writers exactly)
    // ============================================================

    private static CombatState ReadCombatState(ref ByteReader r)
    {
        int turnCounter = r.ReadI32();
        CombatPhase phase = (CombatPhase)r.ReadI32();
        Creature player = ReadCreature(ref r);

        int enemyCount = r.ReadI32();
        if (enemyCount < 0)
        {
            throw new StateCodecException($"CombatState: negative enemy count {enemyCount}.");
        }
        var enemies = ImmutableList.CreateBuilder<Creature>();
        for (int i = 0; i < enemyCount; i++)
        {
            enemies.Add(ReadCreature(ref r));
        }

        int energy = r.ReadI32();
        int baseEnergy = r.ReadI32();
        int handDraw = r.ReadI32();

        CardPile draw = ReadCardPile(ref r);
        CardPile hand = ReadCardPile(ref r);
        CardPile discard = ReadCardPile(ref r);
        CardPile exhaust = ReadCardPile(ref r);

        int playerRng = r.ReadI32();
        int monsterRng = r.ReadI32();
        // Stream-B-T4 additive fields.
        int attacksPlayedThisTurn = r.ReadI32();
        int cardsDrawnThisCombat = r.ReadI32();
        // B.1-gamma-T5 additive fields.
        int lastSpentEnergy = r.ReadI32();
        int exhaustedShivCount = r.ReadI32();

        return new CombatState(
            turnCounter, phase, player, enemies.ToImmutable(),
            energy, baseEnergy, handDraw,
            draw, hand, discard, exhaust,
            playerRng, monsterRng,
            attacksPlayedThisTurn, cardsDrawnThisCombat,
            lastSpentEnergy, exhaustedShivCount);
    }

    private static Creature ReadCreature(ref ByteReader r)
    {
        uint id = r.ReadU32();
        string name = r.ReadLengthPrefixedString();
        int currentHp = r.ReadI32();
        int maxHp = r.ReadI32();
        int block = r.ReadI32();

        int powerCount = r.ReadI32();
        if (powerCount < 0)
        {
            throw new StateCodecException($"Creature: negative power count {powerCount}.");
        }
        var powers = ImmutableList.CreateBuilder<PowerInstance>();
        for (int i = 0; i < powerCount; i++)
        {
            powers.Add(ReadPowerInstance(ref r));
        }

        byte intentPresent = r.ReadU8();
        MonsterIntent? intent = intentPresent switch
        {
            0 => null,
            1 => ReadMonsterIntent(ref r),
            _ => throw new StateCodecException(
                $"Creature: invalid intent-present byte 0x{intentPresent:X2}."),
        };

        bool isPlayer = r.ReadBool();

        return new Creature(id, name, currentHp, maxHp, block, powers.ToImmutable(), intent, isPlayer);
    }

    private static PowerInstance ReadPowerInstance(ref ByteReader r)
    {
        string modelId = r.ReadLengthPrefixedString();
        int stacks = r.ReadI32();
        uint source = r.ReadU32();
        bool justApplied = r.ReadBool();
        return new PowerInstance(modelId, stacks, source, justApplied);
    }

    private static MonsterIntent ReadMonsterIntent(ref ByteReader r)
    {
        MonsterIntentKind kind = (MonsterIntentKind)r.ReadI32();
        int damagePerHit = r.ReadI32();
        int hitCount = r.ReadI32();
        int appliesCount = r.ReadI32();
        if (appliesCount < 0)
        {
            throw new StateCodecException($"MonsterIntent: negative applies-count {appliesCount}.");
        }
        var applies = ImmutableList.CreateBuilder<MonsterIntentPower>();
        for (int i = 0; i < appliesCount; i++)
        {
            string powerId = r.ReadLengthPrefixedString();
            int stacks = r.ReadI32();
            applies.Add(new MonsterIntentPower(powerId, stacks));
        }
        // Stream-B-T3 schema addition.
        string moveId = r.ReadLengthPrefixedString();
        return new MonsterIntent(kind, damagePerHit, hitCount, applies.ToImmutable(), moveId);
    }

    private static CardPile ReadCardPile(ref ByteReader r)
    {
        int count = r.ReadI32();
        if (count < 0)
        {
            throw new StateCodecException($"CardPile: negative count {count}.");
        }
        var cards = ImmutableList.CreateBuilder<CardInstance>();
        for (int i = 0; i < count; i++)
        {
            cards.Add(ReadCardInstance(ref r));
        }
        return new CardPile(cards.ToImmutable());
    }

    private static CardInstance ReadCardInstance(ref ByteReader r)
    {
        uint instanceId = r.ReadU32();
        string modelId = r.ReadLengthPrefixedString();
        int upgrade = r.ReadI32();
        byte overridePresent = r.ReadU8();
        int? costOverride = overridePresent switch
        {
            0 => null,
            1 => r.ReadI32(),
            _ => throw new StateCodecException(
                $"CardInstance: invalid override-present byte 0x{overridePresent:X2}."),
        };
        return new CardInstance(instanceId, modelId, upgrade, costOverride);
    }

    // ============================================================
    // Section readers (Header, Rng, Tokens, Trailer)
    // ============================================================

    private static ManifestStamp ReadStamp(ref ByteReader r, ushort headerSize)
    {
        int startPos = r.Position;
        byte gitShaLen = r.ReadU8();
        ReadOnlySpan<byte> gitSha = r.ReadRawBytes(gitShaLen);
        string gitShaStr = Encoding.UTF8.GetString(gitSha);

        ushort buildIdLen = r.ReadU16();
        ReadOnlySpan<byte> buildId = r.ReadRawBytes(buildIdLen);
        string buildIdStr = Encoding.UTF8.GetString(buildId);

        byte[] contentHash = r.ReadRawBytes(StateCodecConstants.Sha256ByteLength).ToArray();

        int consumed = r.Position - startPos;
        if (consumed != headerSize)
        {
            throw new StateCodecException(
                $"ManifestStamp: declared header_size {headerSize} != actual {consumed} bytes consumed.");
        }
        return new ManifestStamp(gitShaStr, buildIdStr, contentHash);
    }

    private static (RunRngSet RunRng, PlayerRngSet PlayerRng) ReadRngBundle(ref ByteReader r)
    {
        IRngStateSerializer m5 = new RngStateSerializerV1();

        byte runPresent = r.ReadU8();
        if (runPresent != 1)
        {
            throw new StateCodecException(
                $"RngSection: expected run_blob_present=1, got {runPresent} (Phase-1 always carries both).");
        }
        uint runLen = r.ReadU32();
        if (runLen > int.MaxValue)
        {
            throw new StateCodecException($"RngSection: run_blob_len {runLen} exceeds int.MaxValue.");
        }
        ReadOnlySpan<byte> runBytes = r.ReadRawBytes((int)runLen);
        RunRngSet runRng;
        try
        {
            runRng = m5.DeserializeRunRngSet(runBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new StateCodecException("RngSection: M5 RunRngSet decoder rejected the blob.", ex);
        }

        byte playerPresent = r.ReadU8();
        if (playerPresent != 1)
        {
            throw new StateCodecException(
                $"RngSection: expected player_blob_present=1, got {playerPresent} (Phase-1 always carries both).");
        }
        uint playerLen = r.ReadU32();
        if (playerLen > int.MaxValue)
        {
            throw new StateCodecException($"RngSection: player_blob_len {playerLen} exceeds int.MaxValue.");
        }
        ReadOnlySpan<byte> playerBytes = r.ReadRawBytes((int)playerLen);
        PlayerRngSet playerRng;
        try
        {
            playerRng = m5.DeserializePlayerRngSet(playerBytes);
        }
        catch (InvalidDataException ex)
        {
            throw new StateCodecException("RngSection: M5 PlayerRngSet decoder rejected the blob.", ex);
        }

        return (runRng, playerRng);
    }

    private static TokenMap ReadTokens(ref ByteReader r)
    {
        uint count = r.ReadU32();
        if (count > int.MaxValue)
        {
            throw new StateCodecException($"TokensSection: count {count} exceeds int.MaxValue.");
        }
        TokenMap map = new();
        for (int i = 0; i < count; i++)
        {
            string tok = r.ReadLengthPrefixedString();
            int id = r.ReadI32();
            int assigned = map.GetOrAddId(tok);
            if (assigned != id)
            {
                throw new StateCodecException(
                    $"TokensSection: token '{tok}' deserialized with id {id} but reconstruction assigned {assigned}.");
            }
        }
        return map;
    }

    private static void ValidateTrailer(ReadOnlySpan<byte> body, ReadOnlySpan<byte> trailer)
    {
        if (trailer.Length != StateCodecConstants.TrailerSizeBytes)
        {
            throw new StateCodecException(
                $"StateCodec: trailer length {trailer.Length} != expected {StateCodecConstants.TrailerSizeBytes}.");
        }
        uint trailerMagic = BinaryPrimitives.ReadUInt32LittleEndian(trailer[..4]);
        if (trailerMagic != StateCodecConstants.TrailerMagic)
        {
            throw new StateCodecException(
                $"StateCodec: trailer magic mismatch — got 0x{trailerMagic:X8}, expected 0x{StateCodecConstants.TrailerMagic:X8}.");
        }
        ReadOnlySpan<byte> recordedHash = trailer.Slice(4, StateCodecConstants.Sha256ByteLength);
        Span<byte> computed = stackalloc byte[StateCodecConstants.Sha256ByteLength];
        SHA256.HashData(body, computed);
        if (!recordedHash.SequenceEqual(computed))
        {
            throw new StateCodecException(
                "StateCodec: trailer hash mismatch — tamper detected or blob corrupted.");
        }
    }
}
