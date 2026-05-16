using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Channels;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Records a replay session: header → step entries → terminator → trailer.
///
/// <para>
/// <b>Lifecycle:</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="Open(string, ManifestStamp, uint)"/> — opens the file
///   (or in-memory stream for tests) and writes the header synchronously
///   before returning so a subsequent Reader.Open on the same path sees a
///   well-formed header even if the recorder dies mid-session.</item>
///   <item><see cref="AppendStep"/> — encodes one step entry into a fresh
///   byte array and enqueues onto the background flush channel. Returns
///   synchronously after the channel write — no IO on the caller thread.</item>
///   <item><see cref="Close"/> — completes the channel, awaits the
///   background task, writes the terminator + trailer + final SHA-256, and
///   closes the underlying stream. Idempotent.</item>
/// </list>
///
/// <para>
/// <b>Threading contract:</b> the underlying <see cref="Stream"/> is touched
/// only by Open (which finishes before AppendStep is allowed), the background
/// flush task, and Close. The SHA-256 incremental hash is also held by the
/// background task; the hot path does not touch it. This makes
/// <see cref="AppendStep"/> safe to invoke from a single decision-thread
/// (matches M9's utility-thread model per Q1-ADR-008).
/// </para>
///
/// <para>
/// <b>Determinism:</b> AppendStep computes <c>post_hash</c> by serializing
/// (state, runRng, playerRng, tokens, stamp) via S7's
/// <see cref="global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize"/>,
/// extracting the <c>CombatState</c> section bytes, and SHA-256-ing those.
/// This makes the recorded post_hash a deterministic function of the post-
/// step CombatState alone (RngBundle / TokenMap / Stamp affect the section
/// envelope but the CombatState section's bytes are independent of them).
/// </para>
/// </summary>
public sealed class ReplayRecorder : IReplaySink
{
    // ============================================================
    // State (only the flush task / Close / Open touch these)
    // ============================================================

    private Stream? _stream;
    private bool _ownsStream;
    private IncrementalHash? _trailerHash;
    private Channel<byte[]>? _channel;
    private Task? _flushTask;
    private bool _isClosed;
    private bool _isOpen;
    private readonly object _closeLock = new();

    // ============================================================
    // Public API
    // ============================================================

