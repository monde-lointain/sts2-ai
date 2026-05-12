using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Tools.Fixtures;

/// <summary>
/// Q1→Q2 handoff fixture regression — asserts the 6 fixtures on disk match
/// what Q1's M1 codec produces at HEAD for the recorded (seed, encounter)
/// pairs. Per D3 verification gate:
///
/// <list type="number">
///   <item>Each <c>state.blob</c> file's bytes equal
///         <c>StateBlobFixtureRecipe.ProduceBootBlob(seed, encounter)</c>
///         byte-for-byte.</item>
///   <item>Each <c>metadata.json</c>'s
///         <c>expected_canonical_hash_hex</c> equals
///         <c>CanonicalHash.Sha256Hex</c> over the live-produced bytes.</item>
///   <item>Each <c>metadata.json</c>'s <c>blob_bytes</c> matches the on-disk
///         file size.</item>
/// </list>
///
/// <para>
/// Failure here means either Q1's M1 codec drifted, one of its inputs
/// drifted (catalog id list, encounter monster set, etc.), or someone
/// hand-edited a fixture. Regenerate the fixtures via the generator
/// in <c>StateBlobFixtureGenerator</c> and review the diff carefully — the
/// fixture bytes are the contract handed to Q2.
/// </para>
/// </summary>
public class StateBlobFixtureRegressionTests
{
    public static IEnumerable<object[]> AllSlots =>
        StateBlobFixtureRecipe.AllSlots.Select(s => new object[] { s.DirName, s });

    [Theory]
    [MemberData(nameof(AllSlots))]
    public void Fixture_bytes_reproduce_from_clean_Q1_boot(string dirName, StateBlobFixtureRecipe.Slot slot)
    {
        string fixtureDir = FixtureLocator.StateBlobFixtureDir(slot.DirName);
        string blobPath = Path.Combine(fixtureDir, "state.blob");
        Assert.True(File.Exists(blobPath),
            $"[{dirName}] fixture missing on disk at {blobPath}; run the generator.");

        byte[] onDisk = File.ReadAllBytes(blobPath);
        byte[] reproduced = StateBlobFixtureRecipe.ProduceBootBlob(slot.Seed, slot.EncounterId);

        Assert.True(
            onDisk.AsSpan().SequenceEqual(reproduced),
            $"[{dirName}] state.blob ({onDisk.Length}B on disk) != freshly-produced ({reproduced.Length}B); " +
            $"Q1's M1 codec or its inputs drifted relative to the recorded fixture.");
    }

    [Theory]
    [MemberData(nameof(AllSlots))]
    public void Metadata_canonical_hash_matches_live_M5_CanonicalHash(string dirName, StateBlobFixtureRecipe.Slot slot)
    {
        string fixtureDir = FixtureLocator.StateBlobFixtureDir(slot.DirName);
        string metadataPath = Path.Combine(fixtureDir, "metadata.json");
        Assert.True(File.Exists(metadataPath),
            $"[{dirName}] metadata.json missing at {metadataPath}; run the generator.");

        StateBlobFixtureRecipe.Metadata meta = StateBlobFixtureRecipe.ParseMetadata(File.ReadAllText(metadataPath));
        byte[] reproduced = StateBlobFixtureRecipe.ProduceBootBlob(slot.Seed, slot.EncounterId);
        string liveHash = CanonicalHash.Sha256Hex(reproduced);

        Assert.Equal(meta.ExpectedCanonicalHashHex, liveHash);
        Assert.Equal(meta.BlobBytes, reproduced.Length);
        Assert.Equal(slot.Seed, meta.Seed);
        Assert.Equal(slot.EncounterId, meta.EncounterId);
    }

    [Fact]
    public void Roster_size_is_six()
    {
        Assert.Equal(6, StateBlobFixtureRecipe.AllSlots.Count);
    }

    [Fact]
    public void Roster_slots_are_unique_dir_names()
    {
        var dirs = StateBlobFixtureRecipe.AllSlots.Select(s => s.DirName).ToList();
        Assert.Equal(dirs.Count, dirs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Roster_slot_numbers_are_contiguous_starting_at_one()
    {
        for (int i = 0; i < StateBlobFixtureRecipe.AllSlots.Count; i++)
        {
            Assert.Equal(i + 1, StateBlobFixtureRecipe.AllSlots[i].Number);
        }
    }
}
