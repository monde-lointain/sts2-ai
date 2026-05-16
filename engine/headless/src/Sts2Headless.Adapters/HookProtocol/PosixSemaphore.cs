// PosixSemaphore — cross-process named semaphore via libc sem_open/sem_post/
// sem_wait. Used by HookProtocol as the wakeup primitive between Q1 and the
// rollout-worker process.
//
// Why P/Invoke instead of System.Threading.Semaphore:
//   .NET 9's `Semaphore(name)` throws PlatformNotSupportedException on Linux
//   (named primitives are Windows-only in BCL). POSIX sem_open is the standard
//   Linux IPC primitive — same wakeup characteristics, microsecond-class,
//   process-shared by design. Verified empirically before adoption (the env
//   probe in the S9 prep step).
//
// Lifetime:
//   - Owner side: Create(name) -> sem_open(O_CREAT|O_EXCL, mode=0600, init=0)
//     -> on Dispose, sem_close + sem_unlink. The name lives in /dev/shm
//     (e.g., /dev/shm/sem.<name>) for the kernel-namespace duration.
//   - Peer side: Open(name) -> sem_open(0 flags) -> on Dispose, sem_close only;
//     the owner unlinks. This mirrors POSIX shared-memory ownership semantics.
//
// Naming:
//   POSIX requires names start with "/" and contain no other slashes. We
//   normalize by prepending "/" if absent; callers pass plain identifiers.
//
// Wait semantics:
//   - Wait(timeoutMicros): sem_timedwait with CLOCK_REALTIME. Returns true on
//     wakeup, false on timeout. Timeout is computed inside the call to avoid
//     wall-clock drift across the boundary.
//   - Wait() (no arg): sem_wait, blocks until signaled.
//   Note: sem_timedwait uses CLOCK_REALTIME on Linux; clock-drift during the
//   wait can perturb the timeout by < 1 tick. Documented; acceptable for IPC.
//
// This module is process-control, not state-affecting. CLOCK_REALTIME usage is
// for IPC scheduling only and never reaches Domain. Domain's IClock contract
// (S1) is untouched.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sts2Headless.Adapters.HookProtocol;

