// Tests for ActionQueue — the FIFO queue with InsertAtFront overlay used by M6d
// to serialize game effects. Drain ordering matches upstream
// godot/sts2/src/Core/GameActions/Multiplayer/ActionQueueSet.cs: head item is
// returned, executed, removed; sub-actions enqueued during execution go to the
// tail by default. InsertAtFront is a Q1 extension for the rare case where a
// cascading effect must run *before* the next already-queued action.

using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Actions;

public class ActionQueueTests
{
    private sealed class RecordAction : IAction
    {
        public string Label { get; }
        public List<string> Log { get; }
        public RecordAction(string label, List<string> log) { Label = label; Log = log; }
        public void Execute(ExecutionContext ctx) => Log.Add(Label);
    }

    private sealed class EnqueueAction : IAction
    {
        public string Label { get; }
        public List<string> Log { get; }
        public IAction Follow { get; }
        public EnqueueAction(string label, List<string> log, IAction follow) { Label = label; Log = log; Follow = follow; }
        public void Execute(ExecutionContext ctx) { Log.Add(Label); ctx.Queue.Enqueue(Follow); }
    }

    private sealed class InsertFrontAction : IAction
    {
        public string Label { get; }
        public List<string> Log { get; }
        public IAction Follow { get; }
        public InsertFrontAction(string label, List<string> log, IAction follow) { Label = label; Log = log; Follow = follow; }
        public void Execute(ExecutionContext ctx) { Log.Add(Label); ctx.Queue.InsertAtFront(Follow); }
    }

    private static ExecutionContext NewCtx(ActionQueue q)
        => new(new LogicalClock(), new Rng(0u), new HookRegistry(), q);

    [Fact]
    public void NewQueueIsEmpty()
    {
        var q = new ActionQueue();
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void EnqueueMakesNonEmpty()
    {
        var q = new ActionQueue();
        var log = new List<string>();
        q.Enqueue(new RecordAction("a", log));
        Assert.False(q.IsEmpty);
    }

    [Fact]
    public void DrainExecutesFifo()
    {
        var q = new ActionQueue();
        var log = new List<string>();
        q.Enqueue(new RecordAction("a", log));
        q.Enqueue(new RecordAction("b", log));
        q.Enqueue(new RecordAction("c", log));

        q.Drain(NewCtx(q));

        Assert.Equal(new[] { "a", "b", "c" }, log);
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void EmptyDrainIsNoOp()
    {
        var q = new ActionQueue();
        q.Drain(NewCtx(q));
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void SubActionEnqueuedDuringExecuteRunsAfterCurrentTail()
    {
        // Upstream rule: sub-actions enqueued via Enqueue go to the back of the
        // queue (ActionQueueSet appends to the actions list). They execute after
        // anything already queued.
        var q = new ActionQueue();
        var log = new List<string>();
        var c = new RecordAction("c", log);
        var b = new RecordAction("b", log);
        var a = new EnqueueAction("a", log, c); // a runs, then enqueues c
        q.Enqueue(a);
        q.Enqueue(b);

        q.Drain(NewCtx(q));

        // a runs first, queues c. Then b runs (it was queued before c). Then c runs.
        Assert.Equal(new[] { "a", "b", "c" }, log);
    }

    [Fact]
    public void InsertAtFrontPushesToHead()
    {
        // InsertAtFront is the Q1 extension for cascading effects that must run
        // before any already-queued action.
        var q = new ActionQueue();
        var log = new List<string>();
        var c = new RecordAction("c", log);
        var b = new RecordAction("b", log);
        var a = new InsertFrontAction("a", log, c); // a runs, inserts c at front
        q.Enqueue(a);
        q.Enqueue(b);

        q.Drain(NewCtx(q));

        // a runs first, inserts c at front (ahead of b). Then c runs. Then b.
        Assert.Equal(new[] { "a", "c", "b" }, log);
    }

    [Fact]
    public void InsertAtFrontPreservesStabilityAmongExistingHead()
    {
        // If A and B are at the head and we InsertAtFront(X), the new order is
        // [X, A, B] — X pushed in front, A and B's relative order preserved.
        var q = new ActionQueue();
        var log = new List<string>();
        q.Enqueue(new RecordAction("a", log));
        q.Enqueue(new RecordAction("b", log));
        q.InsertAtFront(new RecordAction("x", log));

        q.Drain(NewCtx(q));

        Assert.Equal(new[] { "x", "a", "b" }, log);
    }

    [Fact]
    public void MultipleInsertAtFrontCallsStackInReverseInsertionOrder()
    {
        // Each InsertAtFront pushes to the head. Doing InsertAtFront(x) then
        // InsertAtFront(y) produces drain order [y, x, ...]. This matches
        // intuition: y was inserted most recently, so it executes first.
        var q = new ActionQueue();
        var log = new List<string>();
        q.Enqueue(new RecordAction("z", log));
        q.InsertAtFront(new RecordAction("x", log));
        q.InsertAtFront(new RecordAction("y", log));

        q.Drain(NewCtx(q));

        Assert.Equal(new[] { "y", "x", "z" }, log);
    }

    [Fact]
    public void SubActionInsertedAtFrontDuringExecuteRunsBeforeNextQueuedAction()
    {
        var q = new ActionQueue();
        var log = new List<string>();
        var follower = new RecordAction("follower", log);
        var trigger = new InsertFrontAction("trigger", log, follower);
        var afterTrigger = new RecordAction("after", log);
        q.Enqueue(trigger);
        q.Enqueue(afterTrigger);

        q.Drain(NewCtx(q));

        // trigger runs, inserts follower at front. follower runs next. Then after.
        Assert.Equal(new[] { "trigger", "follower", "after" }, log);
    }

    [Fact]
    public void EnqueueNullThrows()
    {
        var q = new ActionQueue();
        Assert.Throws<System.ArgumentNullException>(() => q.Enqueue(null!));
    }

    [Fact]
    public void InsertAtFrontNullThrows()
    {
        var q = new ActionQueue();
        Assert.Throws<System.ArgumentNullException>(() => q.InsertAtFront(null!));
    }

    [Fact]
    public void DrainWithNullContextThrows()
    {
        var q = new ActionQueue();
        Assert.Throws<System.ArgumentNullException>(() => q.Drain(null!));
    }

    [Fact]
    public void DrainIsReentrantViaSubActionsToFixedPoint()
    {
        // A sub-action enqueueing another sub-action enqueueing another should
        // drain to fixed point in a single Drain call.
        var q = new ActionQueue();
        var log = new List<string>();
        var c = new RecordAction("c", log);
        var b = new EnqueueAction("b", log, c);
        var a = new EnqueueAction("a", log, b);
        q.Enqueue(a);

        q.Drain(NewCtx(q));

        Assert.Equal(new[] { "a", "b", "c" }, log);
        Assert.True(q.IsEmpty);
    }
}
