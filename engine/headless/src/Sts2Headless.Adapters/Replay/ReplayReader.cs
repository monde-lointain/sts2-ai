using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Streaming decoder for replay files written by <see cref="ReplayRecorder"/>.
///
/// <para>
/// <b>Validation steps (in order):</b>
/// </para>
/// <list type="number">
///   <item>Read all bytes, separate body from trailer.</item>
///   <item>Verify <c>trailer_magic</c> equals <see cref="ReplayConstants.TrailerMagic"/>.</item>
///   <item>Verify the trailer's SHA-256 matches a fresh SHA-256 over the body bytes.</item>
///   <item>Decode header: magic, schema, manifest_stamp body, initial_seed.</item>
///   <item>Decode entries until the terminator sentinel is reached.</item>
/// </list>
///
/// <para>
/// Any inconsistency (bad magic, wrong schema, EOF, trailer hash mismatch,
/// malformed entry) throws <see cref="ReplayException"/> before a blob is
/// returned, so callers can rely on <see cref="ReplayBlob.TrailerValidated"/>
/// always being true.
/// </para>
/// </summary>
public static class ReplayReader
{
    // ============================================================
    // Public entry points
    // ============================================================

    /// <summary>
    /// Open the replay file at <paramref name="path"/> and decode it into a
    /// <see cref="ReplayBlob"/>. Throws <see cref="ReplayException"/> on any
    /// integrity or schema failure.
    /// </summary>
    public static ReplayBlob Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    /// <summary>
    /// Decode a replay-file byte buffer. Same contract as <see cref="Open(string)"/>
    /// but takes the bytes directly — used by tests and the future S13 probe.
    /// </summary>
    public static ReplayBlob Decode(ReadOnlySpan<byte> bytes)
    {
        // Minimum size: header magic(4) + schema(2) + manifest_size(4)
        //             + initial_seed(4) + terminator(4) + trailer(36)
        // = 54 bytes if the manifest stamp is empty (which is impossible —
        //   content_hash is always 32 bytes, so minimum stamp is 35 bytes).
        // We let the structural decode catch shortness rather than enforce
        // an artificial floor here.
        if (bytes.Length < 4 /* trailer magic */ + ReplayConstants.Sha256ByteLength)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: blob too short ({bytes.Length} bytes) to contain a trailer.");
        }

