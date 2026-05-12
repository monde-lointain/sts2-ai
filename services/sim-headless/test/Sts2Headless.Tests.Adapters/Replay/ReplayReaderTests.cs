using System.Collections.Immutable;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// ReplayReader basic Open/iterate tests. Tamper / schema-mismatch cases
/// live in <c>ReplayTamperTests</c>.
/// </summary>
public class ReplayReaderTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    private static ManifestStamp MakeStamp() =>
        new("deadbeef", "Q1-Phase1-test", ZeroHash);

    private static byte[] RecordSimple(out StateCodecFixture f)
    {
        f = StateCodecFixtures.GenerateAll()[0];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);
        rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        rec.AppendStep(f.State, new PlayerAction.PlayCard(10u, 1u), f.RunRng, f.PlayerRng, f.Tokens);
        rec.AppendStep(f.State, new PlayerAction.PlayCard(20u, null), f.RunRng, f.PlayerRng, f.Tokens);
        rec.Close();
        return ms.ToArray();
    }

    [Fact]
    public void Decode_returns_blob_with_validated_trailer()
    {
        byte[] bytes = RecordSimple(out _);
        ReplayBlob blob = ReplayReader.Decode(bytes);
        Assert.True(blob.TrailerValidated);
        Assert.Equal((ushort)1, blob.SchemaVersion);
        Assert.Equal(42u, blob.InitialSeed);
    }

    [Fact]
    public void Decode_preserves_manifest_stamp()
    {
        ManifestStamp stamp = new("sha-éπ", "build-世界", ZeroHash);
        StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, stamp, initialSeed: 0u);
        rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        rec.Close();

        ReplayBlob blob = ReplayReader.Decode(ms.ToArray());
        Assert.Equal(stamp, blob.ManifestStamp);
    }

    [Fact]
    public void Decode_returns_entries_in_order()
    {
        byte[] bytes = RecordSimple(out StateCodecFixture f);
        ReplayBlob blob = ReplayReader.Decode(bytes);
        Assert.Equal(3, blob.Entries.Count);

        // Entry 0: EndTurn.
        Assert.Equal((uint)f.State.TurnCounter, blob.Entries[0].TurnNo);
        Assert.Equal(f.State.Phase, blob.Entries[0].Phase);
        Assert.Equal(ReplayActionType.EndTurn, blob.Entries[0].ActionType);
        Assert.Empty(blob.Entries[0].ActionData);

        // Entry 1: PlayCard with target.
        Assert.Equal(ReplayActionType.PlayCard, blob.Entries[1].ActionType);
        PlayerAction a1 = ReplayActionCodec.Decode(blob.Entries[1].ActionType, blob.Entries[1].ActionData);
        var pc1 = Assert.IsType<PlayerAction.PlayCard>(a1);
        Assert.Equal(10u, pc1.CardInstanceId);
        Assert.Equal(1u, pc1.TargetEnemyId);

        // Entry 2: PlayCard without target.
        PlayerAction a2 = ReplayActionCodec.Decode(blob.Entries[2].ActionType, blob.Entries[2].ActionData);
        var pc2 = Assert.IsType<PlayerAction.PlayCard>(a2);
        Assert.Equal(20u, pc2.CardInstanceId);
        Assert.Null(pc2.TargetEnemyId);
    }

    [Fact]
    public void Decode_post_hash_is_32_bytes_per_entry()
    {
        byte[] bytes = RecordSimple(out _);
        ReplayBlob blob = ReplayReader.Decode(bytes);
        foreach (ReplayEntry e in blob.Entries)
        {
            Assert.Equal(32, e.PostHash.Length);
        }
    }

    [Fact]
    public void Decode_zero_entries_yields_empty_list()
    {
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        rec.Close();

        ReplayBlob blob = ReplayReader.Decode(ms.ToArray());
        Assert.Empty(blob.Entries);
    }

    [Fact]
    public void Open_reads_from_disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"replay-reader-{Guid.NewGuid():N}.replay");
        try
        {
            StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
            ReplayRecorder rec = new();
            rec.Open(path, MakeStamp(), initialSeed: 7u);
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
            rec.Close();

            ReplayBlob blob = ReplayReader.Open(path);
            Assert.Equal(7u, blob.InitialSeed);
            Assert.Single(blob.Entries);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
