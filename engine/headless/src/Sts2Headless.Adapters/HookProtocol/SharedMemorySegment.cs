// SharedMemorySegment — owns a MemoryMappedFile + view + pinned base pointer
// for a single ring buffer. Disposable; releases the view, the file, and any
// owner-side unlink obligations.
//
// Linux backing:
//   We use a real file under /dev/shm (tmpfs) so the peer process can
//   memory-map the same bytes by path. .NET's MemoryMappedFile.CreateNew/
//   CreateFromFile uses POSIX mmap underneath. We pass MemoryMappedFileAccess
//   .ReadWrite for both producer and consumer because the SPSC discipline
//   keeps writes confined to single sides.
//
// Cleanup discipline:
//   - Owner creates with CreateOwner; the file lives under /dev/shm/<name>.
//   - Peer attaches with OpenExisting using the same path.
//   - Owner Dispose deletes the file. Peer Dispose closes its handles only.
//
// Why /dev/shm and not /tmp:
//   /dev/shm is tmpfs (RAM-backed) on every modern Linux, so the latency of
//   the file-system roundtrip is irrelevant — we're just using the path as a
//   global namespace.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// Owns a memory-mapped shared region. Caller obtains the raw <see cref="BasePtr"/>
/// for zero-allocation pointer-typed access (e.g., <see cref="SpscRingBuffer"/>).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed unsafe class SharedMemorySegment : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly string? _ownedPath;
    private byte* _basePtr;
    private int _disposed;

    private SharedMemorySegment(
        MemoryMappedFile mmf,
        MemoryMappedViewAccessor view,
        byte* basePtr,
        string? ownedPath,
        int size
    )
    {
        _mmf = mmf;
        _view = view;
        _basePtr = basePtr;
        _ownedPath = ownedPath;
        Size = size;
    }

    /// <summary>Mapped region size in bytes.</summary>
    public int Size { get; }

    /// <summary>Pinned base pointer to the start of the mapped region.</summary>
    public unsafe byte* BasePtr
    {
        get
        {
            if (System.Threading.Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(SharedMemorySegment));
            return _basePtr;
        }
    }

    /// <summary>
    /// Create a new shared-memory region of <paramref name="totalSize"/> bytes
    /// at <paramref name="path"/> (typically under /dev/shm). Existing files
    /// at the path are deleted first to recover from a crashed previous run.
    /// </summary>
    public static SharedMemorySegment CreateOwner(string path, int totalSize)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (totalSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSize));

        // Clean up stale file from a prior crash. Ignore failures — the
        // CreateFromFile call will fail loudly if there's a real problem.
        try
        {
            File.Delete(path);
        }
        catch
        { /* best effort */
        }

        // Pre-create at the right size so the mmap call can map the whole thing.
        using (
            var fs = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite
            )
        )
        {
            fs.SetLength(totalSize);
        }

        var mmf = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            totalSize,
            MemoryMappedFileAccess.ReadWrite
        );
        var view = mmf.CreateViewAccessor(0, totalSize, MemoryMappedFileAccess.ReadWrite);
        byte* ptr = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        return new SharedMemorySegment(mmf, view, ptr, ownedPath: path, totalSize);
    }

    /// <summary>
    /// Attach to an existing shared-memory region at <paramref name="path"/>.
    /// Used by the peer process; ownership stays with the creator.
    /// </summary>
    public static SharedMemorySegment OpenExisting(string path, int totalSize)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (totalSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSize));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Shared-memory region not found at {path}", path);
        }

        var mmf = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            totalSize,
            MemoryMappedFileAccess.ReadWrite
        );
        var view = mmf.CreateViewAccessor(0, totalSize, MemoryMappedFileAccess.ReadWrite);
        byte* ptr = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        return new SharedMemorySegment(mmf, view, ptr, ownedPath: null, totalSize);
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
        catch
        { /* best effort */
        }
        _view.Dispose();
        _mmf.Dispose();
        if (_ownedPath is not null)
        {
            try
            {
                File.Delete(_ownedPath);
            }
            catch
            { /* best effort */
            }
        }
        _basePtr = null;
    }
}
