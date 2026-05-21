using System.Text.Json;
using Sts2Headless.Adapters.Replay;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.Replay;

/// <summary>
/// Verify the JSON debug dumper is field-for-field equivalent to the
/// binary reader for representative fixtures. Required by S10 stage prompt
/// validation gate #7 ("JSON debug dumper: tests verify field-for-field
/// equivalence to binary ... for at least 3 representative fixtures").
/// </summary>
public class ReplayDebugDumperTests
{
    private static readonly byte[] ZeroHash = new byte[32];

    private static ManifestStamp MakeStamp() => new("deadbeef", "Q1-Phase1-dumper", ZeroHash);

    private static byte[] RecordFor(int fixtureIndex, params PlayerAction[] actions)
    {
        StateCodecFixture f = StateCodecFixtures.GenerateAll()[fixtureIndex];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 42u);
        foreach (PlayerAction a in actions)
        {
            rec.AppendStep(f.State, a, f.RunRng, f.PlayerRng, f.Tokens);
        }
        rec.Close();
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Dumper_DTO_matches_binary_reader_per_fixture(int fixtureIndex)
    {
        byte[] bytes = RecordFor(
            fixtureIndex,
            PlayerAction.EndTurn.Instance,
            new PlayerAction.PlayCard(10u, new global::Sts2Headless.Domain.Combat.CreatureId(1u)),
            new PlayerAction.PlayCard(20u, null)
        );

        ReplayBlob blob = ReplayReader.Decode(bytes);
        ReplayDumpDto dto = ReplayDebugDumper.ToDto(blob);

        // Field-for-field equivalence with the binary reader.
        Assert.Equal(blob.SchemaVersion, dto.SchemaVersion);
        Assert.Equal(blob.InitialSeed, dto.InitialSeed);
        Assert.Equal(blob.TrailerValidated, dto.TrailerValidated);
        Assert.Equal(blob.ManifestStamp.GitSha, dto.ManifestStamp.GitSha);
        Assert.Equal(blob.ManifestStamp.BuildId, dto.ManifestStamp.BuildId);
        Assert.Equal(
            Convert.ToHexStringLower(blob.ManifestStamp.ContentHash),
            dto.ManifestStamp.ContentHashHex
        );

        Assert.Equal(blob.Entries.Count, dto.Entries.Length);
        for (int i = 0; i < blob.Entries.Count; i++)
        {
            ReplayEntry e = blob.Entries[i];
            EntryDto ed = dto.Entries[i];
            Assert.Equal(e.TurnNo, ed.TurnNo);
            Assert.Equal(e.Phase.ToString(), ed.Phase);
            Assert.Equal(e.ActionType.ToString(), ed.ActionType);
            Assert.Equal(Convert.ToHexStringLower(e.ActionData), ed.ActionDataHex);
            Assert.Equal(Convert.ToHexStringLower(e.PostHash), ed.PostHashHex);
        }
    }

    [Fact]
    public void ToJsonString_returns_valid_indented_JSON()
    {
        byte[] bytes = RecordFor(0, PlayerAction.EndTurn.Instance);
        ReplayBlob blob = ReplayReader.Decode(bytes);
        string json = ReplayDebugDumper.ToJsonString(blob);

        // Verify parseable JSON. We don't pin specific field names beyond
        // what's in DTO (System.Text.Json uses PascalCase by default,
        // matching DTO property names).
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("SchemaVersion", out _));
        Assert.True(root.TryGetProperty("ManifestStamp", out _));
        Assert.True(root.TryGetProperty("InitialSeed", out _));
        Assert.True(root.TryGetProperty("Entries", out _));
        Assert.True(root.TryGetProperty("TrailerValidated", out _));
    }

    [Fact]
    public void WriteJson_emits_file_to_disk()
    {
        string replayPath = Path.Combine(Path.GetTempPath(), $"rdump-{Guid.NewGuid():N}.replay");
        string jsonPath = Path.Combine(Path.GetTempPath(), $"rdump-{Guid.NewGuid():N}.json");
        try
        {
            StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
            ReplayRecorder rec = new();
            rec.Open(replayPath, MakeStamp(), initialSeed: 7u);
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
            rec.AppendStep(
                f.State,
                new PlayerAction.PlayCard(99u, new global::Sts2Headless.Domain.Combat.CreatureId(5u)),
                f.RunRng,
                f.PlayerRng,
                f.Tokens
            );
            rec.Close();

            ReplayDebugDumper.WriteJson(replayPath, jsonPath);

            Assert.True(File.Exists(jsonPath));
            string contents = File.ReadAllText(jsonPath);
            using JsonDocument doc = JsonDocument.Parse(contents);
            int entryCount = doc.RootElement.GetProperty("Entries").GetArrayLength();
            Assert.Equal(2, entryCount);
        }
        finally
        {
            if (File.Exists(replayPath))
                File.Delete(replayPath);
            if (File.Exists(jsonPath))
                File.Delete(jsonPath);
        }
    }

    [Fact]
    public void Dump_entry_counts_match_recorded_steps()
    {
        // Pin "field-for-field equivalence" against the bare recorder: the
        // entry count in the JSON must equal the number of AppendStep calls.
        int recorded = 7;
        StateCodecFixture f = StateCodecFixtures.GenerateAll()[0];
        using MemoryStream ms = new();
        ReplayRecorder rec = new();
        rec.OpenStream(ms, MakeStamp(), initialSeed: 0u);
        for (int i = 0; i < recorded; i++)
        {
            rec.AppendStep(f.State, PlayerAction.EndTurn.Instance, f.RunRng, f.PlayerRng, f.Tokens);
        }
        rec.Close();

        ReplayBlob blob = ReplayReader.Decode(ms.ToArray());
        ReplayDumpDto dto = ReplayDebugDumper.ToDto(blob);
        Assert.Equal(recorded, dto.Entries.Length);
    }

    [Fact]
    public void Dump_action_types_match_recorded_actions()
    {
        byte[] bytes = RecordFor(
            0,
            PlayerAction.EndTurn.Instance,
            new PlayerAction.PlayCard(10u, new global::Sts2Headless.Domain.Combat.CreatureId(1u)),
            new PlayerAction.PlayCard(20u, null)
        );

        ReplayBlob blob = ReplayReader.Decode(bytes);
        ReplayDumpDto dto = ReplayDebugDumper.ToDto(blob);

        Assert.Equal("EndTurn", dto.Entries[0].ActionType);
        Assert.Equal("PlayCard", dto.Entries[1].ActionType);
        Assert.Equal("PlayCard", dto.Entries[2].ActionType);
        // Empty action data renders as empty hex string.
        Assert.Equal("", dto.Entries[0].ActionDataHex);
        // PlayCard with target = 9 bytes hex = 18 chars.
        Assert.Equal(18, dto.Entries[1].ActionDataHex.Length);
        // PlayCard without target = 5 bytes hex = 10 chars.
        Assert.Equal(10, dto.Entries[2].ActionDataHex.Length);
    }
}
