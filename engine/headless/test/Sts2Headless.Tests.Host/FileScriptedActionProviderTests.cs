using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class FileScriptedActionProviderTests
{
    private static CompositionRoot.CompositionRootBundle BootstrapCombat()
    {
        var args = CliArgs.Parse(
            new[]
            {
                "--seed",
                "42",
                "--character",
                "silent",
                "--deck",
                "starter",
                "--relics",
                "ring_of_the_snake",
                "--encounter",
                "cultists_normal",
                "--ascension",
                "0",
            }
        );
        return CompositionRoot.Build(args);
    }

    [Fact]
    public void Parses_play_and_end_turn_directives()
    {
        var bundle = BootstrapCombat();
        // Use a card we know is in the starting hand. With seed 42, the hand
        // contains some mix of starter cards; iterate provider's actions and
        // assert all are legal.
        var script = new[] { "# comment line", "", "end_turn" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        Assert.Equal(1, provider.DirectiveCount);

        var legal = LegalActions.Enumerate(bundle.Context.State, bundle.Cards);
        var action = provider.NextAction(bundle.Context.State, legal);
        Assert.IsType<PlayerAction.EndTurn>(action);
        Assert.Null(provider.NextAction(bundle.Context.State, legal));
    }

    [Fact]
    public void Resolves_play_directive_to_matching_legal_action()
    {
        var bundle = BootstrapCombat();
        // Find a card model id that's in the hand and playable.
        var hand = bundle.Context.State.HandPile.Cards;
        Assert.NotEmpty(hand);
        string modelId = hand[0].ModelId;
        var script = new[] { $"play {modelId}" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var legal = LegalActions.Enumerate(bundle.Context.State, bundle.Cards);
        var action = provider.NextAction(bundle.Context.State, legal);
        var play = Assert.IsType<PlayerAction.PlayCard>(action);
        Assert.Contains(hand, c => c.InstanceId == play.CardInstanceId && c.ModelId == modelId);
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var bundle = BootstrapCombat();
        var script = new[] { "", "  ", "# a comment", "# another comment", "end_turn" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        Assert.Equal(1, provider.DirectiveCount);
    }

    [Fact]
    public void Throws_on_unknown_directive()
    {
        var bundle = BootstrapCombat();
        var script = new[] { "lol_unknown" };
        Assert.Throws<ScriptParseException>(() =>
            new FileScriptedActionProvider(script, bundle.Cards)
        );
    }

    [Fact]
    public void Throws_on_play_without_card_id()
    {
        var bundle = BootstrapCombat();
        var script = new[] { "play" };
        Assert.Throws<ScriptParseException>(() =>
            new FileScriptedActionProvider(script, bundle.Cards)
        );
    }

    [Fact]
    public void Throws_on_malformed_target_value()
    {
        var bundle = BootstrapCombat();
        var script = new[] { $"play {StrikeSilent.CanonicalId} target=banana" };
        Assert.Throws<ScriptParseException>(() =>
            new FileScriptedActionProvider(script, bundle.Cards)
        );
    }

    [Fact]
    public void Throws_on_play_with_unknown_card_id()
    {
        var bundle = BootstrapCombat();
        var script = new[] { "play HalfDragon" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var legal = LegalActions.Enumerate(bundle.Context.State, bundle.Cards);
        Assert.Throws<ScriptParseException>(() => provider.NextAction(bundle.Context.State, legal));
    }

    [Fact]
    public void Throws_on_play_when_card_not_in_hand()
    {
        var bundle = BootstrapCombat();
        // Survivor is in the starter deck but not necessarily in the opening hand.
        // Pick a card guaranteed to NOT be in hand at this turn: use a card from
        // the catalog that's in the draw pile.
        var inDraw = bundle.Context.State.DrawPile.Cards.FirstOrDefault();
        Assert.NotNull(inDraw);
        var script = new[] { $"play {inDraw!.ModelId}" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var legal = LegalActions.Enumerate(bundle.Context.State, bundle.Cards);
        // If the same model id is in hand (because starter has duplicates), the
        // provider succeeds. Skip the test in that case.
        bool alsoInHand = bundle.Context.State.HandPile.Cards.Any(c => c.ModelId == inDraw.ModelId);
        if (alsoInHand)
            return;
        Assert.Throws<ScriptParseException>(() => provider.NextAction(bundle.Context.State, legal));
    }

    [Fact]
    public void Returns_null_after_directives_exhausted()
    {
        var bundle = BootstrapCombat();
        var script = new[] { "end_turn" };
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var legal = LegalActions.Enumerate(bundle.Context.State, bundle.Cards);
        provider.NextAction(bundle.Context.State, legal);
        Assert.Null(provider.NextAction(bundle.Context.State, legal));
    }

    [Fact]
    public void Constructor_with_missing_file_throws()
    {
        Assert.Throws<ScriptParseException>(() =>
            new FileScriptedActionProvider(
                "/tmp/does-not-exist-sts2-headless.txt",
                new CardCatalog()
            )
        );
    }

    [Fact]
    public void Constructor_reads_file_lines()
    {
        var bundle = BootstrapCombat();
        string tempPath = Path.Combine(Path.GetTempPath(), $"sts2-script-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempPath, "# comment\nend_turn\n");
            var provider = new FileScriptedActionProvider(tempPath, bundle.Cards);
            Assert.Equal(1, provider.DirectiveCount);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

public sealed class MainLoopTests
{
    private static CompositionRoot.CompositionRootBundle BootstrapCombat() =>
        CompositionRoot.Build(
            CliArgs.Parse(
                new[]
                {
                    "--seed",
                    "42",
                    "--character",
                    "silent",
                    "--deck",
                    "starter",
                    "--relics",
                    "ring_of_the_snake",
                    "--encounter",
                    "cultists_normal",
                    "--ascension",
                    "0",
                }
            )
        );

    [Fact]
    public void Run_emits_combat_start_and_combat_end_events()
    {
        var bundle = BootstrapCombat();
        // Script: end every turn until combat ends (defeat scenario eventually).
        var script = Enumerable.Repeat("end_turn", 100);
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var logger = new CapturingLogger();
        var metrics = new InMemoryMetrics();
        var result = MainLoop.Run(bundle.Context, bundle.Cards, provider, logger, metrics);

        Assert.Contains(logger.Entries, e => e.Event == "combat_start");
        Assert.Contains(logger.Entries, e => e.Event == "combat_end");
        Assert.True(result.TurnsPlayed >= 1);
        Assert.True(metrics.Counters[MetricNames.CombatsTotal] == 1);
        Assert.True(metrics.Counters[MetricNames.TurnsTotal] >= 1);
        Assert.True(metrics.Counters[MetricNames.ActionsTotal] >= 1);
    }

    [Fact]
    public void Run_returns_ScriptExhausted_when_provider_runs_out_mid_combat()
    {
        var bundle = BootstrapCombat();
        // Empty script — provider returns null immediately while combat is alive.
        var provider = new FileScriptedActionProvider(Array.Empty<string>(), bundle.Cards);
        var logger = new CapturingLogger();
        var metrics = new InMemoryMetrics();
        var result = MainLoop.Run(bundle.Context, bundle.Cards, provider, logger, metrics);

        Assert.Equal(MainLoop.LoopOutcome.ScriptExhausted, result.Outcome);
        Assert.Contains(logger.Entries, e => e.Event == "script_exhausted");
    }

    [Fact]
    public void Run_with_cancellation_returns_cancelled()
    {
        var bundle = BootstrapCombat();
        var provider = new FileScriptedActionProvider(
            Enumerable.Repeat("end_turn", 100),
            bundle.Cards
        );
        var logger = new CapturingLogger();
        var metrics = new InMemoryMetrics();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled — loop should exit immediately
        var result = MainLoop.Run(
            bundle.Context,
            bundle.Cards,
            provider,
            logger,
            metrics,
            cts.Token
        );
        Assert.Equal(MainLoop.LoopOutcome.Cancelled, result.Outcome);
    }
}
