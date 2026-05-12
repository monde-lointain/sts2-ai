// PosixSemaphore unit tests. Linux-only (the wrapper is [SupportedOSPlatform("linux")]).
//
// What we pin:
//   - Release wakes Wait.
//   - Wait(timeout) returns false on timeout.
//   - Dispose with owner=true unlinks the underlying /dev/shm name so a re-Create succeeds.
//   - Unlink is idempotent on a non-existent name.
//   - sem_open + sem_post + sem_wait roundtrip latency is firmly sub-millisecond.
//
// We use unique names per test so parallel xUnit runs don't collide.

// xUnit1031 fires on Task.Wait / Task.GetAwaiter().GetResult() inside [Fact]
// methods. We use these patterns for bounded test-cleanup ops only (cancelled
// peer task), with explicit timeouts to prevent hangs. The async-tests
// alternative is more idiomatic but obscures the cross-thread coordination
// being exercised. Suppression is file-scoped.
#pragma warning disable xUnit1031

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Sts2Headless.Adapters.HookProtocol;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

[SupportedOSPlatform("linux")]
public class PosixSemaphoreTests
{
    private static string UniqueName([System.Runtime.CompilerServices.CallerMemberName] string member = "")
        => $"q1-test-{member}-{Guid.NewGuid():N}";

    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Fact]
    public void Release_then_Wait_returns_immediately()
    {
        if (!OnLinux) return; // skip on non-Linux harness
        string name = UniqueName();
        PosixSemaphore.Unlink(name);
        using var sem = PosixSemaphore.Create(name);
        sem.Release();
        sem.Wait(); // should not block
    }

    [Fact]
    public void Wait_with_zero_timeout_returns_false_when_unsignaled()
    {
        if (!OnLinux) return;
        string name = UniqueName();
        PosixSemaphore.Unlink(name);
        using var sem = PosixSemaphore.Create(name);
        bool got = sem.Wait(TimeSpan.FromMilliseconds(1));
        Assert.False(got);
    }

    [Fact]
    public async Task Wait_with_timeout_returns_true_after_release()
    {
        if (!OnLinux) return;
        string name = UniqueName();
        PosixSemaphore.Unlink(name);
        using var sem = PosixSemaphore.Create(name);
        var releaseTask = Task.Run(() =>
        {
            Thread.Sleep(50);
            sem.Release();
        });
        bool got = sem.Wait(TimeSpan.FromSeconds(5));
        await releaseTask;
        Assert.True(got);
    }

    [Fact]
    public void Open_after_Create_resolves_same_semaphore()
    {
        if (!OnLinux) return;
        string name = UniqueName();
        PosixSemaphore.Unlink(name);
        using var owner = PosixSemaphore.Create(name);
        using var peer = PosixSemaphore.Open(name);
        // Owner releases; peer wakes.
        owner.Release();
        peer.Wait();
    }

    [Fact]
    public void Owner_Dispose_unlinks_so_recreate_succeeds()
    {
        if (!OnLinux) return;
        string name = UniqueName();
        PosixSemaphore.Unlink(name);
        using (var sem = PosixSemaphore.Create(name)) { /* drop */ }
        // After owner Dispose, name is unlinked; we can Create again with same name.
        using var sem2 = PosixSemaphore.Create(name);
    }

    [Fact]
    public void Unlink_is_idempotent_on_missing_name()
    {
        if (!OnLinux) return;
        string name = UniqueName();
        // Just shouldn't throw.
        PosixSemaphore.Unlink(name);
        PosixSemaphore.Unlink(name);
    }

    [Fact]
    public void Bidirectional_wakeup_within_low_microsecond_budget()
    {
        if (!OnLinux) return;
        string a = UniqueName() + "-a";
        string b = UniqueName() + "-b";
        PosixSemaphore.Unlink(a);
        PosixSemaphore.Unlink(b);
        using var semA = PosixSemaphore.Create(a);
        using var semB = PosixSemaphore.Create(b);

        // Spin up a "peer" that posts B every time it gets A.
        var stop = new CancellationTokenSource();
        var peer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (semA.Wait(TimeSpan.FromMilliseconds(50)))
                {
                    semB.Release();
                }
            }
        });

        try
        {
            // Warm-up: discard first few latencies (JIT / kernel-cache warmup).
            for (int i = 0; i < 16; i++)
            {
                semA.Release();
                semB.Wait();
            }

            // Measure 200 roundtrips; assert p99 well below 1 ms (1000 μs).
            // This is the semaphore-only budget; the full IPC budget is 500 μs
            // for a full message roundtrip, but a single wakeup must comfortably
            // fit within that.
            const int N = 200;
            long[] elapsedTicks = new long[N];
            for (int i = 0; i < N; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                semA.Release();
                semB.Wait();
                long t1 = Stopwatch.GetTimestamp();
                elapsedTicks[i] = t1 - t0;
            }
            Array.Sort(elapsedTicks);
            double tickToMicros = 1_000_000.0 / Stopwatch.Frequency;
            double p99us = elapsedTicks[(int)(N * 0.99)] * tickToMicros;
            Assert.True(p99us < 1000.0, $"p99 wakeup {p99us:F2} μs exceeds 1000 μs budget");
        }
        finally
        {
            stop.Cancel();
            try { peer.GetAwaiter().GetResult(); } catch { /* ok */ }
        }
    }
}
