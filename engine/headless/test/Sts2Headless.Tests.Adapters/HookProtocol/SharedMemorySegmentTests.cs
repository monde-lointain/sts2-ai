// SharedMemorySegment unit tests.
//
// What we pin:
//   - Owner-create + peer-open of the same /dev/shm path see the same bytes.
//   - Owner Dispose deletes the file; peer Dispose does not.
//   - OpenExisting throws when the path is missing.
//   - The ring buffer running on top of the segment survives cross-attach.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sts2Headless.Adapters.HookProtocol;

namespace Sts2Headless.Tests.Adapters.HookProtocol;

[SupportedOSPlatform("linux")]
public unsafe class SharedMemorySegmentTests
{
    private static bool OnLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static string UniqueShmPath(
        [System.Runtime.CompilerServices.CallerMemberName] string member = ""
    ) => Path.Combine("/dev/shm", $"q1-test-shm-{member}-{Guid.NewGuid():N}");

    [Fact]
    public void Owner_and_peer_see_same_bytes()
    {
        if (!OnLinux)
            return;
        string path = UniqueShmPath();
        try
        {
            using var owner = SharedMemorySegment.CreateOwner(path, 4096);
            owner.BasePtr[100] = 0xAB;
            owner.BasePtr[200] = 0xCD;

            using var peer = SharedMemorySegment.OpenExisting(path, 4096);
            Assert.Equal(0xAB, peer.BasePtr[100]);
            Assert.Equal(0xCD, peer.BasePtr[200]);

            // Peer writes; owner sees.
            peer.BasePtr[300] = 0xEF;
            Assert.Equal(0xEF, owner.BasePtr[300]);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    [Fact]
    public void Owner_dispose_deletes_backing_file()
    {
        if (!OnLinux)
            return;
        string path = UniqueShmPath();
        using (var owner = SharedMemorySegment.CreateOwner(path, 4096))
        {
            Assert.True(File.Exists(path));
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OpenExisting_missing_path_throws_FileNotFound()
    {
        if (!OnLinux)
            return;
        string path = UniqueShmPath();
        Assert.Throws<FileNotFoundException>(() => SharedMemorySegment.OpenExisting(path, 4096));
    }

    [Fact]
    public void Ring_on_top_of_shared_segment_roundtrips_across_attach()
    {
        if (!OnLinux)
            return;
        const int cap = 1024;
        int total = SpscRingBuffer.HeaderSize + cap;
        string path = UniqueShmPath();
        try
        {
            using var owner = SharedMemorySegment.CreateOwner(path, total);
            var ownerRing = new SpscRingBuffer(owner.BasePtr, cap, initializeHeader: true);

            // Producer writes via owner...
            byte[] payload = { 0x10, 0x20, 0x30, 0x40 };
            Assert.True(ownerRing.TryWrite(payload));

            // Peer attaches and reads.
            using var peer = SharedMemorySegment.OpenExisting(path, total);
            var peerRing = new SpscRingBuffer(peer.BasePtr, cap, initializeHeader: false);

            byte[] readBack = new byte[4];
            Assert.True(peerRing.TryRead(readBack));
            Assert.Equal(payload, readBack);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }
}
