using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class CliArgsTests
{
    private static readonly string[] MinimalValid = new[]
    {
        "--seed", "42",
        "--character", "silent",
        "--deck", "starter",
        "--relics", "ring_of_the_snake",
        "--encounter", "cultists_normal",
        "--ascension", "0",
    };

    [Fact]
    public void Parses_minimal_valid_invocation()
    {
        CliArgs args = CliArgs.Parse(MinimalValid);
        Assert.Equal(42u, args.Seed);
        Assert.Equal("silent", args.Character);
        Assert.Equal("starter", args.Deck);
        Assert.Equal(new[] { "ring_of_the_snake" }, args.Relics);
        Assert.Equal("cultists_normal", args.Encounter);
        Assert.Equal(0, args.Ascension);
        Assert.Null(args.MetricsPort);
        Assert.Null(args.ScriptPath);
        Assert.Null(args.OutPath);
    }

    [Fact]
    public void Parses_all_optional_flags()
    {
        var extended = MinimalValid.Concat(new[]
        {
            "--metrics-port", "9090",
            "--script", "/tmp/script.txt",
            "--out", "/tmp/state.bin",
        }).ToArray();
        CliArgs args = CliArgs.Parse(extended);
        Assert.Equal(9090, args.MetricsPort);
        Assert.Equal("/tmp/script.txt", args.ScriptPath);
        Assert.Equal("/tmp/state.bin", args.OutPath);
    }

    [Fact]
    public void Parses_relic_comma_list_with_multiple_entries()
    {
        var withMultiRelics = MinimalValid.ToArray();
        withMultiRelics[7] = "ring_of_the_snake,anchor,vajra";
        CliArgs args = CliArgs.Parse(withMultiRelics);
        Assert.Equal(new[] { "ring_of_the_snake", "anchor", "vajra" }, args.Relics);
    }

    [Fact]
    public void Parses_equals_value_form()
    {
        var equalsForm = new[]
        {
            "--seed=42",
            "--character=silent",
            "--deck=starter",
            "--relics=ring_of_the_snake",
            "--encounter=cultists_normal",
            "--ascension=0",
        };
        CliArgs args = CliArgs.Parse(equalsForm);
        Assert.Equal(42u, args.Seed);
        Assert.Equal("silent", args.Character);
    }

    [Fact]
    public void Rejects_unknown_flag()
    {
        var withGarbage = MinimalValid.Concat(new[] { "--bogus", "x" }).ToArray();
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(withGarbage));
        Assert.Contains("--bogus", ex.Message);
    }

    [Fact]
    public void Rejects_missing_required_flag()
    {
        var noSeed = MinimalValid.Skip(2).ToArray();
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(noSeed));
        Assert.Contains("--seed", ex.Message);
    }

    [Fact]
    public void Rejects_malformed_seed()
    {
        var bad = MinimalValid.ToArray();
        bad[1] = "not_a_number";
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
        Assert.Contains("--seed", ex.Message);
    }

    [Fact]
    public void Rejects_negative_seed()
    {
        var bad = MinimalValid.ToArray();
        bad[1] = "-1";
        Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
    }

    [Fact]
    public void Rejects_missing_value_for_flag()
    {
        var bad = new[] { "--seed" };
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
        Assert.Contains("--seed", ex.Message);
    }

    [Fact]
    public void Rejects_out_of_range_metrics_port()
    {
        var bad = MinimalValid.Concat(new[] { "--metrics-port", "99999" }).ToArray();
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
        Assert.Contains("--metrics-port", ex.Message);
    }

    [Fact]
    public void Rejects_empty_relics()
    {
        var bad = MinimalValid.ToArray();
        bad[7] = "";
        Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
    }

    [Fact]
    public void Rejects_empty_entry_in_relic_list()
    {
        var bad = MinimalValid.ToArray();
        bad[7] = "ring,,anchor";
        Assert.Throws<CliParseException>(() => CliArgs.Parse(bad));
    }

    [Fact]
    public void Help_flag_throws_help_marker()
    {
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(new[] { "--help" }));
        Assert.True(ex.IsHelp);
        Assert.Contains("Usage", ex.Message);
    }

    [Fact]
    public void Empty_args_reports_first_required_flag_missing()
    {
        var ex = Assert.Throws<CliParseException>(() => CliArgs.Parse(Array.Empty<string>()));
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void Args_array_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => CliArgs.Parse(null!));
    }
}