        int bodyLength = bytes.Length - ReplayConstants.TrailerSizeBytes;
        if (bodyLength < 0)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: blob too short ({bytes.Length} bytes) for trailer.");
        }
        ReadOnlySpan<byte> body = bytes[..bodyLength];
        ReadOnlySpan<byte> trailer = bytes[bodyLength..];

        ValidateTrailer(body, trailer);

        // ---- Header ----
        int pos = 0;
        if (body.Length < 4 + 2 + 4)
        {
            throw new ReplayException("ReplayReader.Decode: body too short for header fixed fields.");
        }
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos, 4));
        pos += 4;
        if (magic != ReplayConstants.HeaderMagic)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: header magic 0x{magic:X8} != expected 0x{ReplayConstants.HeaderMagic:X8}.");
        }
        ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos, 2));
        pos += 2;
        if (schema != ReplayConstants.SchemaVersion)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: unsupported replay schema version {schema}; this reader supports {ReplayConstants.SchemaVersion}.");
        }
        uint manifestSize = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos, 4));
        pos += 4;
        if (manifestSize > int.MaxValue)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: manifest_size {manifestSize} exceeds int.MaxValue.");
        }
        if (pos + (int)manifestSize > body.Length)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: manifest_size {manifestSize} exceeds remaining buffer ({body.Length - pos} bytes).");
        }
        StateCodec.ManifestStamp stamp = ManifestStampCodec.Decode(body.Slice(pos, (int)manifestSize));
        pos += (int)manifestSize;
        if (pos + 4 > body.Length)
        {
            throw new ReplayException("ReplayReader.Decode: body too short for initial_seed.");
        }
        uint initialSeed = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos, 4));
        pos += 4;

        // ---- Entries ----
        ImmutableList<ReplayEntry>.Builder entries = ImmutableList.CreateBuilder<ReplayEntry>();
        while (true)
        {
            if (pos + 4 > body.Length)
            {
                throw new ReplayException(
                    "ReplayReader.Decode: body ran out before entry terminator.");
            }
            uint turnNo = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos, 4));
            pos += 4;
            if (turnNo == ReplayConstants.EntryTerminator)
            {
                break;
            }
            // u8 phase + u8 action_type + u32 action_size
            if (pos + 1 + 1 + 4 > body.Length)
            {
                throw new ReplayException(
                    "ReplayReader.Decode: body too short for entry header.");
            }
            byte phaseByte = body[pos];
            pos += 1;
            CombatPhase phase = (CombatPhase)phaseByte;
            if (!Enum.IsDefined(typeof(CombatPhase), phase))
            {
                throw new ReplayException(
                    $"ReplayReader.Decode: invalid phase byte 0x{phaseByte:X2}.");
            }
            byte actionByte = body[pos];
            pos += 1;
            ReplayActionType actionType = (ReplayActionType)actionByte;
            if (!Enum.IsDefined(typeof(ReplayActionType), actionType))
            {
                throw new ReplayException(
                    $"ReplayReader.Decode: invalid action_type byte 0x{actionByte:X2}.");
            }
            uint actionSize = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos, 4));
            pos += 4;
            if (actionSize > int.MaxValue)
            {
                throw new ReplayException(
                    $"ReplayReader.Decode: action_size {actionSize} exceeds int.MaxValue.");
            }
            if (pos + (int)actionSize > body.Length)
            {
                throw new ReplayException(
                    $"ReplayReader.Decode: action_size {actionSize} exceeds remaining buffer ({body.Length - pos} bytes).");
            }
            byte[] actionData = body.Slice(pos, (int)actionSize).ToArray();
            pos += (int)actionSize;

            if (pos + ReplayConstants.Sha256ByteLength > body.Length)
            {
                throw new ReplayException(
                    "ReplayReader.Decode: body too short for entry post_hash.");
            }
            byte[] postHash = body.Slice(pos, ReplayConstants.Sha256ByteLength).ToArray();
            pos += ReplayConstants.Sha256ByteLength;

            entries.Add(new ReplayEntry(turnNo, phase, actionType, actionData, postHash));
        }

        if (pos != body.Length)
        {
            throw new ReplayException(
                $"ReplayReader.Decode: {body.Length - pos} trailing bytes after terminator.");
        }

        return new ReplayBlob(schema, stamp, initialSeed, entries.ToImmutable(), TrailerValidated: true);
    }

    // ============================================================
    // Trailer validation
    // ============================================================

    private static void ValidateTrailer(ReadOnlySpan<byte> body, ReadOnlySpan<byte> trailer)
    {
        if (trailer.Length != ReplayConstants.TrailerSizeBytes)
        {
            throw new ReplayException(
                $"ReplayReader: trailer length {trailer.Length} != expected {ReplayConstants.TrailerSizeBytes}.");
        }
        uint trailerMagic = BinaryPrimitives.ReadUInt32LittleEndian(trailer[..4]);
        if (trailerMagic != ReplayConstants.TrailerMagic)
        {
            throw new ReplayException(
                $"ReplayReader: trailer magic 0x{trailerMagic:X8} != expected 0x{ReplayConstants.TrailerMagic:X8}.");
        }
        ReadOnlySpan<byte> recordedHash = trailer.Slice(4, ReplayConstants.Sha256ByteLength);
        Span<byte> computed = stackalloc byte[ReplayConstants.Sha256ByteLength];
        SHA256.HashData(body, computed);
        if (!recordedHash.SequenceEqual(computed))
        {
            throw new ReplayException(
                "ReplayReader: trailer hash mismatch — tamper detected or blob corrupted.");
        }
    }
}
