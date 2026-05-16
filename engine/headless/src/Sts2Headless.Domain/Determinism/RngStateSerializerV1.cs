using System.Buffers.Binary;
using System.Text;

namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// M5 RNG state codec, schema version 1. All multi-byte integers are
/// little-endian; enums are emitted in <see cref="Enum.GetValues{TEnum}"/>
/// declaration order (NOT Dictionary enumeration order) to keep the byte
/// stream order-stable across runs and platforms.
///
/// Wire format (all integers little-endian):
///
///   Rng:
///     u16  magic  = 0x5234 ('R2' = "Rng v.2 schema family")
///     u16  schema = 1
///     u32  seed
///     i32  counter
///     [TOTAL: 12 bytes]
///
///   PlayerRngSet:
///     u16  magic  = 0x5054 ('PT' = "Player rng set Type")
///     u16  schema = 1
///     u32  seed
///     i32  count
///     count * { i32 enum-value (PlayerRngType cast to int), i32 counter }
///
///   RunRngSet:
///     u16  magic  = 0x5254 ('RT' = "Run rng set Type")
///     u16  schema = 1
///     i32  stringSeedByteLength
///     bytes stringSeedUtf8
///     u32  seed (derived hash; redundant with stringSeed but pinned for
///                tamper-detection on load)
///     i32  count
///     count * { i32 enum-value (RunRngType cast to int), i32 counter }
///
/// The magic constants are arbitrary 16-bit guards; they exist so a stale or
/// truncated blob fails loudly at deserialize time rather than silently
/// constructing an Rng with seed=0.
///
/// Order stability: we emit per-subsystem entries by iterating
/// <c>Enum.GetValues</c> (which returns enum declaration order on .NET 9),
/// NOT by iterating the underlying dictionary. This is the upstream-equivalent
/// behavior (upstream's SerializableX classes do the same — see
/// <c>SerializablePlayerRngSet.Serialize</c>).
///
/// Roundtrip strategy: deserialize via the existing
/// <c>PlayerRngSet.Restore</c> / <c>RunRngSet.Restore</c> factories, which
/// construct a fresh set and fast-forward each subsystem's counter to the
/// recorded value. The resumed Rng stream is byte-equal to the pre-save one
/// because that's exactly what FastForwardCounter is designed to guarantee.
/// </summary>
public sealed class RngStateSerializerV1 : IRngStateSerializer
{
    private const ushort MagicRng = 0x5234;
    private const ushort MagicPlayerSet = 0x5054;
    private const ushort MagicRunSet = 0x5254;
    private const ushort SchemaVersion = 1;

    // === Rng ===

