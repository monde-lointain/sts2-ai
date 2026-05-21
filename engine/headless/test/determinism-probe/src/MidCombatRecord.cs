using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// One per-turn-side state snapshot in the mid-combat behavioral probe.
/// Binary-serialized per the wave-45 G5/H9 wire layout:
/// <code>
/// [u32 magic=0x00010001][u32 record_count]
/// ([u32 record_len][u32 crc32][record bytes])*
/// </code>
/// Little-endian. Schema version 0x00010001 = mid-combat probe v1.
/// </summary>
/// <param name="Turn">Player-turn counter (0 = pre-combat, 1+ = in-combat).</param>
/// <param name="Side">
/// Snapshot side — one of "player-pre", "player-end", "enemy-end".
/// player-pre: after StartPlayerTurn (draw, energy set, blocks cleared);
/// player-end: after EndPlayerTurn;
/// enemy-end: after EnemyTurn.
/// </param>
/// <param name="Phase">String name of CombatPhase at snapshot time.</param>
/// <param name="PlayerHp">Player's current HP.</param>
/// <param name="PlayerBlock">Player's current block.</param>
/// <param name="Energy">Available energy.</param>
/// <param name="PowerStacks">Player's power stack list (order-stable).</param>
/// <param name="Enemies">Enemy snapshots in spawn order.</param>
/// <param name="RngCounter">Player RNG counter at snapshot time (for RNG divergence diagnosis).</param>
public sealed record MidCombatRecord(
    int Turn,
    string Side,
    string Phase,
    int PlayerHp,
    int PlayerBlock,
    int Energy,
    IReadOnlyList<PowerStackEntry> PowerStacks,
    IReadOnlyList<EnemySnapshot> Enemies,
    int RngCounter
)
{
    /// <summary>Mid-combat probe v1 file magic. First 4 bytes of every golden file.</summary>
    public const uint FileMagic = 0x00010001u;

    // -------------------------------------------------------------------------
    // Record-level binary write / read
    // -------------------------------------------------------------------------

    /// <summary>
    /// Write this record into <paramref name="bw"/>. Does NOT include the
    /// length-prefix or CRC32 framing — the file writer handles those.
    /// </summary>
    public void WriteTo(BinaryWriter bw)
    {
        ArgumentNullException.ThrowIfNull(bw);
        bw.Write(Turn);
        WriteString(bw, Side);
        WriteString(bw, Phase);
        bw.Write(PlayerHp);
        bw.Write(PlayerBlock);
        bw.Write(Energy);

        bw.Write(PowerStacks.Count);
        foreach (PowerStackEntry p in PowerStacks)
            p.WriteTo(bw);

        bw.Write(Enemies.Count);
        foreach (EnemySnapshot e in Enemies)
            e.WriteTo(bw);

        bw.Write(RngCounter);
    }

    /// <summary>Read a record from <paramref name="br"/>. Inverse of <see cref="WriteTo"/>.</summary>
    public static MidCombatRecord ReadFrom(BinaryReader br)
    {
        ArgumentNullException.ThrowIfNull(br);
        int turn = br.ReadInt32();
        string side = ReadString(br);
        string phase = ReadString(br);
        int playerHp = br.ReadInt32();
        int playerBlock = br.ReadInt32();
        int energy = br.ReadInt32();

        int powerCount = br.ReadInt32();
        var powers = new PowerStackEntry[powerCount];
        for (int i = 0; i < powerCount; i++)
            powers[i] = PowerStackEntry.ReadFrom(br);

        int enemyCount = br.ReadInt32();
        var enemies = new EnemySnapshot[enemyCount];
        for (int i = 0; i < enemyCount; i++)
            enemies[i] = EnemySnapshot.ReadFrom(br);

        int rngCounter = br.ReadInt32();

        return new MidCombatRecord(
            turn,
            side,
            phase,
            playerHp,
            playerBlock,
            energy,
            powers,
            enemies,
            rngCounter
        );
    }

    // -------------------------------------------------------------------------
    // File-level write / read
    // -------------------------------------------------------------------------

    /// <summary>
    /// Write a sequence of records to <paramref name="outputPath"/> using the
    /// framed file format: magic + count header, then per-record (length + crc32 + bytes).
    /// Overwrites the file if it exists; creates parent directories as needed.
    /// </summary>
    public static void WriteFile(string outputPath, IReadOnlyList<MidCombatRecord> records)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(records);
        string? dirName = Path.GetDirectoryName(outputPath);
        if (dirName is not null)
            Directory.CreateDirectory(dirName);
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

        bw.Write(FileMagic);
        bw.Write((uint)records.Count);

        foreach (MidCombatRecord rec in records)
        {
            using var ms = new MemoryStream();
            using (var mbw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                rec.WriteTo(mbw);
                mbw.Flush();
            }
            byte[] bytes = ms.ToArray();
            uint crc = Crc32.Compute(bytes);
            bw.Write((uint)bytes.Length);
            bw.Write(crc);
            bw.Write(bytes);
        }
        bw.Flush();
    }

    /// <summary>
    /// Read a file produced by <see cref="WriteFile"/>. Validates magic and
    /// per-record CRC32; throws <see cref="InvalidDataException"/> on corrupt data.
    /// </summary>
    public static IReadOnlyList<MidCombatRecord> ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        uint magic = br.ReadUInt32();
        if (magic != FileMagic)
            throw new InvalidDataException(
                $"MidCombatRecord.ReadFile: bad magic 0x{magic:X8} at '{path}'; expected 0x{FileMagic:X8}.");

        uint count = br.ReadUInt32();
        var records = new MidCombatRecord[count];
        for (uint i = 0; i < count; i++)
        {
            uint len = br.ReadUInt32();
            uint storedCrc = br.ReadUInt32();
            byte[] bytes = br.ReadBytes((int)len);
            if (bytes.Length != (int)len)
                throw new InvalidDataException(
                    $"MidCombatRecord.ReadFile: truncated record {i} at '{path}'.");
            uint computedCrc = Crc32.Compute(bytes);
            if (computedCrc != storedCrc)
                throw new InvalidDataException(
                    $"MidCombatRecord.ReadFile: CRC32 mismatch on record {i} at '{path}' " +
                    $"(stored=0x{storedCrc:X8} computed=0x{computedCrc:X8}).");
            using var ms = new MemoryStream(bytes, writable: false);
            using var mbr = new BinaryReader(ms, Encoding.UTF8, leaveOpen: false);
            records[i] = ReadFrom(mbr);
        }
        return records;
    }

    // -------------------------------------------------------------------------
    // String helpers (length-prefixed UTF-8)
    // -------------------------------------------------------------------------

    internal static void WriteString(BinaryWriter bw, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    internal static string ReadString(BinaryReader br)
    {
        int len = br.ReadInt32();
        byte[] bytes = br.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>A single power stack entry (modelId + stacks) on a creature.</summary>
/// <param name="ModelId">Power catalog id (e.g. "RitualPower").</param>
/// <param name="Stacks">Current stack count.</param>
public sealed record PowerStackEntry(string ModelId, int Stacks)
{
    /// <inheritdoc cref="MidCombatRecord.WriteTo"/>
    public void WriteTo(BinaryWriter bw)
    {
        MidCombatRecord.WriteString(bw, ModelId);
        bw.Write(Stacks);
    }

    /// <inheritdoc cref="MidCombatRecord.ReadFrom"/>
    public static PowerStackEntry ReadFrom(BinaryReader br)
    {
        string modelId = MidCombatRecord.ReadString(br);
        int stacks = br.ReadInt32();
        return new PowerStackEntry(modelId, stacks);
    }
}

/// <summary>
/// Snapshot of a single enemy creature at a turn-side checkpoint.
/// </summary>
/// <param name="Name">Monster catalog id (stable; used as diff key).</param>
/// <param name="Hp">Current HP.</param>
/// <param name="Block">Current block.</param>
/// <param name="MoveId">Current move-id (state-machine cursor).</param>
/// <param name="IntentKind">String label of the resolved intent kind.</param>
/// <param name="IntentDamagePerHit">Per-hit damage (0 for non-attack intents).</param>
/// <param name="IntentHitCount">Hit count (0 for non-attack intents).</param>
/// <param name="IntentSelfBlockGain">Block gained when executing this intent.</param>
/// <param name="Powers">Power stacks on this enemy.</param>
public sealed record EnemySnapshot(
    string Name,
    int Hp,
    int Block,
    string MoveId,
    string IntentKind,
    int IntentDamagePerHit,
    int IntentHitCount,
    int IntentSelfBlockGain,
    IReadOnlyList<PowerStackEntry> Powers
)
{
    /// <inheritdoc cref="MidCombatRecord.WriteTo"/>
    public void WriteTo(BinaryWriter bw)
    {
        MidCombatRecord.WriteString(bw, Name);
        bw.Write(Hp);
        bw.Write(Block);
        MidCombatRecord.WriteString(bw, MoveId);
        MidCombatRecord.WriteString(bw, IntentKind);
        bw.Write(IntentDamagePerHit);
        bw.Write(IntentHitCount);
        bw.Write(IntentSelfBlockGain);
        bw.Write(Powers.Count);
        foreach (PowerStackEntry p in Powers)
            p.WriteTo(bw);
    }

    /// <inheritdoc cref="MidCombatRecord.ReadFrom"/>
    public static EnemySnapshot ReadFrom(BinaryReader br)
    {
        string name = MidCombatRecord.ReadString(br);
        int hp = br.ReadInt32();
        int block = br.ReadInt32();
        string moveId = MidCombatRecord.ReadString(br);
        string intentKind = MidCombatRecord.ReadString(br);
        int damagePerHit = br.ReadInt32();
        int hitCount = br.ReadInt32();
        int selfBlockGain = br.ReadInt32();
        int powerCount = br.ReadInt32();
        var powers = new PowerStackEntry[powerCount];
        for (int i = 0; i < powerCount; i++)
            powers[i] = PowerStackEntry.ReadFrom(br);
        return new EnemySnapshot(
            name,
            hp,
            block,
            moveId,
            intentKind,
            damagePerHit,
            hitCount,
            selfBlockGain,
            powers
        );
    }
}

/// <summary>
/// CRC32 (ISO 3309 / Ethernet / ZIP polynomial) helper.
/// Only the bytes path is exposed — no streaming needed for probe record sizes.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            t[i] = crc;
        }
        return t;
    }

    public static uint Compute(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        uint crc = 0xFFFF_FFFFu;
        foreach (byte b in bytes)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFF_FFFFu;
    }
}
