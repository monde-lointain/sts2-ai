// SpscRingBuffer — lock-free single-producer / single-consumer byte ring,
// backed by a fixed-size memory-mapped view so a sibling process can attach
// to the same bytes. One ring carries one direction (Q1 -> Q8 or Q8 -> Q1);
// the protocol allocates two of them.
//
// Reference design:
//   Vyukov SPSC queue (https://www.1024cores.net/home/lock-free-algorithms/queues/unbounded-spsc-queue)
//   plus LMAX Disruptor cursor-with-cache patterns
//   (https://lmax-exchange.github.io/disruptor/disruptor.html). We keep the
//   producer's tail and consumer's head in separate cache lines to avoid
//   false sharing — the dominant cost on tight IPC loops. We additionally
//   keep "cached" copies of the other side's index so we read the remote
//   atomic at most once per slow path, not once per byte.
//
// Wire layout in the mapped view (all little-endian, fixed offsets):
//
//   off  size  field
//   ---  ----  ----------------------------------------------------------------
//     0     8  head        (u64, consumer's read cursor)
//     8    56  pad         (cache-line padding so head & tail don't share line)
//    64     8  tail        (u64, producer's write cursor)
//    72    56  pad
//   128     8  capacity    (u64, payload byte capacity — power of 2)
//   136     8  flags       (u64; reserved, set to 0 in v1)
//   144   112  reserved    (zero)
//   256   ...  payload     (capacity bytes follow)
//
// The header is 256 bytes (4 cache lines). The total mapped size is
// 256 + capacity bytes. Capacity is a power of two so wrap is a bitmask.
//
// Operations:
//   TryWrite(ReadOnlySpan&lt;byte&gt;) → bool: copies bytes to ring, advances tail
//     with Volatile.Write (release fence). Returns false if not enough free
//     space; producer should signal-then-retry. Zero allocation.
//   TryRead(Span&lt;byte&gt; dest, out int read) → bool: peeks header u32 length
//     then copies length bytes to dest, advances head with Volatile.Write.
//     Returns false if no full frame available. Zero allocation.
//   AvailableToRead → bytes pending for consumer (snapshot).
//   FreeCapacity → bytes producer can write right now (snapshot).
//
// Frames over the byte ring carry their own length prefix at the protocol
// layer (MessageFrame, T2) — the ring itself is byte-oriented.
//
// Thread safety:
//   - Single producer: ONE thread/process calls TryWrite at any time.
//   - Single consumer: ONE thread/process calls TryRead at any time.
//   - Cross-process safety is via the explicit volatile ops + cache-line
//     padding; no locks are taken.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// Lock-free SPSC byte ring backed by an unmanaged buffer. Power-of-two
/// capacity; explicit memory ordering via <see cref="Volatile"/>. Zero
/// allocation on Write/Read hot paths.
/// </summary>
public sealed unsafe class SpscRingBuffer
{
    /// <summary>Header size in bytes. Fixed; must match across processes.</summary>
    public const int HeaderSize = 256;

    private const int HeadOffset = 0;
    private const int TailOffset = 64;
    private const int CapacityOffset = 128;
    private const int FlagsOffset = 136;

    private readonly byte* _base;
    private readonly int _capacity;
    private readonly int _mask;

    // Cached remote-side indices to keep the hot path off the shared atomic.
    // Updated only when the cached value is stale. Per-side caches live on the
    // owning side only; they are NOT shared cross-process.
    private long _cachedHead;
    private long _cachedTail;

    /// <summary>
    /// Constructor. <paramref name="basePtr"/> points to the start of the
    /// mapped view; the buffer's <see cref="HeaderSize"/> + capacity bytes
    /// must already be reserved. <paramref name="capacity"/> is the payload
    /// size (must be power-of-two, ≥ 64, ≤ 1 GiB).
    /// </summary>
    public SpscRingBuffer(byte* basePtr, int capacity, bool initializeHeader)
    {
        if (basePtr is null) throw new ArgumentNullException(nameof(basePtr));
        ValidateCapacity(capacity);
        _base = basePtr;
        _capacity = capacity;
        _mask = capacity - 1;

        if (initializeHeader)
        {
            // Owner stamps the header so the peer can sanity-check.
            *(long*)(basePtr + HeadOffset) = 0;
            *(long*)(basePtr + TailOffset) = 0;
            *(long*)(basePtr + CapacityOffset) = capacity;
            *(long*)(basePtr + FlagsOffset) = 0;
        }
        else
        {
            // Peer verifies the capacity field matches what it expects.
            long observed = *(long*)(basePtr + CapacityOffset);
            if (observed != capacity)
            {
                throw new InvalidOperationException(
                    $"SpscRingBuffer header capacity mismatch: header={observed} expected={capacity}");
            }
        }
        _cachedHead = 0;
        _cachedTail = 0;
    }

    /// <summary>Capacity in bytes (power of two).</summary>
    public int Capacity => _capacity;

