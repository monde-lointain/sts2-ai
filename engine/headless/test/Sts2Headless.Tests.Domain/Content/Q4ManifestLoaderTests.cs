using System.IO;
using System.Reflection;
using Sts2Headless.Domain.Content;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// Q4 manifest loader tests. The domain parser takes a JSON <see cref="string"/> only —
/// file IO is performed in tests (banned in the Domain assembly per the determinism
/// analyzer) and in production by M9 Host (S8).
/// </summary>
public class Q4ManifestLoaderTests
{
    private const string ValidEmptyManifest =
        @"{
            ""cards"":    [],
            ""relics"":   [],
            ""powers"":   [],
            ""monsters"": [],
            ""potions"":  []
        }";

    [Fact]
    public void LoadFromString_returns_all_empty_lists_for_empty_manifest()
    {
        Q4Manifest manifest = Q4ManifestLoader.LoadFromString(ValidEmptyManifest);

        Assert.Empty(manifest.Cards);
        Assert.Empty(manifest.Relics);
        Assert.Empty(manifest.Powers);
        Assert.Empty(manifest.Monsters);
        Assert.Empty(manifest.Potions);
    }

    [Fact]
    public void LoadFromString_preserves_order_of_ids_inside_bucket()
    {
        const string json =
            @"{
            ""cards"":    [""strike"", ""defend"", ""bash""],
            ""relics"":   [],
            ""powers"":   [],
            ""monsters"": [],
            ""potions"":  []
        }";

        Q4Manifest manifest = Q4ManifestLoader.LoadFromString(json);

        Assert.Equal(new[] { "strike", "defend", "bash" }, manifest.Cards);
    }

    [Fact]
    public void LoadFromString_throws_for_null()
    {
        Assert.Throws<ArgumentNullException>(() => Q4ManifestLoader.LoadFromString(null!));
    }

    [Fact]
    public void LoadFromString_throws_for_malformed_json()
    {
        const string malformed = "{ \"cards\": [ "; // unterminated
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(malformed)
        );
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void LoadFromString_throws_when_root_is_not_an_object()
    {
        const string arrayRoot = "[]";
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(arrayRoot)
        );
        Assert.Contains("root must be a JSON object", ex.Message);
    }

    [Fact]
    public void LoadFromString_throws_for_unknown_root_key()
    {
        // Catches typos like "monster" (singular) vs "monsters". Without this guard the
        // bucket would silently default to [] and the coverage gate would lie.
        const string json =
            @"{
            ""cards"":    [],
            ""relics"":   [],
            ""powers"":   [],
            ""monsters"": [],
            ""potions"":  [],
            ""bogus"":    []
        }";
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(json)
        );
        Assert.Contains("'bogus'", ex.Message);
    }

    [Fact]
    public void LoadFromString_throws_for_missing_required_key()
    {
        // "monsters" missing.
        const string json =
            @"{
            ""cards"":   [],
            ""relics"":  [],
            ""powers"":  [],
            ""potions"": []
        }";
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(json)
        );
        Assert.Contains("'monsters'", ex.Message);
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void LoadFromString_throws_when_bucket_is_not_an_array()
    {
        const string json =
            @"{
            ""cards"":    {},
            ""relics"":   [],
            ""powers"":   [],
            ""monsters"": [],
            ""potions"":  []
        }";
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(json)
        );
        Assert.Contains("'cards'", ex.Message);
        Assert.Contains("must be an array", ex.Message);
    }

    [Fact]
    public void LoadFromString_throws_when_bucket_entry_is_not_a_string()
    {
        const string json =
            @"{
            ""cards"":    [""strike"", 42, ""defend""],
            ""relics"":   [],
            ""powers"":   [],
            ""monsters"": [],
            ""potions"":  []
        }";
        Q4ManifestFormatException ex = Assert.Throws<Q4ManifestFormatException>(() =>
            Q4ManifestLoader.LoadFromString(json)
        );
        Assert.Contains("'cards'", ex.Message);
        Assert.Contains("[1]", ex.Message);
        Assert.Contains("must be a string", ex.Message);
    }

    [Fact]
    public void LoadFromString_loads_phase1_fixture_from_repo_root()
    {
        // Round-trip the on-disk fixture (test/fixtures/q4-manifest-phase1.json at repo
        // root). This is the contract M9 Host (S8) will use in production: locate the
        // manifest file relative to the running process, read it, hand the bytes to the
        // domain loader. The domain loader itself never touches the filesystem.
        string repoRoot = LocateRepoRoot();
        string manifestPath = Path.Combine(repoRoot, "test", "fixtures", "q4-manifest-phase1.json");
        Assert.True(File.Exists(manifestPath), $"Fixture not found at {manifestPath}");

        string json = File.ReadAllText(manifestPath);
        Q4Manifest manifest = Q4ManifestLoader.LoadFromString(json);

        // Phase-1 fixture lists smoke set + S12 expansion. As later S12 commits land,
        // each bucket grows toward its full Phase-1 target. T1 lands cards; bucket sizes
        // assert lower bounds (so the test stays green as relics/powers/etc. follow).
        Assert.True(manifest.Cards.Count >= 9, $"cards >=9, got {manifest.Cards.Count}");
        Assert.True(manifest.Relics.Count >= 5, $"relics >=5, got {manifest.Relics.Count}");
        Assert.True(manifest.Powers.Count >= 5, $"powers >=5, got {manifest.Powers.Count}");
        Assert.True(manifest.Monsters.Count >= 2, $"monsters >=2, got {manifest.Monsters.Count}");
        // Potions bucket: empty in T1-T4, populated in T5.
    }

    [Fact]
    public void Q4Manifest_Empty_is_a_valid_singleton_with_no_ids()
    {
        Assert.Empty(Q4Manifest.Empty.Cards);
        Assert.Empty(Q4Manifest.Empty.Relics);
        Assert.Empty(Q4Manifest.Empty.Powers);
        Assert.Empty(Q4Manifest.Empty.Monsters);
        Assert.Empty(Q4Manifest.Empty.Potions);
    }

    /// <summary>
    /// Walk up from the test assembly directory until we find a directory that contains
    /// <c>sts2-headless.sln</c>. Used here to locate the repo-root fixture file. Tests
    /// (unlike Domain) are not subject to the banned-API analyzer so System.IO is fine.
    /// </summary>
    private static string LocateRepoRoot()
    {
        string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "sts2-headless.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate repo root (no sts2-headless.sln above the test assembly)."
        );
    }
}
