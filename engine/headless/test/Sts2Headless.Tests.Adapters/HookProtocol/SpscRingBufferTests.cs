// SPSC ring buffer unit tests.
//
// What we pin:
//   1. Layout: header is 256 bytes; payload follows.
//   2. Basic write/read roundtrip.
//   3. Capacity validation (power-of-two; ≥ 64).
//   4. Wrap-around correctness: write across the wrap boundary, read across
//      the wrap boundary.
//   5. FreeCapacity / AvailableToRead snapshots.
//   6. Producer/consumer cross-thread under contention (SPSC, two threads).
//   7. Zero allocation on the hot path (TryWrite / TryRead).
//
// Tests use a managed `byte[]` pinned via `GCHandle.Alloc(... Pinned)` for the
// non-IPC unit cases. Cross-process roundtrip lives under
// MemoryMappedRingTests (it needs the real shared-memory plumbing).

// xUnit1031 fires on Task.WaitAll inside [Fact] — used in the SPSC contention
// test for bounded coordination with explicit timeout.
#pragma warning disable xUnit1031

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sts2Headless.Adapters.HookProtocol;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

public unsafe class SpscRingBufferTests
{
    private const int Capacity = 1024;

    private static GCHandle AllocBackingBuffer(out byte* basePtr)
    {
        byte[] buf = new byte[SpscRingBuffer.HeaderSize + Capacity];
        var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        basePtr = (byte*)h.AddrOfPinnedObject();
        return h;
    }

    [Fact]
    public void HeaderSize_is_256_bytes()
    {
        Assert.Equal(256, SpscRingBuffer.HeaderSize);
    }

    [Fact]
    public void Constructor_rejects_non_power_of_two_capacity()
    {
        byte[] buf = new byte[SpscRingBuffer.HeaderSize + 1000];
        var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            byte* ptr = (byte*)h.AddrOfPinnedObject();
            Assert.Throws<ArgumentException>(() =>
                new SpscRingBuffer(ptr, 1000, initializeHeader: true)
            );
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Constructor_rejects_capacity_below_64()
    {
        byte[] buf = new byte[SpscRingBuffer.HeaderSize + 32];
        var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            byte* ptr = (byte*)h.AddrOfPinnedObject();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SpscRingBuffer(ptr, 32, initializeHeader: true)
            );
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Roundtrip_small_frame()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            byte[] payload = { 0x01, 0x02, 0x03, 0x04, 0x05 };
            Assert.True(ring.TryWrite(payload));
            Assert.Equal(payload.Length, ring.AvailableToRead);

            byte[] readBack = new byte[payload.Length];
            Assert.True(ring.TryRead(readBack));
            Assert.Equal(payload, readBack);
            Assert.Equal(0, ring.AvailableToRead);
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Returns_false_when_no_space()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            byte[] big = new byte[Capacity];
            // Fill ring completely.
            Assert.True(ring.TryWrite(big));
            // Next write fails because ring is full.
            Assert.False(ring.TryWrite(new byte[1]));
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Returns_false_on_underflow()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            Assert.False(ring.TryRead(new byte[4]));
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Wrap_around_write_and_read()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            // Push tail near end-of-buffer, then drain so head=tail≈Capacity-10.
            byte[] prime = new byte[Capacity - 10];
            for (int i = 0; i < prime.Length; i++)
                prime[i] = (byte)(i % 251);
            Assert.True(ring.TryWrite(prime));
            byte[] sink = new byte[Capacity - 10];
            Assert.True(ring.TryRead(sink));
            Assert.Equal(prime, sink);

            // Now write a 50-byte payload that wraps around.
            byte[] wrap = new byte[50];
            for (int i = 0; i < wrap.Length; i++)
                wrap[i] = (byte)(0xA0 + i);
            Assert.True(ring.TryWrite(wrap));

            byte[] readback = new byte[50];
            Assert.True(ring.TryRead(readback));
            Assert.Equal(wrap, readback);
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Peek_does_not_advance()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };
            Assert.True(ring.TryWrite(payload));

