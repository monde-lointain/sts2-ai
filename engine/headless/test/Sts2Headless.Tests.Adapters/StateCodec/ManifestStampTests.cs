using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Tests for <see cref="ManifestStamp"/> — the (git-sha, build-id, content-hash)
/// triple stamped on every state blob. ContentHash is a SHA-256 of the
/// registered-content id-set; the recipe is the alphabetized list of catalog
/// ids fed through SHA-256.
/// </summary>
public class ManifestStampTests
{
    [Fact]
    public void Equality_is_value_based()
    {
        var a = new ManifestStamp("abc", "build-1", new byte[] { 1, 2, 3 });
        var b = new ManifestStamp("abc", "build-1", new byte[] { 1, 2, 3 });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_distinguishes_content_hash()
    {
        var a = new ManifestStamp("abc", "build-1", new byte[] { 1, 2, 3 });
        var b = new ManifestStamp("abc", "build-1", new byte[] { 1, 2, 4 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_distinguishes_git_sha()
    {
        var a = new ManifestStamp("abc", "build-1", new byte[] { 1, 2, 3 });
        var b = new ManifestStamp("xyz", "build-1", new byte[] { 1, 2, 3 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Empty_strings_are_allowed()
    {
        var stamp = new ManifestStamp("", "", Array.Empty<byte>());
        Assert.Equal("", stamp.GitSha);
        Assert.Equal("", stamp.BuildId);
        Assert.Empty(stamp.ContentHash);
    }

    [Fact]
    public void ContentHashFromIds_alphabetizes_and_hashes()
    {
        // Recipe per spec: sort ids ASCII-ordinal-ascending, join by 0x00,
        // UTF-8 bytes → SHA-256. Same recipe must produce identical hash
        // regardless of input order.
        byte[] hash1 = ManifestStamp.ContentHashFromIds(new[] { "B", "A", "C" });
        byte[] hash2 = ManifestStamp.ContentHashFromIds(new[] { "A", "B", "C" });
        byte[] hash3 = ManifestStamp.ContentHashFromIds(new[] { "C", "B", "A" });
        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
        Assert.Equal(32, hash1.Length);
    }

    [Fact]
    public void ContentHashFromIds_distinguishes_id_sets()
    {
        byte[] hash1 = ManifestStamp.ContentHashFromIds(new[] { "A", "B" });
        byte[] hash2 = ManifestStamp.ContentHashFromIds(new[] { "A", "C" });
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ContentHashFromIds_empty_set_is_stable()
    {
        byte[] hash1 = ManifestStamp.ContentHashFromIds(Array.Empty<string>());
        byte[] hash2 = ManifestStamp.ContentHashFromIds(Array.Empty<string>());
        Assert.Equal(hash1, hash2);
        Assert.Equal(32, hash1.Length);
    }
}
