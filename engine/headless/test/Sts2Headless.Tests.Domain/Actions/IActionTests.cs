// Tests for the IAction interface. Verifies the contract: Execute receives an
// ExecutionContext and may mutate the queue (sub-action enqueueing) and fire hooks.
// Concrete actions are mocked here; the queue/registry behavior is covered in
// ActionQueueTests and HookRegistryTests.

using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Actions;

public class IActionTests
{
    private sealed class RecordingAction : IAction
    {
        public List<string> Log { get; } = new();
        public string Label { get; }

        public RecordingAction(string label)
        {
            Label = label;
        }

        public void Execute(ExecutionContext ctx) => Log.Add(Label);
    }

    [Fact]
    public void ExecuteIsCalledWithProvidedContext()
    {
        var clock = new LogicalClock();
        var rng = new Rng(0u);
        var ctx = new ExecutionContext(clock, rng, new HookRegistry(), new ActionQueue());

        ExecutionContext? captured = null;
        var action = new CapturingAction(c => captured = c);
        action.Execute(ctx);

        Assert.Same(ctx, captured);
    }

    [Fact]
    public void ActionCanEnqueueDuringExecute()
    {
        // IAction.Execute may mutate the queue — this is how cascading effects work
        // (upstream: actions add follow-up actions during ExecuteAction).
        var clock = new LogicalClock();
        var rng = new Rng(0u);
        var queue = new ActionQueue();
        var ctx = new ExecutionContext(clock, rng, new HookRegistry(), queue);

        var follower = new RecordingAction("follower");
        var enqueueing = new CapturingAction(c => c.Queue.Enqueue(follower));
        enqueueing.Execute(ctx);

        Assert.False(queue.IsEmpty);
    }

    private sealed class CapturingAction : IAction
    {
        private readonly System.Action<ExecutionContext> _fn;

        public CapturingAction(System.Action<ExecutionContext> fn)
        {
            _fn = fn;
        }

        public void Execute(ExecutionContext ctx) => _fn(ctx);
    }
}
