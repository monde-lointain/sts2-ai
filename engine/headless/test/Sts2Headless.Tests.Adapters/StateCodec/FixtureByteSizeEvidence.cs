using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;
using Xunit.Abstractions;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// Diagnostic: log the on-wire byte count for each fixture so a CI run shows
/// the corpus envelope at a glance (and so reviewers reading the S7 report
/// can sanity-check sizes are non-trivial / non-degenerate).
/// </summary>
public class FixtureByteSizeEvidence
{
    private readonly ITestOutputHelper _out;

    public FixtureByteSizeEvidence(ITestOutputHelper testOutputHelper)
    {
        _out = testOutputHelper;
    }

    [Fact]
    public void Log_byte_sizes_for_each_fixture()
    {
        foreach (StateCodecFixture f in StateCodecFixtures.GenerateAll())
        {
            byte[] bytes = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
                f.State, f.RunRng, f.PlayerRng, f.Tokens, f.Stamp);
            _out.WriteLine($"{f.Name,-46} {bytes.Length,6} bytes");
        }
    }
}
