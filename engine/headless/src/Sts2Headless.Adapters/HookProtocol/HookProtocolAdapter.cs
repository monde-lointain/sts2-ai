// HookProtocolAdapter — Q1-side IPC adapter (M2 / Q1-ADR-005).
//
// Architecture (per docs/specs/modules/hook-protocol-adapter.md):
//
//   +-------+     +-------------------+     +--------------------+
//   |       |     |  q1->q8 ring      |     |                    |
//   |       |---->|  (SPSC byte ring) |---->|                    |
//   |  Q1   |     +-------------------+     |   Q8 worker        |
//   |       |     |  q8->q1 ring      |     |   (mock or real)   |
//   |       |<----|  (SPSC byte ring) |<----|                    |
//   +-------+     +-------------------+     +--------------------+
//        ^                                          ^
//        |                                          |
//   inbound sem (q8->q1 wakeup)                outbound sem (q1->q8 wakeup)
//
// Two rings + two semaphores. Q1 is producer on outbound, consumer on inbound.
// One reader thread runs inside Q1, blocks on the inbound semaphore, drains
// frames from the inbound ring, and routes responses by correlation_id to
// awaiters.
//
// Start():
//   1. Create both shm files at /dev/shm/<path>.q1tq8.shm and .q8tq1.shm.
//   2. Create both semaphores.
//   3. Subprocess launch is the CALLER's responsibility (we don't own how to
//      spawn the worker; tests use Process.Start, deploy uses systemd, etc.).
//   4. Send ManifestRequest, wait for ManifestResponse, compare manifests.
//   5. Spin up the reader thread.
//
// Stop():
//   - Send Terminate, set the cancellation token, join the reader thread,
//     dispose rings + semaphores + shm.
//
// Hot path:
//   FireHook + WaitResponse must be zero-allocation between Start() and the
//   correlation_id-keyed slot retrieval. We pre-allocate a scratch buffer
//   per call frame on the producer side (one buffer per FireHook, but bounded
//   by max-payload). The slot table is a ConcurrentDictionary, but we use it
//   only at the slow path; the hot return is a ManualResetEventSlim signal.
//
// Q1-ADR-008: single-threaded decision path. The reader thread is BACKGROUND
// machinery, not the decision path. Reader -> response-slot signaling is
// safe; the decision-path thread blocks on the slot ManualResetEventSlim
// until the response arrives.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// Q1-side IPC adapter for the Hook Protocol (M2). Owns the shared-memory
/// rings and named semaphores; spawns a background reader thread to drain
/// inbound responses. Public surface per stage S9 prompt.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed unsafe class HookProtocolAdapter : IDisposable
{
    // ---- Configuration ----

    /// <summary>Schema version this adapter implementation speaks. Bump on any wire-format change (Q1-ADR-005).</summary>
    public const ushort SchemaVersion = 1;

    /// <summary>Default ring capacity in bytes (must be power of two). 64 KiB is comfortable for Phase-1 payload sizes.</summary>
    public const int DefaultRingCapacity = 1 << 16;

    /// <summary>Default handshake timeout. If the worker doesn't send its manifest within this, the start fails.</summary>
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Default per-response wait timeout. The latency gate is 500 μs; the upper bound here catches a stalled worker.</summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);

    // ---- State ----

    private readonly int _ringCapacity;
    private readonly HookProtocolManifest _manifest;
    private readonly TimeSpan _handshakeTimeout;
    private readonly TimeSpan _waitTimeout;

    private SharedMemorySegment? _q1ToQ8Shm;
    private SharedMemorySegment? _q8ToQ1Shm;
    private SpscRingBuffer? _q1ToQ8Ring;
    private SpscRingBuffer? _q8ToQ1Ring;
    private PosixSemaphore? _outboundSem; // Q1 releases; Q8 waits.
    private PosixSemaphore? _inboundSem; // Q8 releases; Q1 waits.

    private Thread? _readerThread;
    private CancellationTokenSource? _readerCts;

    private readonly ConcurrentDictionary<ulong, ResponseSlot> _pending = new();
    private readonly Pool<ResponseSlot> _slotPool = new(static () => new ResponseSlot());

    private string? _basePath;
    private int _started;
    private int _disposed;

    // Hot-path scratch buffer: one outgoing frame at a time. Sized to the
    // largest WireFrameSize we'll emit on the FireHook path. Reallocated only
    // on grow.
    private byte[] _outboundScratch = Array.Empty<byte>();
    private readonly object _outboundLock = new();

    public HookProtocolAdapter(
        HookProtocolManifest manifest,
        int? ringCapacity = null,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? waitTimeout = null
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _manifest = manifest;
        _ringCapacity = ringCapacity ?? manifest.RingCapacity;
        if (_ringCapacity != manifest.RingCapacity)
        {
            throw new ArgumentException(
                $"ringCapacity ({_ringCapacity}) must match manifest.RingCapacity ({manifest.RingCapacity})",
                nameof(ringCapacity)
            );
        }
        _handshakeTimeout = handshakeTimeout ?? DefaultHandshakeTimeout;
        _waitTimeout = waitTimeout ?? DefaultWaitTimeout;
    }

    /// <summary>True once Start has succeeded and reader thread is running.</summary>
    public bool IsRunning => Volatile.Read(ref _started) == 1 && Volatile.Read(ref _disposed) == 0;

    /// <summary>Outbound (Q1->Q8) shared-memory file path. Available after Start.</summary>
    public string? OutboundShmPath => _basePath is null ? null : OutboundShmPathFor(_basePath);

    /// <summary>Inbound (Q8->Q1) shared-memory file path. Available after Start.</summary>
    public string? InboundShmPath => _basePath is null ? null : InboundShmPathFor(_basePath);

    /// <summary>Outbound semaphore name (Q1 releases; Q8 waits).</summary>
    public string? OutboundSemName => _basePath is null ? null : OutboundSemNameFor(_basePath);

    /// <summary>Inbound semaphore name (Q8 releases; Q1 waits).</summary>
    public string? InboundSemName => _basePath is null ? null : InboundSemNameFor(_basePath);

    // -------- Path / Name conventions --------
    // Used by the mock-worker (and any real Q8) to attach to the same primitives.

    public static string OutboundShmPathFor(string basePath) => basePath + ".q1tq8.shm";

    public static string InboundShmPathFor(string basePath) => basePath + ".q8tq1.shm";

    public static string OutboundSemNameFor(string basePath) =>
        SanitizeForSemName(basePath) + "_q1tq8";

    public static string InboundSemNameFor(string basePath) =>
        SanitizeForSemName(basePath) + "_q8tq1";

    private static string SanitizeForSemName(string s)
    {
        // POSIX sem names allow at most one leading '/'. Strip everything else.
        var buf = new char[s.Length];
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '/' || c == '\\')
                continue;
            buf[n++] = c;
        }
        return new string(buf, 0, n);
    }

    // ============================================================
    // Public API
    // ============================================================

    /// <summary>
    /// Bring up the rings + semaphores. <paramref name="workerSocketPath"/>
    /// is the BASE path for the IPC primitives (we derive .q1tq8.shm /
    /// .q8tq1.shm / semaphore names from it). Pre-create only — caller is
    /// responsible for spawning the worker process AFTER this returns so the
    /// worker sees the already-created shm + semaphores.
    /// </summary>
    public StartResult PreStart(string workerSocketPath)
    {
        ArgumentNullException.ThrowIfNull(workerSocketPath);
        if (Interlocked.CompareExchange(ref _started, 0, 0) != 0)
            throw new InvalidOperationException("HookProtocolAdapter already started");

        _basePath = workerSocketPath;
        int total = SpscRingBuffer.HeaderSize + _ringCapacity;

        // Outbound: Q1 produces, Q8 consumes. Q1 OWNS the shm.
        _q1ToQ8Shm = SharedMemorySegment.CreateOwner(OutboundShmPathFor(workerSocketPath), total);
        _q1ToQ8Ring = new SpscRingBuffer(_q1ToQ8Shm.BasePtr, _ringCapacity, initializeHeader: true);

        // Inbound: Q8 produces, Q1 consumes. Q1 STILL owns (we pre-create both).
        _q8ToQ1Shm = SharedMemorySegment.CreateOwner(InboundShmPathFor(workerSocketPath), total);
        _q8ToQ1Ring = new SpscRingBuffer(_q8ToQ1Shm.BasePtr, _ringCapacity, initializeHeader: true);

        // Stale-cleanup before Create.
        PosixSemaphore.Unlink(OutboundSemNameFor(workerSocketPath));
        PosixSemaphore.Unlink(InboundSemNameFor(workerSocketPath));
        _outboundSem = PosixSemaphore.Create(OutboundSemNameFor(workerSocketPath));
        _inboundSem = PosixSemaphore.Create(InboundSemNameFor(workerSocketPath));

        return new StartResult(
            OutboundShmPathFor(workerSocketPath),
            InboundShmPathFor(workerSocketPath),
            OutboundSemNameFor(workerSocketPath),
            InboundSemNameFor(workerSocketPath),
            _ringCapacity
        );
    }

    /// <summary>
    /// Complete startup: perform the handshake and start the reader thread.
    /// Caller MUST have spawned the worker process between PreStart and Start
    /// so the worker is ready to send its manifest.
    /// </summary>
    public void FinishStart()
    {
        if (
            _q1ToQ8Ring is null
            || _q8ToQ1Ring is null
            || _outboundSem is null
            || _inboundSem is null
        )
        {
            throw new InvalidOperationException("PreStart must be called before FinishStart");
        }

        // Send ManifestRequest with our manifest. The worker echoes its own
        // manifest; we compare. Bidirectional protocol.
        SendManifest(MessageType.ManifestRequest, correlationId: 0);

        // Wait for peer's manifest with a hard timeout.
        var peer = ReceiveManifestSync(_handshakeTimeout);
        string? mismatch = _manifest.DescribeMismatch(peer);
        if (mismatch is not null)
        {
            throw new HookProtocolHandshakeException(
                $"Hook-protocol manifest mismatch — {mismatch}"
            );
        }

        // Spawn the reader thread for subsequent inbound messages.
        _readerCts = new CancellationTokenSource();
        _readerThread = new Thread(ReaderLoop)
        {
            Name = "HookProtocol-Reader",
            IsBackground = true,
        };
        _readerThread.Start();

        Volatile.Write(ref _started, 1);
    }

    /// <summary>
    /// Convenience: PreStart, invoke <paramref name="onPrimitivesReady"/> (so
    /// caller can spawn the worker), then FinishStart.
    /// </summary>
    public void Start(string workerSocketPath, Action<StartResult> onPrimitivesReady)
    {
        ArgumentNullException.ThrowIfNull(onPrimitivesReady);
        var info = PreStart(workerSocketPath);
        onPrimitivesReady(info);
        FinishStart();
    }

    /// <summary>
    /// Synchronously fire a hook to the worker. Encodes the message frame,
    /// copies to the outbound ring, and signals the worker.
    /// </summary>
    public void FireHook(HookType hookType, ReadOnlySpan<byte> payload, ulong correlationId)
    {
        ThrowIfNotRunning();
        // Reserve a response slot so the reader can route by correlation_id.
        var slot = _slotPool.Rent();
        slot.Reset(correlationId);
        if (!_pending.TryAdd(correlationId, slot))
        {
            _slotPool.Return(slot);
            throw new InvalidOperationException(
                $"correlation_id {correlationId} is already pending — caller must use unique IDs per outstanding request"
            );
        }
        // Build the frame body: 2-byte hook type + caller payload. This is
        // the M2 wire convention for hook-request bodies.
        byte[] bodyBuf = RentBody(2 + payload.Length);
        try
        {
            bodyBuf[0] = (byte)((int)hookType & 0xFF);
            bodyBuf[1] = (byte)(((int)hookType >> 8) & 0xFF);
            payload.CopyTo(bodyBuf.AsSpan(2, payload.Length));

            var header = new MessageHeader(
                MessageType.HookRequest,
                SchemaVersion,
                2 + payload.Length,
                correlationId
            );
            SendFrame(header, bodyBuf.AsSpan(0, 2 + payload.Length));
        }
        finally
        {
            ReturnBody(bodyBuf);
        }
    }

    /// <summary>
    /// Await the response with the given correlation_id. Returns a
    /// <see cref="ResponseFrame"/> on success; throws on timeout / cancellation.
    /// </summary>
    public ValueTask<ResponseFrame> WaitResponse(
        ulong correlationId,
        CancellationToken ct = default
    )
    {
        ThrowIfNotRunning();
        if (!_pending.TryGetValue(correlationId, out var slot))
        {
            throw new InvalidOperationException(
                $"correlation_id {correlationId} has no pending entry — FireHook must be called first"
            );
        }
        return new ValueTask<ResponseFrame>(
            slot.WaitAsync(_waitTimeout, ct)
                .ContinueWith(
                    t =>
                    {
                        // Slot is single-shot; remove from map and return to pool once consumed.
                        _pending.TryRemove(correlationId, out _);
                        // Don't return to pool until value retrieved — keep ResponseFrame alive
                        // for the caller. We return on a separate path via Stop().
                        return t.GetAwaiter().GetResult();
                    },
                    ct,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
        );
    }

    /// <summary>
    /// Synchronous wait variant — used by the latency-gate hot path. Spins
    /// for a few microseconds, then falls back to event-block. The async
    /// version pays a TaskCompletionSource / continuation cost we don't want
    /// at 500-μs budget.
    /// </summary>
    public ResponseFrame WaitResponseSync(ulong correlationId, CancellationToken ct = default)
    {
        ThrowIfNotRunning();
        if (!_pending.TryGetValue(correlationId, out var slot))
        {
            throw new InvalidOperationException(
                $"correlation_id {correlationId} has no pending entry — FireHook must be called first"
            );
        }
        var frame = slot.Wait(_waitTimeout, ct);
        _pending.TryRemove(correlationId, out _);
        _slotPool.Return(slot);
        return frame;
    }

    /// <summary>
    /// Subscribe a single HookHandler to each of the given hook types that
    /// fires a HookRequest + waits for HookResponse synchronously, then
    /// returns the response bytes to the caller through the supplied
    /// callback. This is the lightweight integration shim S6 will use; tests
    /// use FireHook + WaitResponseSync directly.
    /// </summary>
    public IReadOnlyList<HookSubscriptionHandle> SubscribeForwardingTo(
        HookRegistry registry,
        IEnumerable<HookType> hookTypes,
        Func<HookType, byte[]> payloadFactory,
        Action<HookType, ResponseFrame> responseSink
    )
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hookTypes);
        ArgumentNullException.ThrowIfNull(payloadFactory);
        ArgumentNullException.ThrowIfNull(responseSink);

        var handles = new List<HookSubscriptionHandle>();
        ulong seq = 0;
        foreach (var ht in hookTypes)
        {
            HookType captured = ht;
            void Handler(HookContext _)
            {
                ulong id = Interlocked.Increment(ref seq);
                byte[] payload = payloadFactory(captured);
                FireHook(captured, payload, id);
                var frame = WaitResponseSync(id);
                responseSink(captured, frame);
            }
            var reg = new HookRegistration(Handler, priority: 0);
            handles.Add(registry.Subscribe(captured, reg));
        }
        return handles;
    }

    /// <summary>Graceful shutdown: send Terminate, join reader, dispose resources.</summary>
    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        try
        {
            if (Volatile.Read(ref _started) == 1)
            {
                // Send Terminate; ignore errors (the peer may be gone).
                try
                {
                    var header = new MessageHeader(MessageType.Terminate, SchemaVersion, 0, 0);
                    SendFrame(header, ReadOnlySpan<byte>.Empty);
                }
                catch
                { /* best effort */
                }

                _readerCts?.Cancel();
                // Self-wake the reader by posting the inbound sem.
                try
                {
                    _inboundSem?.Release();
                }
                catch { }
                _readerThread?.Join(TimeSpan.FromSeconds(2));
            }
        }
        finally
        {
            // Wake all pending slots so callers don't deadlock.
            foreach (var kv in _pending)
            {
                kv.Value.Fail(
                    new HookProtocolHandshakeException("Adapter stopped before response arrived")
                );
            }
            _pending.Clear();

            _readerCts?.Dispose();
            _q1ToQ8Ring = null;
            _q8ToQ1Ring = null;
            _outboundSem?.Dispose();
            _inboundSem?.Dispose();
            _q1ToQ8Shm?.Dispose();
            _q8ToQ1Shm?.Dispose();
            _outboundSem = null;
            _inboundSem = null;
            _q1ToQ8Shm = null;
            _q8ToQ1Shm = null;
        }
    }

    public void Dispose() => Stop();

    // ============================================================
    // Frame I/O
    // ============================================================

    private void SendFrame(MessageHeader header, ReadOnlySpan<byte> body)
    {
        int wire = header.WireSize;
        lock (_outboundLock)
        {
            if (_outboundScratch.Length < wire)
            {
                // Grow to next power-of-two ≥ wire.
                int cap = Math.Max(
                    64,
                    _outboundScratch.Length == 0 ? 64 : _outboundScratch.Length * 2
                );
                while (cap < wire)
                    cap *= 2;
                _outboundScratch = new byte[cap];
            }
            int n = MessageFrame.Encode(header, body, _outboundScratch);
            // Spin until ring has space; producer-side blocking on a full
            // ring is rare on the latency gate but possible under burst load.
            while (!_q1ToQ8Ring!.TryWrite(_outboundScratch.AsSpan(0, n)))
            {
                Thread.SpinWait(64);
            }
        }
        _outboundSem!.Release();
    }

    private void ReaderLoop()
    {
        var ct = _readerCts!.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Block until something is signaled OR a short tick expires
                // (defensive — we don't want to miss a Cancel between sem.Wait
                // and the iteration boundary).
                if (!_inboundSem!.Wait(TimeSpan.FromMilliseconds(100)))
                {
                    continue;
                }
                // Drain ALL frames currently in the inbound ring. The peer may
                // send multiple posts before we wake; one wakeup may cover N
                // frames. The semaphore counts wake-ups, not frames, so we
                // can't strictly pair them — drain-to-empty is the safe rule.
                DrainInbound();
            }
        }
        catch (Exception ex)
        {
            // Surface to all pending slots so callers don't hang.
            foreach (var kv in _pending)
            {
                kv.Value.Fail(ex);
            }
        }
    }

    private void DrainInbound()
    {
        Span<byte> headerSpan = stackalloc byte[MessageHeader.HeaderSize];
        while (true)
        {
            if (_q8ToQ1Ring!.AvailableToRead < MessageHeader.HeaderSize)
                return;
            if (!_q8ToQ1Ring.TryPeek(headerSpan))
                return;
            var header = MessageHeader.Decode(headerSpan);
            int wire = header.WireSize;
            if (_q8ToQ1Ring.AvailableToRead < wire)
                return;
            byte[] frameBuf = new byte[wire];
            if (!_q8ToQ1Ring.TryRead(frameBuf))
                return; // shouldn't happen given we checked
            byte[] payload = new byte[header.PayloadLength];
            Buffer.BlockCopy(frameBuf, MessageHeader.HeaderSize, payload, 0, header.PayloadLength);

            var frame = new ResponseFrame(header, payload);
            DispatchInbound(frame);
        }
    }

    private void DispatchInbound(ResponseFrame frame)
    {
        switch (frame.Header.Type)
        {
            case MessageType.HookResponse:
            case MessageType.Error:
                if (_pending.TryGetValue(frame.Header.CorrelationId, out var slot))
                {
                    slot.Complete(frame);
                }
                // Drop responses with no matching slot (e.g., late after timeout).
                break;
            case MessageType.Terminate:
                _readerCts!.Cancel();
                break;
            case MessageType.ManifestRequest:
            case MessageType.ManifestResponse:
                // After startup, peer should not send more manifests; ignore.
                break;
            default:
                // Reserved / unknown — drop silently in production; tests verify these aren't sent.
                break;
        }
    }

    private void SendManifest(MessageType type, ulong correlationId)
    {
        int size = _manifest.EncodedSize;
        byte[] body = RentBody(size);
        try
        {
            int n = _manifest.Encode(body);
            var hdr = new MessageHeader(type, SchemaVersion, n, correlationId);
            SendFrame(hdr, body.AsSpan(0, n));
        }
        finally
        {
            ReturnBody(body);
        }
    }

    private HookProtocolManifest ReceiveManifestSync(TimeSpan timeout)
    {
        // Block until inbound has at least one frame or timeout elapses.
        DateTime deadline = DateTime.UtcNow + timeout;
        Span<byte> headerSpan = stackalloc byte[MessageHeader.HeaderSize];
        while (true)
        {
            if (_q8ToQ1Ring!.AvailableToRead >= MessageHeader.HeaderSize)
            {
                if (_q8ToQ1Ring.TryPeek(headerSpan))
                {
                    var hdr = MessageHeader.Decode(headerSpan);
                    int wire = hdr.WireSize;
                    if (_q8ToQ1Ring.AvailableToRead >= wire)
                    {
                        byte[] frameBuf = new byte[wire];
                        _q8ToQ1Ring.TryRead(frameBuf);
                        if (
                            hdr.Type != MessageType.ManifestRequest
                            && hdr.Type != MessageType.ManifestResponse
                        )
                        {
                            throw new HookProtocolHandshakeException(
                                $"Handshake protocol violation: expected ManifestRequest/Response, got {hdr.Type}"
                            );
                        }
                        return HookProtocolManifest.Decode(
                            frameBuf.AsSpan(MessageHeader.HeaderSize, hdr.PayloadLength)
                        );
                    }
                }
            }
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new HookProtocolHandshakeException(
                    $"Handshake timed out after {timeout} waiting for peer manifest"
                );
            }
            // Bounded wait on inbound sem.
            _inboundSem!.Wait(
                remaining > TimeSpan.FromMilliseconds(50)
                    ? TimeSpan.FromMilliseconds(50)
                    : remaining
            );
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private void ThrowIfNotRunning()
    {
        if (!IsRunning)
            throw new InvalidOperationException(
                "HookProtocolAdapter is not running (call Start / FinishStart first)"
            );
    }

    // Body buffer pool: simple thread-static stash to avoid hot-path GC.
    [ThreadStatic]
    private static byte[]? t_bodyBuf;

    private static byte[] RentBody(int min)
    {
        var b = t_bodyBuf;
        if (b is null || b.Length < min)
        {
            b = new byte[Math.Max(min, 256)];
            t_bodyBuf = b;
        }
        return b;
    }

    private static void ReturnBody(
        byte[] _
    )
    { /* thread-static; no-op return */
    }

    /// <summary>Tuple-ish info returned by <see cref="PreStart"/>.</summary>
    public readonly struct StartResult
    {
        public string OutboundShmPath { get; }
        public string InboundShmPath { get; }
        public string OutboundSemName { get; }
        public string InboundSemName { get; }
        public int RingCapacity { get; }

        public StartResult(string outShm, string inShm, string outSem, string inSem, int cap)
        {
            OutboundShmPath = outShm;
            InboundShmPath = inShm;
            OutboundSemName = outSem;
            InboundSemName = inSem;
            RingCapacity = cap;
        }
    }

    /// <summary>
    /// Single-shot response slot. Wait() blocks until Complete(frame); Fail()
    /// surfaces an exception. Reused via the pool.
    /// </summary>
    internal sealed class ResponseSlot
    {
        private ResponseFrame? _frame;
        private Exception? _error;
        private readonly ManualResetEventSlim _signal = new(initialState: false);
        private TaskCompletionSource<ResponseFrame>? _tcs;
        public ulong CorrelationId;

        public void Reset(ulong correlationId)
        {
            CorrelationId = correlationId;
            _frame = null;
            _error = null;
            _signal.Reset();
            _tcs = null;
        }

        public void Complete(ResponseFrame frame)
        {
            _frame = frame;
            _signal.Set();
            _tcs?.TrySetResult(frame);
        }

        public void Fail(Exception ex)
        {
            _error = ex;
            _signal.Set();
            _tcs?.TrySetException(ex);
        }

        public ResponseFrame Wait(TimeSpan timeout, CancellationToken ct)
        {
            if (!_signal.Wait(timeout, ct))
            {
                throw new TimeoutException(
                    $"WaitResponse timed out for correlation_id={CorrelationId} after {timeout}"
                );
            }
            if (_error is not null)
                throw _error;
            return _frame!;
        }

        public Task<ResponseFrame> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            // Build a TCS the producer can complete from the reader thread.
            var tcs = new TaskCompletionSource<ResponseFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _tcs = tcs;
            // Already complete?
            if (_signal.IsSet)
            {
                if (_error is not null)
                    tcs.TrySetException(_error);
                else if (_frame is not null)
                    tcs.TrySetResult(_frame);
            }
            if (ct.CanBeCanceled)
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
            }
            if (timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero)
            {
                _ = Task.Delay(timeout, ct)
                    .ContinueWith(
                        _ =>
                        {
                            tcs.TrySetException(
                                new TimeoutException(
                                    $"WaitResponse timed out for correlation_id={CorrelationId} after {timeout}"
                                )
                            );
                        },
                        TaskScheduler.Default
                    );
            }
            return tcs.Task;
        }
    }

    /// <summary>Tiny object pool, single-threaded use (we hold _outboundLock).</summary>
    private sealed class Pool<T>
        where T : class
    {
        private readonly Func<T> _factory;
        private readonly ConcurrentQueue<T> _stash = new();

        public Pool(Func<T> factory)
        {
            _factory = factory;
        }

        public T Rent() => _stash.TryDequeue(out var x) ? x : _factory();

        public void Return(T x) => _stash.Enqueue(x);
    }
}
