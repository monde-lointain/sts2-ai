namespace Sts2Headless.Tests.Tools.Fixtures;

/// <summary>
/// Pins the two verbatim README header notes mandated by the D3 fixture
/// roster (project-lead approved): #4 KaiserCrabBoss and #6 SmallSlimes.
///
/// <para>
/// <b>Why a test, not just a code review check:</b> the D3 spec says the
/// notes must match VERBATIM, and a future edit to the README could silently
/// reword them. Pinning to a string-contains check inside the test suite
/// makes any drift surface as a CI failure rather than a quiet review miss.
/// </para>
/// </summary>
public class FixtureReadmeContractTests
{
    private const string Fixture4HeaderNote =
        "Phase-1 KaiserCrabBoss spawn-time powers (BackAttackLeft/Right, CrabRage, Surrounded) reference power IDs absent from the Phase-1 power catalog; Q2 adapter must define unknown-power-reference behavior — surface in Q2 S0 ADRs.";

    private const string Fixture6HeaderNote =
        "Encounter cannot run end-to-end in Q1 Phase-1A — encounter-RNG plumbing deferred to B.1-ε. Initial-state only; stresses Q2 MissingUpstream path.";

    [Fact]
    public void Readme_carries_KaiserCrabBoss_verbatim_header_note()
    {
        string readme = ReadReadme();
        Assert.Contains(Fixture4HeaderNote, readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_carries_SmallSlimes_verbatim_header_note()
    {
        string readme = ReadReadme();
        Assert.Contains(Fixture6HeaderNote, readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_exists_at_corpus_root()
    {
        string path = Path.Combine(FixtureLocator.StateBlobsDir, "README.md");
        Assert.True(File.Exists(path), $"Corpus README missing at {path}.");
    }

    private static string ReadReadme()
    {
        string path = Path.Combine(FixtureLocator.StateBlobsDir, "README.md");
        return File.ReadAllText(path);
    }
}