            byte[] peek = new byte[4];
            Assert.True(ring.TryPeek(peek));
            Assert.Equal(payload, peek);
            Assert.Equal(4, ring.AvailableToRead);

            // Same data still readable after peek.
            byte[] read = new byte[4];
            Assert.True(ring.TryRead(read));
            Assert.Equal(payload, read);
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Free_capacity_reflects_write_state()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            Assert.Equal(Capacity, ring.FreeCapacity);
            byte[] payload = new byte[10];
            Assert.True(ring.TryWrite(payload));
            Assert.Equal(Capacity - 10, ring.FreeCapacity);
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Spsc_concurrent_producer_consumer_preserves_byte_stream()
    {
        // Use the largest reasonable buffer; the test pushes a long stream
        // across the wrap boundary multiple times.
        const int cap = 4096;
        byte[] buf = new byte[SpscRingBuffer.HeaderSize + cap];
        var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            byte* ptr = (byte*)h.AddrOfPinnedObject();
            var ring = new SpscRingBuffer(ptr, cap, initializeHeader: true);

            const int totalBytes = 200_000;
            byte[] produced = new byte[totalBytes];
            for (int i = 0; i < totalBytes; i++)
                produced[i] = (byte)(i * 7 + 13);

            byte[] consumed = new byte[totalBytes];
            var consumer = Task.Run(() =>
            {
                int got = 0;
                while (got < totalBytes)
                {
                    int chunk = Math.Min(37, totalBytes - got);
                    Span<byte> dest = consumed.AsSpan(got, chunk);
                    while (!ring.TryRead(dest))
                    {
                        Thread.SpinWait(1);
                    }
                    got += chunk;
                }
            });
            var producer = Task.Run(() =>
            {
                int sent = 0;
                while (sent < totalBytes)
                {
                    int chunk = Math.Min(53, totalBytes - sent);
                    ReadOnlySpan<byte> src = produced.AsSpan(sent, chunk);
                    while (!ring.TryWrite(src))
                    {
                        Thread.SpinWait(1);
                    }
                    sent += chunk;
                }
            });
            Assert.True(Task.WaitAll(new[] { producer, consumer }, TimeSpan.FromSeconds(30)));
            Assert.Equal(produced, consumed);
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Hot_path_writes_and_reads_do_not_allocate()
    {
        // Per stage prompt: zero allocations on Enqueue/Dequeue hot paths.
        // Verified via GC.GetAllocatedBytesForCurrentThread() deltas.
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            var ring = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            byte[] payload = new byte[64];
            byte[] sink = new byte[64];

            // Warm-up so JIT / runtime overhead doesn't leak into the measurement.
            for (int i = 0; i < 4; i++)
            {
                Assert.True(ring.TryWrite(payload));
                Assert.True(ring.TryRead(sink));
            }

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                Assert.True(ring.TryWrite(payload));
                Assert.True(ring.TryRead(sink));
            }
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();
            long delta = afterBytes - beforeBytes;
            // Tight bound; the only legit churn would be xUnit harness or JIT
            // tier-up which should be settled after warm-up. We give a generous
            // slack of 4 KiB to absorb any incidental harness noise without
            // letting real allocations slip through.
            Assert.True(delta < 4096, $"Allocated {delta} bytes on hot path (expected ~0).");
        }
        finally
        {
            h.Free();
        }
    }

    [Fact]
    public void Constructor_validates_header_capacity_on_peer_attach()
    {
        var h = AllocBackingBuffer(out byte* ptr);
        try
        {
            // Owner initializes with Capacity.
            _ = new SpscRingBuffer(ptr, Capacity, initializeHeader: true);
            // Peer with mismatched expected capacity should be rejected.
            Assert.Throws<InvalidOperationException>(() =>
                new SpscRingBuffer(ptr, Capacity * 2, initializeHeader: false)
            );
            // Peer with matching capacity attaches cleanly.
            _ = new SpscRingBuffer(ptr, Capacity, initializeHeader: false);
        }
        finally
        {
            h.Free();
        }
    }
}
