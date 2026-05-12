using System.Buffers.Binary;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// Hard contracts on the reader's failure paths. Every recoverable error
/// path must throw <see cref="ReplayException"/>.
/// </summary>
public class ReplayTamperTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    private static ManifestStamp MakeStamp() =>
        new("deadbeef", "Q1-Phase1-test", ZeroHash);

    private static byte[] RecordTwoSteps()
    {
        StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);
        rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        rec.AppendStep(f.State, new PlayerAction.PlayCard(20u, null), f.RunRng, f.PlayerRng, f.Tokens);
        rec.Close();
        return ms.ToArray();
    }

    // ============================================================
    // Trailer tamper
    // ============================================================

    [Fact]
    public void Tamper_trailer_magic_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        // Last 36 bytes = trailer = u32 magic + 32-byte sha. Magic is at offset bytes.Length-36..-32.
        int magicOffset = bytes.Length - ReplayConstants.TrailerSizeBytes;
        bytes[magicOffset] ^= 0xFF;
        ReplayException ex = Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
        Assert.Contains("trailer magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tamper_trailer_sha_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        // The SHA-256 sits in the last 32 bytes — flip a byte in the middle.
        bytes[bytes.Length - 10] ^= 0xFF;
        ReplayException ex = Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
        Assert.Contains("hash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trailer_size_short_blob_throws_ReplayException()
    {
        // 35 bytes = under TrailerSizeBytes; reader must reject.
        Assert.Throws<ReplayException>(() => ReplayReader.Decode(new byte[35]));
    }

    // ============================================================
    // Entry tamper → trailer SHA mismatch
    // ============================================================

    [Fact]
    public void Tamper_an_entry_byte_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        // Find a middle byte (well inside the entries region). Flip it; the
        // trailer SHA won't match anymore.
        int mid = bytes.Length / 2;
        bytes[mid] ^= 0xFF;
        ReplayException ex = Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
        Assert.Contains("hash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tamper_initial_seed_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        // Header layout: magic(4) + schema(2) + manifest_size(4) + manifest_bytes + initial_seed(4).
        // We flip a byte known to be inside the manifest region (offset 12 = first byte of the manifest body).
        bytes[12] ^= 0xFF;
        Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
    }

    // ============================================================
    // Schema mismatch
    // ============================================================

    [Fact]
    public void Schema_99_blob_rejected_with_clear_message()
    {
        // Build a synthetic blob with schema=99 but a valid trailer SHA.
        // Easiest path: record a valid blob, change schema bytes, recompute trailer SHA.
        byte[] bytes = RecordTwoSteps();
        // Schema sits at offset 4..6 (after header magic).
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 99);
        // Recompute trailer SHA over the new body.
        int bodyLen = bytes.Length - ReplayConstants.TrailerSizeBytes;
        Span<byte> newHash = stackalloc byte[ReplayConstants.Sha256ByteLength];
        System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, bodyLen), newHash);
        newHash.CopyTo(bytes.AsSpan(bodyLen + 4, ReplayConstants.Sha256ByteLength));

        ReplayException ex = Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("99", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bad_header_magic_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0xDEADBEEFu);
        // Recompute the trailer SHA so we trigger the magic check, not the trailer check.
        int bodyLen = bytes.Length - ReplayConstants.TrailerSizeBytes;
        Span<byte> newHash = stackalloc byte[ReplayConstants.Sha256ByteLength];
        System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, bodyLen), newHash);
        newHash.CopyTo(bytes.AsSpan(bodyLen + 4, ReplayConstants.Sha256ByteLength));

        ReplayException ex = Assert.Throws<ReplayException>(() => ReplayReader.Decode(bytes));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // Truncation
    // ============================================================

    [Fact]
    public void Truncated_blob_throws_ReplayException()
    {
        byte[] bytes = RecordTwoSteps();
        // Lop off the last 20 bytes — partly trailer, partly trailing entry.
        byte[] truncated = bytes.AsSpan(0, bytes.Length - 20).ToArray();
        Assert.Throws<ReplayException>(() => ReplayReader.Decode(truncated));
    }

    // ============================================================
    // Empty input
    // ============================================================

    [Fact]
    public void Empty_bytes_throws_ReplayException()
    {
        Assert.Throws<ReplayException>(() => ReplayReader.Decode(ReadOnlySpan<byte>.Empty));
    }
}