    /// <summary>Snapshot of bytes pending for the consumer.</summary>
    public int AvailableToRead
    {
        get
        {
            long head = Volatile.Read(ref HeadRef);
            long tail = Volatile.Read(ref TailRef);
            return (int)(tail - head);
        }
    }

    /// <summary>Snapshot of bytes the producer can write right now.</summary>
    public int FreeCapacity
    {
        get
        {
            long head = Volatile.Read(ref HeadRef);
            long tail = Volatile.Read(ref TailRef);
            return _capacity - (int)(tail - head);
        }
    }

    /// <summary>
    /// Producer-side write. Copies the entire span if there is room; returns
    /// false (without partial write) otherwise. Zero allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(ReadOnlySpan<byte> src)
    {
        if (src.IsEmpty) return true;
        int len = src.Length;
        if (len > _capacity)
        {
            // Defensive: a frame larger than the whole ring can never fit;
            // returning false would deadlock. Caller's contract violation.
            throw new ArgumentException(
                $"Write of {len} bytes exceeds ring capacity {_capacity}", nameof(src));
        }

        long tail = Volatile.Read(ref TailRef);
        // Fast path: cached head may say we have room. Slow path re-reads the
        // shared atomic if cache says we don't.
        long head = _cachedHead;
        int free = _capacity - (int)(tail - head);
        if (free < len)
        {
            head = Volatile.Read(ref HeadRef);
            _cachedHead = head;
            free = _capacity - (int)(tail - head);
            if (free < len) return false;
        }

        int writeIdx = (int)(tail & _mask);
        int firstChunk = Math.Min(len, _capacity - writeIdx);
        fixed (byte* sp = src)
        {
            Buffer.MemoryCopy(sp, _base + HeaderSize + writeIdx, firstChunk, firstChunk);
            int remainder = len - firstChunk;
            if (remainder > 0)
            {
                Buffer.MemoryCopy(sp + firstChunk, _base + HeaderSize, remainder, remainder);
            }
        }

        // Release fence: bytes are visible before tail advance.
        Volatile.Write(ref TailRef, tail + len);
        return true;
    }

    /// <summary>
    /// Consumer-side read. Pulls exactly <c>dest.Length</c> bytes if available;
    /// returns false (without partial read) otherwise. Zero allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(Span<byte> dest)
    {
        if (dest.IsEmpty) return true;
        int len = dest.Length;

        long head = Volatile.Read(ref HeadRef);
        long tail = _cachedTail;
        int avail = (int)(tail - head);
        if (avail < len)
        {
            tail = Volatile.Read(ref TailRef);
            _cachedTail = tail;
            avail = (int)(tail - head);
            if (avail < len) return false;
        }

        int readIdx = (int)(head & _mask);
        int firstChunk = Math.Min(len, _capacity - readIdx);
        fixed (byte* dp = dest)
        {
            Buffer.MemoryCopy(_base + HeaderSize + readIdx, dp, firstChunk, firstChunk);
            int remainder = len - firstChunk;
            if (remainder > 0)
            {
                Buffer.MemoryCopy(_base + HeaderSize, dp + firstChunk, remainder, remainder);
            }
        }

        Volatile.Write(ref HeadRef, head + len);
        return true;
    }

    /// <summary>
    /// Peek bytes at the current head WITHOUT advancing. Used to read a frame
    /// length prefix before reserving destination buffer. Zero allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeek(Span<byte> dest)
    {
        if (dest.IsEmpty) return true;
        int len = dest.Length;

        long head = Volatile.Read(ref HeadRef);
        long tail = _cachedTail;
        int avail = (int)(tail - head);
        if (avail < len)
        {
            tail = Volatile.Read(ref TailRef);
            _cachedTail = tail;
            avail = (int)(tail - head);
            if (avail < len) return false;
        }

        int readIdx = (int)(head & _mask);
        int firstChunk = Math.Min(len, _capacity - readIdx);
        fixed (byte* dp = dest)
        {
            Buffer.MemoryCopy(_base + HeaderSize + readIdx, dp, firstChunk, firstChunk);
            int remainder = len - firstChunk;
            if (remainder > 0)
            {
                Buffer.MemoryCopy(_base + HeaderSize, dp + firstChunk, remainder, remainder);
            }
        }
        return true;
    }

    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Returned by ref; cannot be readonly.")]
    private ref long HeadRef => ref *(long*)(_base + HeadOffset);
    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Returned by ref; cannot be readonly.")]
    private ref long TailRef => ref *(long*)(_base + TailOffset);

    private static void ValidateCapacity(int capacity)
    {
        if (capacity < 64) throw new ArgumentOutOfRangeException(nameof(capacity), "minimum 64");
        if (capacity > (1 << 30)) throw new ArgumentOutOfRangeException(nameof(capacity), "maximum 1 GiB");
        if ((capacity & (capacity - 1)) != 0) throw new ArgumentException("capacity must be a power of two", nameof(capacity));
    }
}
