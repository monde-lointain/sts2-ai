// Tests for HookRegistry — the per-HookType subscriber registry that fires
// callbacks in deterministic order per Q1-ADR-006:
//   1. explicit priority field, highest-first
//   2. tie-break by registration order
//   3. registration order is itself deterministic via
//      (owner-creature-id, owner-content-id, source-position)
//
// Phase-1 scope: priority + raw-registration-order suffice for unit tests
// here. The (owner-creature-id, owner-content-id, source-position) tuple is
// surfaced via HookRegistration so S5 content can attach those identifiers
// when registering. Tests for the tuple-ordering are in S4-T5 (pinned).

using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Actions;

public class HookRegistryTests
{
    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    private static HookContext NewHookCtx(ExecutionContext ctx) => new(ctx);

    [Fact]
    public void NoSubscribersFireIsNoOp()
    {
        var reg = new HookRegistry();
        var ctx = NewCtx();
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(ctx));
    }

    [Fact]
    public void SubscribeThenFireInvokesHandlerOnce()
    {
        var reg = new HookRegistry();
        int calls = 0;
        reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration((h) => calls++, priority: 0));
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FireHandlersReceiveTheSuppliedHookContext()
    {
        var reg = new HookRegistry();
        var ctx = NewCtx();
        var hookCtx = NewHookCtx(ctx);
        HookContext? captured = null;
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => captured = h, priority: 0)
        );
        reg.Fire(HookType.AfterCardPlayed, hookCtx);
        Assert.NotNull(captured);
        Assert.Same(ctx, captured!.Value.Execution);
    }

    [Fact]
    public void HigherPriorityFiresFirst()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        // Subscribe in low-then-high order; priority must override.
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("low"), priority: 1)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("high"), priority: 10)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("mid"), priority: 5)
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "high", "mid", "low" }, log);
    }

    [Fact]
    public void EqualPriorityFireInRegistrationOrder()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("a"), priority: 0)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("b"), priority: 0)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("c"), priority: 0)
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "a", "b", "c" }, log);
    }

    [Fact]
    public void EqualPriorityEqualOwnerSortByContentId()
    {
        // When priority + ownerCreatureId match, sort by ownerContentId.
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) => log.Add("b"),
                priority: 0,
                ownerCreatureId: 1,
                ownerContentId: 200,
                sourcePosition: 0
            )
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) => log.Add("a"),
                priority: 0,
                ownerCreatureId: 1,
                ownerContentId: 100,
                sourcePosition: 0
            )
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "a", "b" }, log);
    }

    [Fact]
    public void EqualPriorityEqualOwnerEqualContentSortBySourcePosition()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) => log.Add("second"),
                priority: 0,
                ownerCreatureId: 1,
                ownerContentId: 1,
                sourcePosition: 7
            )
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) => log.Add("first"),
                priority: 0,
                ownerCreatureId: 1,
                ownerContentId: 1,
                sourcePosition: 3
            )
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "first", "second" }, log);
    }

    [Fact]
    public void TwoSubscribersWithDifferentOwnerCreatureIdsFireInOwnerOrder()
    {
        // Owner-creature-id is the first tiebreaker after priority.
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("creature-2"), priority: 0, ownerCreatureId: 2)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("creature-1"), priority: 0, ownerCreatureId: 1)
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "creature-1", "creature-2" }, log);
    }

    [Fact]
    public void UnsubscribeRemovesOnlyThatSubscription()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        var h1 = reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("a"), priority: 0)
        );
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("b"), priority: 0)
        );
        reg.Unsubscribe(h1);
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "b" }, log);
    }

    [Fact]
    public void UnsubscribeStaleHandleIsNoOp()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        var h1 = reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("a"), priority: 0)
        );
        reg.Unsubscribe(h1);
        // Second unsubscribe with the same (now stale) handle: no-op, no throw.
        reg.Unsubscribe(h1);
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Empty(log);
    }

    [Fact]
    public void UnsubscribeDefaultHandleIsNoOp()
    {
        var reg = new HookRegistry();
        // default(HookSubscriptionHandle) is "no subscription" — unsubscribing it
        // must not throw and must not affect any existing subscription.
        reg.Unsubscribe(default);
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("a"), priority: 0)
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "a" }, log);
    }

    [Fact]
    public void SameDelegateSubscribedTwiceFiresTwice()
    {
        // Per spec deliverable: subscribing the same handler twice subscribes it
        // twice (matching upstream's multi-listener semantics where the same
        // model can be referenced from multiple lists).
        var reg = new HookRegistry();
        int calls = 0;
        HookHandler handler = (_) => calls++;
        reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration(handler, priority: 0));
        reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration(handler, priority: 0));
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void SubscribeOneHookTypeDoesNotAffectAnother()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration((h) => log.Add("played"), priority: 0)
        );
        reg.Subscribe(
            HookType.AfterDamageReceived,
            new HookRegistration((h) => log.Add("damaged"), priority: 0)
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "played" }, log);
    }

    [Fact]
    public void SubscribeNullRegistrationThrows()
    {
        var reg = new HookRegistry();
        // The struct itself can't be null, but the inner handler can.
        Assert.Throws<System.ArgumentNullException>(() =>
            reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration(null!, priority: 0))
        );
    }

    [Fact]
    public void HookContextProvidesAccessToExecutionContext()
    {
        var ctx = NewCtx();
        var hookCtx = new HookContext(ctx);
        Assert.Same(ctx, hookCtx.Execution);
    }

    [Fact]
    public void SubscriptionsAddedDuringFireDoNotAffectThatFire()
    {
        // From action-queue.md: "an action's resolution triggers a hook that
        // registers another hook; verify new registration takes effect for
        // FUTURE triggers, not the in-progress one."
        var reg = new HookRegistry();
        var log = new List<string>();
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) =>
                {
                    log.Add("first");
                    reg.Subscribe(
                        HookType.AfterCardPlayed,
                        new HookRegistration(
                            (_) => log.Add("second-added-during-fire"),
                            priority: 0
                        )
                    );
                },
                priority: 0
            )
        );
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Equal(new[] { "first" }, log);

        // The second subscription fires on the NEXT trigger.
        log.Clear();
        reg.Fire(HookType.AfterCardPlayed, NewHookCtx(NewCtx()));
        Assert.Contains("first", log);
        Assert.Contains("second-added-during-fire", log);
    }
}
