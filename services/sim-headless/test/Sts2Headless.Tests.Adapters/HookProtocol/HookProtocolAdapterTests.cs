// HookProtocolAdapter unit tests — peer simulated in the same process.
//
// These tests do NOT spawn the mock-worker (that's T6). Instead, after
// PreStart() returns, we open the rings and semaphores AS IF we were the
// worker, run a tiny scripted echo loop on a background task, and let the
// real adapter drive its handshake + FireHook + WaitResponseSync calls
// against us. This validates the adapter's wire-level correctness and the
// public surface in isolation.

#pragma warning disable xUnit1031 // bounded blocking inside [Fact] is intentional

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Sts2Headless.Adapters.HookProtocol;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

[SupportedOSPlatform("linux")]
public unsafe class HookProtocolAdapterTests
{
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static HookProtocolManifest MakeManifest(int ringCapacity = HookProtocolAdapter.DefaultRingCapacity)
    {
        byte[] hash = new byte[HookProtocolManifest.ContentHashSize];
        for (int i = 0; i < hash.Length; i++) hash[i] = (byte)i;
        return new HookProtocolManifest(hash, HookProtocolAdapter.SchemaVersion, ringCapacity, "q1-test");
    }

    private static string UniqueBase([System.Runtime.CompilerServices.CallerMemberName] string member = "")
        => Path.Combine("/dev/shm", $"q1-adapter-test-{member}-{Guid.NewGuid():N}");

