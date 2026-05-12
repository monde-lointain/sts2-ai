using Sts2Headless.Host;

namespace Sts2Headless.Tests.Host;

public sealed class GracefulShutdownTests
{
    [Fact]
    public void Token_initially_not_cancelled()
    {
        using var gs = new GracefulShutdown(attachProcessSignals: false);
        Assert.False(gs.Token.IsCancellationRequested);
        Assert.False(gs.IsShutdownRequested);
    }

    [Fact]
    public void Trigger_cancels_token()
    {
        using var gs = new GracefulShutdown(attachProcessSignals: false);
        gs.Trigger("test");
        Assert.True(gs.Token.IsCancellationRequested);
        Assert.True(gs.IsShutdownRequested);
        Assert.Equal("test", gs.TriggerReason);
    }

    [Fact]
    public void Trigger_is_idempotent_and_first_reason_wins()
    {
        using var gs = new GracefulShutdown(attachProcessSignals: false);
        gs.Trigger("first");
        gs.Trigger("second");
        Assert.Equal("first", gs.TriggerReason);
    }

    [Fact]
    public void Dispose_does_not_throw_after_trigger()
    {
        var gs = new GracefulShutdown(attachProcessSignals: false);
        gs.Trigger();
        gs.Dispose();
        gs.Dispose(); // idempotent
    }

    [Fact]
    public void MainLoop_observes_token_and_exits_with_Cancelled_outcome()
    {
        var bundle = CompositionRoot.Build(CliArgs.Parse(new[]
        {
            "--seed", "42",
            "--character", "silent",
            "--deck", "starter",
            "--relics", "ring_of_the_snake",
            "--encounter", "cultists_normal",
            "--ascension", "0",
        }));
        var script = Enumerable.Repeat("end_turn", 1000);
        var provider = new FileScriptedActionProvider(script, bundle.Cards);
        var logger = new CapturingLogger();
        var metrics = new InMemoryMetrics();
        using var gs = new GracefulShutdown(attachProcessSignals: false);

        // Pre-trigger so the loop's first cancellation check trips.
        gs.Trigger("test");

        var result = MainLoop.Run(bundle.Context, bundle.Cards, provider, logger, metrics, gs.Token);
        Assert.Equal(MainLoop.LoopOutcome.Cancelled, result.Outcome);
        Assert.Contains(logger.Entries, e => e.Event == "shutdown");
    }
}