/// <summary>
/// POSIX named semaphore wrapper. Cross-process wakeup primitive for the
/// HookProtocol IPC. Linux-only (per stage scope: "Linux primary, Windows
/// fallback skipped — latency gate priority").
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PosixSemaphore : IDisposable
{
    // POSIX sem_t pointer. Opaque; libc owns the layout. Treated as IntPtr
    // for marshaling. `SEM_FAILED` is (sem_t*)-1 per <semaphore.h>.
    private static readonly IntPtr SemFailed = new(-1);

    // O_CREAT (0o100) | O_EXCL (0o200). Octal values from <fcntl.h>; we use
    // them directly so this code reads identically against the libc header.
    private const int O_CREAT = 0x40;
    private const int O_EXCL = 0x80;

    // Mode 0o600: owner rw, group/other 0. Sufficient — only the owner and the
    // child process (spawned by the same user) need access.
    private const uint Mode0600 = 0x180;

    private IntPtr _handle;
    private readonly string _name;
    private readonly bool _owner;
    private int _disposed; // 0 = live, 1 = disposed

    private PosixSemaphore(IntPtr handle, string name, bool owner)
    {
        _handle = handle;
        _name = name;
        _owner = owner;
    }

    /// <summary>
    /// Create a new named semaphore. Fails if a semaphore with the same name
    /// already exists (call <see cref="Unlink"/> first if a stale one lingers
    /// from a crashed previous run).
    /// </summary>
    public static PosixSemaphore Create(string name, uint initialValue = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        string posix = NormalizeName(name);
        IntPtr handle = sem_open(posix, O_CREAT | O_EXCL, Mode0600, initialValue);
        if (handle == SemFailed)
        {
            int errno = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"sem_open(O_CREAT|O_EXCL, \"{posix}\") failed: errno={errno} ({StrError(errno)})"
            );
        }
        return new PosixSemaphore(handle, posix, owner: true);
    }

    /// <summary>
    /// Open an existing named semaphore created by another process. Fails if
    /// the name does not exist.
    /// </summary>
    public static PosixSemaphore Open(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string posix = NormalizeName(name);
        // Zero flags = open-existing semantics in POSIX.
        IntPtr handle = sem_open(posix, 0, 0, 0);
        if (handle == SemFailed)
        {
            int errno = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"sem_open(\"{posix}\") failed: errno={errno} ({StrError(errno)})"
            );
        }
        return new PosixSemaphore(handle, posix, owner: false);
    }

    /// <summary>
    /// Unlink a (possibly stale) named semaphore. Idempotent — no-op if absent.
    /// Used during startup to clean up if a previous run crashed without
    /// disposing.
    /// </summary>
    public static void Unlink(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string posix = NormalizeName(name);
        // Ignore errno here — common case is ENOENT (stale-free state).
        _ = sem_unlink(posix);
    }

    /// <summary>
    /// Block until the semaphore is signaled (sem_wait).
    /// </summary>
    public void Wait()
    {
        ThrowIfDisposed();
        int r;
        do
        {
            r = sem_wait(_handle);
        } while (
            r != 0 && Marshal.GetLastPInvokeError() == 4 /* EINTR */
        );
        if (r != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"sem_wait failed: errno={errno} ({StrError(errno)})"
            );
        }
    }

    /// <summary>
    /// Block until the semaphore is signaled or the timeout elapses. Returns
    /// true on signal, false on timeout.
    /// </summary>
    public bool Wait(TimeSpan timeout)
    {
        ThrowIfDisposed();
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        // Compute absolute deadline using CLOCK_REALTIME. sem_timedwait expects
        // an absolute timespec; relative-to-absolute conversion is the caller's
        // job per the man page.
        Timespec abs = default;
        if (
            clock_gettime(
                0 /* CLOCK_REALTIME */
                ,
                ref abs
            ) != 0
        )
        {
            int errno = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"clock_gettime(CLOCK_REALTIME) failed: errno={errno}"
            );
        }
        long addNs = (long)(timeout.TotalMilliseconds * 1_000_000.0);
        abs.tv_sec += addNs / 1_000_000_000;
        abs.tv_nsec += addNs % 1_000_000_000;
        if (abs.tv_nsec >= 1_000_000_000)
        {
            abs.tv_sec += 1;
            abs.tv_nsec -= 1_000_000_000;
        }

        int r;
        do
        {
            r = sem_timedwait(_handle, ref abs);
        } while (
            r != 0 && Marshal.GetLastPInvokeError() == 4 /* EINTR */
        );

        if (r == 0)
            return true;
        int err = Marshal.GetLastPInvokeError();
        if (
            err == 110 /* ETIMEDOUT */
        )
            return false;
        throw new InvalidOperationException($"sem_timedwait failed: errno={err} ({StrError(err)})");
    }

    /// <summary>
    /// Signal the semaphore (sem_post). Wakes one waiter.
    /// </summary>
    public void Release()
    {
        ThrowIfDisposed();
        if (sem_post(_handle) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"sem_post failed: errno={errno} ({StrError(errno)})"
            );
        }
    }

    public void Dispose()
    {
        // Interlocked guard so concurrent Dispose calls don't double-close.
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_handle != IntPtr.Zero && _handle != SemFailed)
        {
            _ = sem_close(_handle);
            _handle = IntPtr.Zero;
        }
        if (_owner)
        {
            _ = sem_unlink(_name);
        }
    }

    private void ThrowIfDisposed()
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PosixSemaphore));
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("name must not be empty", nameof(name));
        if (name[0] != '/')
            name = "/" + name;
        for (int i = 1; i < name.Length; i++)
        {
            if (name[i] == '/')
                throw new ArgumentException(
                    "POSIX semaphore name may contain at most one leading '/'",
                    nameof(name)
                );
        }
        return name;
    }

    private static string StrError(int errno) =>
        Marshal.PtrToStringAnsi(strerror(errno)) ?? errno.ToString();

    // ---- libc P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long tv_sec;
        public long tv_nsec;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr sem_open(
        [MarshalAs(UnmanagedType.LPStr)] string name,
        int oflag,
        uint mode,
        uint value
    );

    [DllImport("libc", SetLastError = true)]
    private static extern int sem_close(IntPtr sem);

    [DllImport("libc", SetLastError = true)]
    private static extern int sem_unlink([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("libc", SetLastError = true)]
    private static extern int sem_wait(IntPtr sem);

    [DllImport("libc", SetLastError = true)]
    private static extern int sem_timedwait(IntPtr sem, ref Timespec abs_timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern int sem_post(IntPtr sem);

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clk_id, ref Timespec tp);

    [DllImport("libc")]
    private static extern IntPtr strerror(int errnum);
}