    /// <summary>
    /// Open a replay session that writes to the given file path. Creates or
    /// overwrites the file. The header is written synchronously before this
    /// method returns; subsequent <see cref="AppendStep"/> calls are
    /// asynchronous.
    /// </summary>
    public void Open(string path, ManifestStamp stamp, uint initialSeed)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stamp);
        if (_isOpen)
        {
            throw new InvalidOperationException("ReplayRecorder.Open: already opened.");
        }
        // FileStream with FileMode.Create truncates any existing file.
        FileStream fs = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: false
        );
        OpenInternal(fs, ownsStream: true, stamp, initialSeed);
    }

    /// <summary>
    /// Open against an arbitrary <see cref="Stream"/> — used by tests with
    /// <see cref="MemoryStream"/>. The recorder does not take ownership; the
    /// caller is responsible for disposing.
    /// </summary>
    public void OpenStream(Stream stream, ManifestStamp stamp, uint initialSeed)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(stamp);
        if (_isOpen)
        {
            throw new InvalidOperationException("ReplayRecorder.OpenStream: already opened.");
        }
        OpenInternal(stream, ownsStream: false, stamp, initialSeed);
    }

    private void OpenInternal(Stream stream, bool ownsStream, ManifestStamp stamp, uint initialSeed)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _trailerHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Header is written synchronously before AppendStep is allowed.
        byte[] header = BuildHeaderBytes(stamp, initialSeed);
        _stream.Write(header, 0, header.Length);
        _trailerHash.AppendData(header);

        // Unbounded channel — disk pressure is handled by simply letting the
        // queue grow. The module spec calls for an oldest-drop policy on
        // pressure, but Phase-1 keeps it simple and lets the flush task fall
        // behind; the queue size is observable via metrics in a later stage.
        _channel = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            }
        );
        _flushTask = Task.Run(FlushLoopAsync);

        _isOpen = true;
        _isClosed = false;
    }

    /// <summary>
    /// Append one step to the replay. Encodes the step entry synchronously
    /// (so post_hash and action bytes are stable), then enqueues for the
    /// background flush task. Returns immediately after the channel write.
    /// </summary>
    public void AppendStep(
        CombatState postState,
        PlayerAction action,
        RunRngSet runRng,
        PlayerRngSet playerRng,
        TokenMap tokens
    )
    {
        ArgumentNullException.ThrowIfNull(postState);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(playerRng);
        ArgumentNullException.ThrowIfNull(tokens);

        Channel<byte[]> channel =
            _channel
            ?? throw new InvalidOperationException("ReplayRecorder.AppendStep: not opened.");
        if (_isClosed)
        {
            throw new InvalidOperationException("ReplayRecorder.AppendStep: already closed.");
        }

        // Compute post_hash using S7's StateCodec to get the CombatState
        // section bytes. We need the stamp here only to satisfy Serialize's
        // signature; the CombatState section's bytes are independent of it.
        // We use a throwaway stamp built from canonical zero bytes.
        byte[] postHash = ComputePostHash(postState, runRng, playerRng, tokens);

        // Encode the entry to bytes.
        (ReplayActionType actionType, byte[] actionData) = ReplayActionCodec.Encode(action);
        byte[] entry = BuildEntryBytes(
            (uint)postState.TurnCounter,
            postState.Phase,
            actionType,
            actionData,
            postHash
        );

        // Enqueue. Unbounded channel — TryWrite always succeeds while the
        // channel is open. Channel.Writer.Complete() in Close prevents
        // further writes.
        if (!channel.Writer.TryWrite(entry))
        {
            throw new InvalidOperationException(
                "ReplayRecorder.AppendStep: channel rejected write (already completed?)."
            );
        }
    }

    /// <summary>
    /// Flush pending entries, write terminator + trailer, and close the
    /// stream. Idempotent — multiple calls are safe.
    /// </summary>
    public void Close()
    {
        lock (_closeLock)
        {
            if (_isClosed)
            {
                return;
            }
            _isClosed = true;
        }

        Channel<byte[]>? channel = _channel;
        Task? flushTask = _flushTask;
        Stream? stream = _stream;
        IncrementalHash? trailerHash = _trailerHash;

        if (channel is null || flushTask is null || stream is null || trailerHash is null)
        {
            // Open was never called or already torn down.
            return;
        }

        // Stop accepting writes, then wait for the background to drain.
        channel.Writer.Complete();
        flushTask.GetAwaiter().GetResult();

        // Terminator (u32 = 0xFFFFFFFF) — part of the body that the trailer hashes.
        Span<byte> term = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(term, ReplayConstants.EntryTerminator);
        stream.Write(term);
        trailerHash.AppendData(term);

        // Trailer body = u32 magic + 32-byte sha256(all preceding bytes
        // up to and including the terminator, but NOT including the trailer
        // magic itself — matches S7's convention for the State Codec
        // trailer).
        byte[] digest = trailerHash.GetHashAndReset();

        Span<byte> trailerMagic = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(trailerMagic, ReplayConstants.TrailerMagic);
        stream.Write(trailerMagic);
        stream.Write(digest);

        stream.Flush();
        if (_ownsStream)
        {
            stream.Dispose();
        }

        trailerHash.Dispose();
        _stream = null;
        _channel = null;
        _flushTask = null;
        _trailerHash = null;
    }

    /// <summary>Dispose closes the recorder.</summary>
    public void Dispose() => Close();

    // ============================================================
    // Background flush loop
    // ============================================================

    private async Task FlushLoopAsync()
    {
        Channel<byte[]> channel = _channel!;
        Stream stream = _stream!;
        IncrementalHash hash = _trailerHash!;

        // Reader.WaitToReadAsync returns false when the channel is completed
        // and empty — at which point the loop exits and Close proceeds.
        while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out byte[]? entry))
            {
                stream.Write(entry, 0, entry.Length);
                hash.AppendData(entry);
            }
        }
    }

    // ============================================================
    // Byte builders (no IO; pure functions of inputs)
    // ============================================================

    /// <summary>
    /// Construct the header bytes:
    /// <code>
    ///   u32 HeaderMagic, u16 SchemaVersion,
    ///   u32 manifest_size, bytes manifest_stamp,
    ///   u32 initial_seed
    /// </code>
    /// </summary>
    private static byte[] BuildHeaderBytes(ManifestStamp stamp, uint initialSeed)
    {
        byte[] stampBytes = ManifestStampCodec.Encode(stamp);
        int totalLen =
            4 /* magic */
            + 2 /* schema */
            + 4 /* manifest_size */
            + stampBytes.Length
            + 4 /* initial_seed */
        ;
        byte[] buf = new byte[totalLen];
        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), ReplayConstants.HeaderMagic);
        pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos, 2), ReplayConstants.SchemaVersion);
        pos += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)stampBytes.Length);
        pos += 4;
        stampBytes.CopyTo(buf.AsSpan(pos, stampBytes.Length));
        pos += stampBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), initialSeed);
        return buf;
    }

    /// <summary>
    /// Construct one entry's bytes:
    /// <code>
    ///   u32 turn_no, u8 phase, u8 action_type,
    ///   u32 action_size, bytes action_data,
    ///   32 bytes post_hash
    /// </code>
    /// </summary>
    private static byte[] BuildEntryBytes(
        uint turnNo,
        CombatPhase phase,
        ReplayActionType actionType,
        byte[] actionData,
        byte[] postHash
    )
    {
        if (turnNo == ReplayConstants.EntryTerminator)
        {
            // Defensive: turn 0xFFFFFFFF collides with the terminator sentinel.
            // Phase-1 turn counters never reach this; reject explicitly to
            // surface format violations early.
            throw new ArgumentException(
                $"ReplayRecorder.AppendStep: turn_no=0x{turnNo:X8} collides with EntryTerminator.",
                nameof(turnNo)
            );
        }
        if (postHash.Length != ReplayConstants.Sha256ByteLength)
        {
            throw new ArgumentException(
                $"ReplayRecorder.AppendStep: post_hash must be {ReplayConstants.Sha256ByteLength} bytes (got {postHash.Length}).",
                nameof(postHash)
            );
        }

        int totalLen = 4 + 1 + 1 + 4 + actionData.Length + ReplayConstants.Sha256ByteLength;
        byte[] buf = new byte[totalLen];
        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), turnNo);
        pos += 4;
        buf[pos] = (byte)phase;
        pos += 1;
        buf[pos] = (byte)actionType;
        pos += 1;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), (uint)actionData.Length);
        pos += 4;
        actionData.AsSpan().CopyTo(buf.AsSpan(pos, actionData.Length));
        pos += actionData.Length;
        postHash.AsSpan().CopyTo(buf.AsSpan(pos, ReplayConstants.Sha256ByteLength));
        return buf;
    }

    /// <summary>
    /// Compute the canonical post-state hash:
    /// <c>SHA-256(StateCodec.Serialize(state, ...) CombatState section bytes)</c>.
    /// </summary>
    /// <remarks>
    /// We serialize the full state tuple via S7, decode it to extract the
    /// CombatState section bytes (which are independent of rng/tokens/stamp),
    /// then hash those bytes via S1's CanonicalHash. The hash is therefore
    /// a deterministic fingerprint of the post-step CombatState alone.
    /// </remarks>
    internal static byte[] ComputePostHash(
        CombatState state,
        RunRngSet runRng,
        PlayerRngSet playerRng,
        TokenMap tokens
    )
    {
        // Stamp content is irrelevant to the CombatState section bytes — use
        // a fixed throwaway. We pick a zero content_hash + empty strings so
        // the serialize call's stamp validation passes.
        ManifestStamp throwaway = new(
            string.Empty,
            string.Empty,
            new byte[ReplayConstants.Sha256ByteLength]
        );
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            state,
            runRng,
            playerRng,
            tokens,
            throwaway
        );
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        byte[]? csBytes =
            decoded.CombatStateBytes
            ?? throw new InvalidOperationException(
                "ReplayRecorder.ComputePostHash: StateCodec produced no CombatState section."
            );
        Span<byte> hash = stackalloc byte[ReplayConstants.Sha256ByteLength];
        SHA256.HashData(csBytes, hash);
        return hash.ToArray();
    }
}
