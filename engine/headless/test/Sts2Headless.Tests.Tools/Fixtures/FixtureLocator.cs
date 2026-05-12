namespace Sts2Headless.Tests.Tools.Fixtures;

/// <summary>
/// Locates the on-disk fixture directory from a test's bin output. Walks the
/// parent chain looking for <c>sts2-headless.sln</c> (the canonical anchor
/// used by every other test in the repo — see
/// <c>test/Sts2Headless.Tests.Domain/Content/SmokeContentTests.cs</c> for the
/// shared pattern).
/// </summary>
public static class FixtureLocator
{
    /// <summary>Repo root (the dir containing <c>sts2-headless.sln</c>).</summary>
    public static string RepoRoot { get; } = LocateRepoRoot();

    /// <summary>Absolute path to <c>test/fixtures/state-blobs/</c>.</summary>
    public static string StateBlobsDir { get; } =
        Path.Combine(RepoRoot, "test", "fixtures", "state-blobs");

    /// <summary>Absolute path to a single fixture subdir.</summary>
    public static string StateBlobFixtureDir(string dirName) =>
        Path.Combine(StateBlobsDir, dirName);

    private static string LocateRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "sts2-headless.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate repo root from " + AppContext.BaseDirectory + "; " +
            "sts2-headless.sln must be in some parent directory.");
    }
}
