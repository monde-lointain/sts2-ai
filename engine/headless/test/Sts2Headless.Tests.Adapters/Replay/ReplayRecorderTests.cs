using System.Buffers.Binary;
using System.Collections.Immutable;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// ReplayRecorder hot-path / lifecycle / threading tests. Verifies:
/// </summary>
/// <list type="bullet">
///   <item>Open writes a well-formed header (magic, schema, stamp, seed)
///   synchronously before returning.</item>
///   <item>AppendStep returns within a microsecond budget (channel-write only).</item>
///   <item>Close terminates the stream with terminator + trailer + SHA-256.</item>
///   <item>Close is idempotent.</item>
///   <item>AppendStep after Close throws.</item>
///   <item>All entries appear in the output (durability after Close).</item>
/// </list>
public class ReplayRecorderTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    private static ManifestStamp MakeStamp() =>
        new("deadbeefcafebabe1234567890abcdef12345678", "Q1-Phase1-test", ZeroHash);

    private static StateCodecFixture PickFixture(int index) =>
        StateCodecFixtures.GenerateAll()[index];

    [Fact]
    public void Open_writes_synchronous_header_to_stream()
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);

        // Header must already be in the stream — we have not appended any
        // steps yet and AppendStep is the only async path.
        Assert.True(ms.Length > 4 + 2 + 4 + 4, "header must be written synchronously on Open");
        ms.Position = 0;
        Span<byte> hdr = stackalloc byte[4];
        int read = ms.Read(hdr);
        Assert.Equal(4, read);
        Assert.Equal(ReplayConstants.HeaderMagic, BinaryPrimitives.ReadUInt32LittleEndian(hdr));

        rec.Close();
    }

    [Fact]
    public void AppendStep_returns_quickly_off_hot_path()
    {
        StateCodecFixture f = PickFixture(0);
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);

        // Budget: under 5 ms per call on a debug build. The intent is to
        // verify that the channel write returns synchronously; we do not
        // measure microseconds (xUnit + Debug build is too noisy). The
        // synchronous path computes a CanonicalHash (one SHA-256 over the
        // CombatState section) which is the dominant cost.
        long start = Environment.TickCount64;
        for (int i = 0; i < 50; i++)
        {
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        }
        long elapsedMs = Environment.TickCount64 - start;
        Assert.True(
            elapsedMs < 500,
            $"50 AppendStep calls took {elapsedMs}ms — should be near-immediate"
        );

        rec.Close();
    }

    [Fact]
    public void Close_writes_terminator_and_trailer()
    {
        StateCodecFixture f = PickFixture(0);
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);
        rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        rec.Close();

        // Last 4+32 bytes are the trailer.
        byte[] all = ms.ToArray();
        Assert.True(
            all.Length >= ReplayConstants.TrailerSizeBytes + 4,
            "stream must include terminator + trailer"
        );

        int trailerOffset = all.Length - ReplayConstants.TrailerSizeBytes;
        uint trailerMagic = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(trailerOffset, 4));
        Assert.Equal(ReplayConstants.TrailerMagic, trailerMagic);

        // The four bytes immediately before the trailer are the entry
        // terminator (u32 = 0xFFFFFFFF).
        uint term = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(trailerOffset - 4, 4));
        Assert.Equal(ReplayConstants.EntryTerminator, term);
    }

    [Fact]
    public void Close_is_idempotent()
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        rec.Close();
        long lenAfterFirstClose = ms.Length;
        rec.Close();
        Assert.Equal(lenAfterFirstClose, ms.Length);
    }

    [Fact]
    public void AppendStep_after_Close_throws()
    {
        StateCodecFixture f = PickFixture(0);
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        rec.Close();
        Assert.Throws<InvalidOperationException>(() =>
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens)
        );
    }

    [Fact]
    public void Open_twice_throws()
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        Assert.Throws<InvalidOperationException>(() =>
            rec.OpenStream(ms, MakeStamp(), initialSeed: 1u)
        );
        rec.Close();
    }

    [Fact]
    public void AppendStep_before_Open_throws()
    {
        StateCodecFixture f = PickFixture(0);
        ReplayRecorder rec = new();
        Assert.Throws<InvalidOperationException>(() =>
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens)
        );
    }

    [Fact]
    public void Many_entries_durable_after_Close()
    {
        StateCodecFixture f = PickFixture(0);
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        for (int i = 0; i < 100; i++)
        {
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        }
        rec.Close();

        // Each entry has: u32 turn + u8 phase + u8 action + u32 size + 0 data + 32 hash = 42 bytes.
        // 100 of those + header + terminator(4) + trailer(36) must be in the stream.
        const int entryBytes = 4 + 1 + 1 + 4 + 0 + 32;
        Assert.True(ms.Length > 100 * entryBytes, "all 100 entries must be persisted");
    }

    [Fact]
    public void Dispose_calls_Close()
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        rec.Dispose();
        // After Dispose, the trailer must be present.
        byte[] all = ms.ToArray();
        int trailerOffset = all.Length - ReplayConstants.TrailerSizeBytes;
        uint trailerMagic = BinaryPrimitives.ReadUInt32LittleEndian(all.AsSpan(trailerOffset, 4));
        Assert.Equal(ReplayConstants.TrailerMagic, trailerMagic);
    }

    [Fact]
    public void Open_writes_file_to_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-test-{Guid.NewGuid():N}.replay");
        try
        {
            ReplayRecorder rec = new();
            rec.Open(path, MakeStamp(), initialSeed: 7u);
            StateCodecFixture f = PickFixture(0);
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
            rec.Close();

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(
                ReplayConstants.HeaderMagic,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4))
            );
            int trailerOffset = bytes.Length - ReplayConstants.TrailerSizeBytes;
            Assert.Equal(
                ReplayConstants.TrailerMagic,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(trailerOffset, 4))
            );
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
