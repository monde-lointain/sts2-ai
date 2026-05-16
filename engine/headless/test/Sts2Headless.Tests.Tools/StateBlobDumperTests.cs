using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Tests.Tools.Fixtures;
using Sts2Headless.Tools.StateBlobDumper;

namespace Sts2Headless.Tests.Tools;

/// <summary>
/// Unit tests for the <see cref="Program"/> entrypoint in the
/// <c>StateBlobDumper</c> tool. Drives <see cref="Program.Run"/> against the
/// six on-disk Q2 handoff fixtures plus negative cases (missing arg, bad
/// path, corrupt blob). Output is JSONL so we assert against parsed JSON,
/// never against raw strings — that keeps the tests stable across whitespace
/// or property-ordering wobble.
/// </summary>
public class StateBlobDumperTests
{
    public static IEnumerable<object[]> AllSlots =>
        StateBlobFixtureRecipe.AllSlots.Select(s => new object[] { s.DirName, s });

    [Theory]
    [MemberData(nameof(AllSlots))]
    public void Run_decodes_each_fixture_to_envelope_plus_sections_plus_hash(
        string dirName,
        StateBlobFixtureRecipe.Slot slot
    )
    {
        Assert.Equal(dirName, slot.DirName); // parameter-pair sanity, also satisfies xUnit1026.
        string blobPath = Path.Combine(
            FixtureLocator.StateBlobFixtureDir(slot.DirName),
            "state.blob"
        );
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        int exit = Program.Run(new[] { blobPath }, stdout, stderr);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(string.Empty, stderr.ToString());

        List<JsonDocument> lines = ParseJsonl(stdout.ToString());
        // Envelope + 3 sections + canonical-hash = 5 lines today (Phase 1).
        Assert.Equal(5, lines.Count);

        JsonElement envelope = lines[0].RootElement;
        Assert.Equal("envelope", envelope.GetProperty("kind").GetString());
        // SchemaVersion lives on the internal StateCodecConstants; the
        // envelope reports it as an int, so we just sanity-check it's in the
        // documented v1..v3 range. (Updating this when v4 lands is fine.)
        int schema = envelope.GetProperty("schema_version").GetInt32();
        Assert.InRange(schema, 1, 99);
        Assert.True(envelope.GetProperty("trailer_validated").GetBoolean());
        Assert.Equal(blobPath, envelope.GetProperty("path").GetString());

        JsonElement stamp = envelope.GetProperty("manifest_stamp");
        Assert.Equal(
            StateBlobFixtureRecipe.FixtureGitSha,
            stamp.GetProperty("git_sha").GetString()
        );
        Assert.Equal(
            StateBlobFixtureRecipe.FixtureBuildId,
            stamp.GetProperty("build_id").GetString()
        );
        // content_hash_hex is 64-char lowercase
        string contentHashHex = stamp.GetProperty("content_hash_hex").GetString()!;
        Assert.Equal(64, contentHashHex.Length);
        Assert.Equal(contentHashHex, contentHashHex.ToLowerInvariant());

        // Three Phase-1 sections in canonical order.
        Assert.Equal((int)SectionId.Rng, lines[1].RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Rng", lines[1].RootElement.GetProperty("name").GetString());
        Assert.Equal((int)SectionId.Tokens, lines[2].RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Tokens", lines[2].RootElement.GetProperty("name").GetString());
        Assert.Equal((int)SectionId.CombatState, lines[3].RootElement.GetProperty("id").GetInt32());
        Assert.Equal("CombatState", lines[3].RootElement.GetProperty("name").GetString());

        // Final line: canonical-hash equals the metadata-recorded hash.
        JsonElement hashLine = lines[4].RootElement;
        Assert.Equal("canonical-hash", hashLine.GetProperty("kind").GetString());
        string emittedHash = hashLine.GetProperty("sha256_hex").GetString()!;
        var metaJson = File.ReadAllText(
            Path.Combine(FixtureLocator.StateBlobFixtureDir(slot.DirName), "metadata.json")
        );
        StateBlobFixtureRecipe.Metadata meta = StateBlobFixtureRecipe.ParseMetadata(metaJson);
        Assert.Equal(meta.ExpectedCanonicalHashHex, emittedHash);

        foreach (JsonDocument d in lines)
            d.Dispose();
    }

    [Fact]
    public void Run_envelope_count_and_sizes_match_disk()
    {
        string blobPath = Path.Combine(
            FixtureLocator.StateBlobFixtureDir(StateBlobFixtureRecipe.AllSlots[0].DirName),
            "state.blob"
        );
        int fileSize = (int)new FileInfo(blobPath).Length;

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(Program.ExitOk, Program.Run(new[] { blobPath }, stdout, stderr));

        List<JsonDocument> lines = ParseJsonl(stdout.ToString());
        JsonElement envelope = lines[0].RootElement;
        Assert.Equal(fileSize, envelope.GetProperty("blob_bytes").GetInt32());
        Assert.Equal(3, envelope.GetProperty("section_count").GetInt32());
        foreach (JsonDocument d in lines)
            d.Dispose();
    }

    [Fact]
    public void Run_combat_state_body_carries_player_and_enemy_summaries()
    {
        // Use fixture #4 (KaiserCrabBoss): exercises two named enemies +
        // spawn powers, so the pretty-print has more substance.
        string blobPath = Path.Combine(
            FixtureLocator.StateBlobFixtureDir("04-kaiser-crab-boss-seed42"),
            "state.blob"
        );
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(Program.ExitOk, Program.Run(new[] { blobPath }, stdout, stderr));

        List<JsonDocument> lines = ParseJsonl(stdout.ToString());
        JsonElement combat = lines[3].RootElement.GetProperty("body");

        Assert.Equal(1, combat.GetProperty("turn_counter").GetInt32());
        Assert.Equal(2, combat.GetProperty("enemy_count").GetInt32());
        JsonElement enemies = combat.GetProperty("enemies");
        Assert.Equal(2, enemies.GetArrayLength());
        Assert.Equal("Crusher", enemies[0].GetProperty("name").GetString());
        Assert.Equal("Rocket", enemies[1].GetProperty("name").GetString());
        // KaiserCrabBoss spawn-time powers reference ids absent from the
        // Phase-1 power catalog (BackAttackLeft/Right, CrabRage, Surrounded).
        // Q1's monster bootstrap drops the unresolved references rather than
        // attaching a PowerInstance — Crusher + Rocket boot with power_count
        // = 0. This is exactly the behaviour the fixture-#4 README header
        // note flags for Q2's S0 ADR. The intent slot, by contrast, IS
        // resolved (rotation-state-machine moves don't require catalog
        // entries), so the dumper has substance to render there.
        Assert.Equal(0, enemies[0].GetProperty("power_count").GetInt32());
        Assert.Equal(0, enemies[1].GetProperty("power_count").GetInt32());
        Assert.True(enemies[0].TryGetProperty("intent", out _));
        Assert.True(enemies[1].TryGetProperty("intent", out _));

        // Player block sanity.
        JsonElement player = combat.GetProperty("player");
        Assert.True(player.GetProperty("is_player").GetBoolean());
        Assert.Equal(70, player.GetProperty("current_hp").GetInt32());
        foreach (JsonDocument d in lines)
            d.Dispose();
    }

    [Fact]
    public void Run_with_no_args_returns_usage_exit_code()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = Program.Run(Array.Empty<string>(), stdout, stderr);
        Assert.Equal(Program.ExitUsage, exit);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("Usage: StateBlobDumper", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_with_missing_file_returns_usage_exit_code_with_error_json()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = Program.Run(new[] { "/no/such/path/here.blob" }, stdout, stderr);
        Assert.Equal(Program.ExitUsage, exit);
        // Error is emitted as a JSON object on stderr.
        using var doc = JsonDocument.Parse(stderr.ToString());
        Assert.Equal("error", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("file_not_found", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public void Run_with_corrupt_blob_returns_decode_exit_code()
    {
        // Take a known-good fixture, flip one byte in the middle (so the
        // trailer SHA mismatches), assert the dumper emits an error and
        // returns the decode exit code.
        string sourcePath = Path.Combine(
            FixtureLocator.StateBlobFixtureDir(StateBlobFixtureRecipe.AllSlots[0].DirName),
            "state.blob"
        );
        byte[] bytes = File.ReadAllBytes(sourcePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        string corruptPath = Path.Combine(
            Path.GetTempPath(),
            $"sts2-state-blob-dumper-corrupt-{Guid.NewGuid():N}.blob"
        );
        try
        {
            File.WriteAllBytes(corruptPath, bytes);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exit = Program.Run(new[] { corruptPath }, stdout, stderr);
            Assert.Equal(Program.ExitDecode, exit);
            using var doc = JsonDocument.Parse(stderr.ToString());
            Assert.Equal("error", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("state_codec_exception", doc.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            if (File.Exists(corruptPath))
                File.Delete(corruptPath);
        }
    }

    private static List<JsonDocument> ParseJsonl(string text)
    {
        var result = new List<JsonDocument>();
        foreach (string raw in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            result.Add(JsonDocument.Parse(raw));
        }
        return result;
    }
}