    /// <summary>
    /// In-process worker simulation. Attaches to the existing shm + semaphores
    /// and runs a callback loop. Cleanup via Dispose.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private sealed class FakePeer : IDisposable
    {
        private readonly SharedMemorySegment _inboundShm;   // q1->q8 (we read from this)
        private readonly SharedMemorySegment _outboundShm;  // q8->q1 (we write to this)
        private readonly SpscRingBuffer _inboundRing;
        private readonly SpscRingBuffer _outboundRing;
        private readonly PosixSemaphore _inboundSem;        // q1->q8 sem; we WAIT on this
        private readonly PosixSemaphore _outboundSem;       // q8->q1 sem; we RELEASE this
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;
        private readonly Action<MessageHeader, byte[], Action<MessageType, byte[]>> _onFrame;

        public FakePeer(HookProtocolAdapter.StartResult info,
                        Action<MessageHeader, byte[], Action<MessageType, byte[]>> onFrame)
        {
            int total = SpscRingBuffer.HeaderSize + info.RingCapacity;
            _inboundShm = SharedMemorySegment.OpenExisting(info.OutboundShmPath, total);
            _outboundShm = SharedMemorySegment.OpenExisting(info.InboundShmPath, total);
            _inboundRing = new SpscRingBuffer(_inboundShm.BasePtr, info.RingCapacity, initializeHeader: false);
            _outboundRing = new SpscRingBuffer(_outboundShm.BasePtr, info.RingCapacity, initializeHeader: false);
            _inboundSem = PosixSemaphore.Open(info.OutboundSemName);
            _outboundSem = PosixSemaphore.Open(info.InboundSemName);
            _onFrame = onFrame;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FakePeer" };
            _thread.Start();
        }

        public void SendFrame(MessageType type, ulong correlationId, byte[] payload)
        {
            var hdr = new MessageHeader(type, HookProtocolAdapter.SchemaVersion, payload.Length, correlationId);
            byte[] wire = new byte[hdr.WireSize];
            MessageFrame.Encode(hdr, payload, wire);
            while (!_outboundRing.TryWrite(wire))
            {
                Thread.SpinWait(64);
            }
            _outboundSem.Release();
        }

        private void Loop()
        {
            var ct = _cts.Token;
            Span<byte> headerSpan = stackalloc byte[MessageHeader.HeaderSize];
            void Respond(MessageType type, byte[] payload)
            {
                var hdr = new MessageHeader(type, HookProtocolAdapter.SchemaVersion, payload.Length, _lastCorr);
                byte[] wire = new byte[hdr.WireSize];
                MessageFrame.Encode(hdr, payload, wire);
                while (!_outboundRing.TryWrite(wire))
                {
                    Thread.SpinWait(64);
                }
                _outboundSem.Release();
            }
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!_inboundSem.Wait(TimeSpan.FromMilliseconds(100))) continue;
                    while (true)
                    {
                        if (_inboundRing.AvailableToRead < MessageHeader.HeaderSize) break;
                        if (!_inboundRing.TryPeek(headerSpan)) break;
                        var hdr = MessageHeader.Decode(headerSpan);
                        if (_inboundRing.AvailableToRead < hdr.WireSize) break;
                        byte[] frameBuf = new byte[hdr.WireSize];
                        _inboundRing.TryRead(frameBuf);
                        byte[] payload = new byte[hdr.PayloadLength];
                        Buffer.BlockCopy(frameBuf, MessageHeader.HeaderSize, payload, 0, hdr.PayloadLength);
                        _lastCorr = hdr.CorrelationId;
                        if (hdr.Type == MessageType.Terminate) { _cts.Cancel(); break; }
                        _onFrame(hdr, payload, Respond);
                    }
                }
            }
            catch { /* shutdown */ }
        }
        private ulong _lastCorr;

        public void Dispose()
        {
            _cts.Cancel();
            try { _inboundSem.Release(); } catch { }
            _thread.Join(TimeSpan.FromSeconds(2));
            _inboundSem.Dispose();
            _outboundSem.Dispose();
            _inboundShm.Dispose();
            _outboundShm.Dispose();
        }
    }

    [Fact]
    public void Start_with_matching_manifest_completes_handshake()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            adapter.Start(basePath, info =>
            {
                peer = new FakePeer(info, (hdr, payload, respond) =>
                {
                    // Echo any HookRequest -> HookResponse with same payload.
                    if (hdr.Type == MessageType.HookRequest) respond(MessageType.HookResponse, payload);
                });
                // Worker would first send its manifest; do that.
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
            });
            Assert.True(adapter.IsRunning);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void Start_with_mismatched_schema_throws_handshake_exception()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            var ex = Assert.Throws<HookProtocolHandshakeException>(() =>
                adapter.Start(basePath, info =>
                {
                    peer = new FakePeer(info, (hdr, payload, respond) => { });
                    // Send a manifest with WRONG schema version.
                    var wrong = new HookProtocolManifest(
                        manifest.ContentHash, schemaVersion: (ushort)(manifest.SchemaVersion + 1),
                        manifest.RingCapacity, manifest.BuildId);
                    byte[] mbuf = new byte[wrong.EncodedSize];
                    wrong.Encode(mbuf);
                    peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
                }));
            Assert.Contains("schema_version", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void Start_with_mismatched_content_hash_throws()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            byte[] otherHash = new byte[HookProtocolManifest.ContentHashSize];
            for (int i = 0; i < otherHash.Length; i++) otherHash[i] = 0xFF;
            var ex = Assert.Throws<HookProtocolHandshakeException>(() =>
                adapter.Start(basePath, info =>
                {
                    peer = new FakePeer(info, (hdr, payload, respond) => { });
                    var wrong = new HookProtocolManifest(
                        otherHash, manifest.SchemaVersion, manifest.RingCapacity, manifest.BuildId);
                    byte[] mbuf = new byte[wrong.EncodedSize];
                    wrong.Encode(mbuf);
                    peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
                }));
            Assert.Contains("content_hash", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void FireHook_then_WaitResponseSync_returns_echoed_payload()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            adapter.Start(basePath, info =>
            {
                peer = new FakePeer(info, (hdr, payload, respond) =>
                {
                    // Echo HookRequest -> HookResponse.
                    if (hdr.Type == MessageType.HookRequest) respond(MessageType.HookResponse, payload);
                });
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
            });

            byte[] payload = { 0xAA, 0xBB, 0xCC };
            adapter.FireHook(HookType.BeforeCombatStart, payload, correlationId: 42);
            var resp = adapter.WaitResponseSync(42);
            Assert.Equal(MessageType.HookResponse, resp.Header.Type);
            Assert.Equal(42ul, resp.Header.CorrelationId);
            // The body is 2-byte hook-type + payload.
            byte[] body = resp.Payload.ToArray();
            Assert.Equal((int)HookType.BeforeCombatStart, body[0] | (body[1] << 8));
            Assert.Equal(0xAA, body[2]);
            Assert.Equal(0xBB, body[3]);
            Assert.Equal(0xCC, body[4]);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void Multiple_concurrent_FireHook_calls_route_by_correlation_id()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            adapter.Start(basePath, info =>
            {
                peer = new FakePeer(info, (hdr, payload, respond) =>
                {
                    if (hdr.Type == MessageType.HookRequest) respond(MessageType.HookResponse, payload);
                });
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
            });

            // Fire three back-to-back, then wait in reverse order. Each
            // wait must match the right correlation_id.
            byte[] p1 = { 1 };
            byte[] p2 = { 2 };
            byte[] p3 = { 3 };
            adapter.FireHook(HookType.AfterCombatEnd, p1, correlationId: 100);
            adapter.FireHook(HookType.AfterCombatEnd, p2, correlationId: 200);
            adapter.FireHook(HookType.AfterCombatEnd, p3, correlationId: 300);
            var r3 = adapter.WaitResponseSync(300);
            var r1 = adapter.WaitResponseSync(100);
            var r2 = adapter.WaitResponseSync(200);
            Assert.Equal(100ul, r1.Header.CorrelationId);
            Assert.Equal(200ul, r2.Header.CorrelationId);
            Assert.Equal(300ul, r3.Header.CorrelationId);
            // Payload offset 2..end is the original.
            Assert.Equal(1, r1.Payload.Span[2]);
            Assert.Equal(2, r2.Payload.Span[2]);
            Assert.Equal(3, r3.Payload.Span[2]);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void Duplicate_correlation_id_rejected()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            adapter.Start(basePath, info =>
            {
                peer = new FakePeer(info, (hdr, payload, respond) => { });
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
            });
            adapter.FireHook(HookType.AfterCombatEnd, ReadOnlySpan<byte>.Empty, correlationId: 7);
            Assert.Throws<InvalidOperationException>(
                () => adapter.FireHook(HookType.AfterCombatEnd, ReadOnlySpan<byte>.Empty, correlationId: 7));
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }

    [Fact]
    public void SubscribeForwardingTo_invokes_FireHook_on_registry_Fire()
    {
        if (!OnLinux) return;
        string basePath = UniqueBase();
        var manifest = MakeManifest();
        using var adapter = new HookProtocolAdapter(manifest);
        FakePeer? peer = null;
        try
        {
            adapter.Start(basePath, info =>
            {
                peer = new FakePeer(info, (hdr, payload, respond) =>
                {
                    if (hdr.Type == MessageType.HookRequest) respond(MessageType.HookResponse, new byte[] { 0xEC });
                });
                byte[] mbuf = new byte[manifest.EncodedSize];
                manifest.Encode(mbuf);
                peer!.SendFrame(MessageType.ManifestResponse, 0, mbuf);
            });

            var registry = new HookRegistry();
            int received = 0;
            var handles = adapter.SubscribeForwardingTo(
                registry,
                hookTypes: new[] { HookType.BeforeSideTurnStart, HookType.BeforeTurnEnd },
                payloadFactory: _ => new byte[] { 0xFF },
                responseSink: (_, _) => Interlocked.Increment(ref received));
            Assert.Equal(2, handles.Count);

            // Fire with default(HookContext); our handler does NOT dereference
            // the context (it just calls FireHook+WaitResponseSync).
            registry.Fire(HookType.BeforeSideTurnStart, default);
            registry.Fire(HookType.BeforeTurnEnd, default);
            Assert.Equal(2, received);
        }
        finally
        {
            adapter.Stop();
            peer?.Dispose();
        }
    }
}