    public byte[] SerializeRng(Rng rng)
    {
        byte[] buf = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), MagicRng);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), SchemaVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), rng.Seed);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), rng.Counter);
        return buf;
    }

    public Rng DeserializeRng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 12)
        {
            throw new InvalidDataException($"Rng state blob must be 12 bytes (got {bytes.Length})");
        }
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]);
        if (magic != MagicRng)
        {
            throw new InvalidDataException($"Rng state magic mismatch: 0x{magic:X4}");
        }
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2));
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported Rng state schema version: {schema}");
        }
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        int counter = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4));
        return new Rng(seed, counter);
    }

    // === PlayerRngSet ===

    public byte[] SerializePlayerRngSet(PlayerRngSet set)
    {
        PlayerRngType[] types = Enum.GetValues<PlayerRngType>();
        int payload = 4 + 4 + (types.Length * 8); // seed + count + per-entry
        byte[] buf = new byte[4 + payload]; // + 4 bytes header (magic+schema)
        int offset = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), MagicPlayerSet);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), SchemaVersion);
        offset += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), set.Seed);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), types.Length);
        offset += 4;
        foreach (PlayerRngType t in types)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), (int)t);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), set.GetCounter(t));
            offset += 4;
        }
        return buf;
    }

    public PlayerRngSet DeserializePlayerRngSet(ReadOnlySpan<byte> bytes)
    {
        ReadHeader(bytes, MagicPlayerSet);
        if (bytes.Length < 12)
        {
            throw new InvalidDataException(
                $"PlayerRngSet blob too short for header: {bytes.Length}"
            );
        }
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4));
        if (count < 0)
        {
            throw new InvalidDataException($"PlayerRngSet negative entry count: {count}");
        }
        int expected = 12 + (count * 8);
        if (bytes.Length != expected)
        {
            throw new InvalidDataException(
                $"PlayerRngSet blob length {bytes.Length} != expected {expected} for {count} entries"
            );
        }
        var counters = new Dictionary<PlayerRngType, int>(count);
        for (int i = 0; i < count; i++)
        {
            int entryOffset = 12 + (i * 8);
            int rawEnum = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(entryOffset, 4));
            int counter = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(entryOffset + 4, 4));
            if (!Enum.IsDefined(typeof(PlayerRngType), rawEnum))
            {
                throw new InvalidDataException(
                    $"PlayerRngSet unknown PlayerRngType value: {rawEnum}"
                );
            }
            counters[(PlayerRngType)rawEnum] = counter;
        }
        return PlayerRngSet.Restore(seed, counters);
    }

    // === RunRngSet ===

    public byte[] SerializeRunRngSet(RunRngSet set)
    {
        RunRngType[] types = Enum.GetValues<RunRngType>();
        byte[] stringBytes = Encoding.UTF8.GetBytes(set.StringSeed);
        int payload =
            4 // stringSeedByteLength
            + stringBytes.Length // string bytes
            + 4 // seed
            + 4 // count
            + (types.Length * 8); // per-entry
        byte[] buf = new byte[4 + payload];
        int offset = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), MagicRunSet);
        offset += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), SchemaVersion);
        offset += 2;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), stringBytes.Length);
        offset += 4;
        stringBytes.CopyTo(buf.AsSpan(offset, stringBytes.Length));
        offset += stringBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), set.Seed);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), types.Length);
        offset += 4;
        foreach (RunRngType t in types)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), (int)t);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset, 4), set.GetCounter(t));
            offset += 4;
        }
        return buf;
    }

    public RunRngSet DeserializeRunRngSet(ReadOnlySpan<byte> bytes)
    {
        ReadHeader(bytes, MagicRunSet);
        if (bytes.Length < 8)
        {
            throw new InvalidDataException(
                $"RunRngSet blob too short for header+stringLength: {bytes.Length}"
            );
        }
        int stringLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4));
        if (stringLen < 0)
        {
            throw new InvalidDataException($"RunRngSet negative string length: {stringLen}");
        }
        int afterString = 8 + stringLen;
        if (bytes.Length < afterString + 8)
        {
            throw new InvalidDataException(
                $"RunRngSet blob too short for string+seed+count: {bytes.Length}"
            );
        }
        string stringSeed = Encoding.UTF8.GetString(bytes.Slice(8, stringLen));
        uint storedSeed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(afterString, 4));
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(afterString + 4, 4));
        if (count < 0)
        {
            throw new InvalidDataException($"RunRngSet negative entry count: {count}");
        }
        int expected = afterString + 8 + (count * 8);
        if (bytes.Length != expected)
        {
            throw new InvalidDataException(
                $"RunRngSet blob length {bytes.Length} != expected {expected} for {count} entries"
            );
        }
        // Tamper-detection: rederive seed from the string and confirm.
        uint derivedSeed = (uint)StringHelpers.GetDeterministicHashCode(stringSeed);
        if (derivedSeed != storedSeed)
        {
            throw new InvalidDataException(
                $"RunRngSet stored seed 0x{storedSeed:X8} does not match derived 0x{derivedSeed:X8}"
            );
        }
        var counters = new Dictionary<RunRngType, int>(count);
        for (int i = 0; i < count; i++)
        {
            int entryOffset = afterString + 8 + (i * 8);
            int rawEnum = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(entryOffset, 4));
            int counter = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(entryOffset + 4, 4));
            if (!Enum.IsDefined(typeof(RunRngType), rawEnum))
            {
                throw new InvalidDataException($"RunRngSet unknown RunRngType value: {rawEnum}");
            }
            counters[(RunRngType)rawEnum] = counter;
        }
        return RunRngSet.Restore(stringSeed, counters);
    }

    private static void ReadHeader(ReadOnlySpan<byte> bytes, ushort expectedMagic)
    {
        if (bytes.Length < 4)
        {
            throw new InvalidDataException($"Rng state blob too short for header: {bytes.Length}");
        }
        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes[..2]);
        if (magic != expectedMagic)
        {
            throw new InvalidDataException(
                $"Rng state magic mismatch: expected 0x{expectedMagic:X4}, got 0x{magic:X4}"
            );
        }
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(2, 2));
        if (schema != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported Rng state schema version: {schema}");
        }
    }
}
