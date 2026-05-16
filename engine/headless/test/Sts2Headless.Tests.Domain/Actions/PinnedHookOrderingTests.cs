// PINNED MULTI-HOOK ORDERING TESTS — CI HARD GATE (Q1-ADR-006, R5).
//
// Per S4 spec and docs/specs/modules/action-queue.md:
//   "5+ scenarios derived from upstream code paths. Each test asserts post-
//   state matches an upstream golden value. The test method comment cites the
//   upstream file + region you derived the scenario from. These tests must
//   run on every dotnet test — no skip attributes, no environment guards."
//
// The "post-state" each scenario produces is a deterministic sequence of
// string labels representing the order in which subscriber handlers fired.
// Each scenario maps directly to a specific code path in upstream
// godot/sts2/src/Core/Hooks/Hook.cs; the golden sequence is the order in
// which upstream would invoke listener methods for that scenario.
//
// These are CI-gate tests; no skip attributes, no environment guards.

using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Actions;

public class PinnedHookOrderingTests
{
    private static HookContext NewHookCtx()
    {
        var ctx = new ExecutionContext(
            new LogicalClock(),
            new Rng(0u),
            new HookRegistry(),
            new ActionQueue()
        );
        return new HookContext(ctx);
    }

    /// <summary>
    /// SCENARIO 1 — AfterPlayerTurnStart tri-pass.
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 643-666.
    ///
    ///     public static async Task AfterPlayerTurnStart(...)
    ///     {
    ///         foreach (model in combatState.IterateHookListeners())
    ///             await model.AfterPlayerTurnStartEarly(...);   // pass 1
    ///         foreach (model in combatState.IterateHookListeners())
    ///             await model.AfterPlayerTurnStart(...);        // pass 2
    ///         foreach (model in combatState.IterateHookListeners())
    ///             await model.AfterPlayerTurnStartLate(...);    // pass 3
    ///     }
    ///
    /// Three independent passes, each iterating the full listener set. A
    /// listener that subscribes to all three sees its Early call before its
    /// Normal call before its Late call, but importantly: every listener's
    /// Early runs before any listener's Normal.
    ///
    /// Golden: ["A-Early", "B-Early", "A-Normal", "B-Normal", "A-Late", "B-Late"]
    /// — provided A registers before B at each hook type.
    /// </summary>
    [Fact]
    public void Scenario1_AfterPlayerTurnStart_TriPassOrdering()
    {
        var reg = new HookRegistry();
        var log = new List<string>();

        reg.Subscribe(
            HookType.AfterPlayerTurnStartEarly,
            new HookRegistration((_) => log.Add("A-Early"))
        );
        reg.Subscribe(
            HookType.AfterPlayerTurnStartEarly,
            new HookRegistration((_) => log.Add("B-Early"))
        );
        reg.Subscribe(
            HookType.AfterPlayerTurnStart,
            new HookRegistration((_) => log.Add("A-Normal"))
        );
        reg.Subscribe(
            HookType.AfterPlayerTurnStart,
            new HookRegistration((_) => log.Add("B-Normal"))
        );
        reg.Subscribe(
            HookType.AfterPlayerTurnStartLate,
            new HookRegistration((_) => log.Add("A-Late"))
        );
        reg.Subscribe(
            HookType.AfterPlayerTurnStartLate,
            new HookRegistration((_) => log.Add("B-Late"))
        );

        // Reproduce upstream's three sequential foreach passes.
        reg.Fire(HookType.AfterPlayerTurnStartEarly, NewHookCtx());
        reg.Fire(HookType.AfterPlayerTurnStart, NewHookCtx());
        reg.Fire(HookType.AfterPlayerTurnStartLate, NewHookCtx());

        Assert.Equal(
            new[] { "A-Early", "B-Early", "A-Normal", "B-Normal", "A-Late", "B-Late" },
            log
        );
    }

    /// <summary>
    /// SCENARIO 2 — AfterCardChangedPiles dual-pass (early ordinary then late).
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 100-112.
    ///
    ///     public static async Task AfterCardChangedPiles(...)
    ///     {
    ///         foreach (model in runState.IterateHookListeners(combatState))
    ///             await model.AfterCardChangedPiles(...);       // pass 1
    ///         foreach (model in runState.IterateHookListeners(combatState))
    ///             await model.AfterCardChangedPilesLate(...);   // pass 2
    ///     }
    ///
    /// Two passes. Pinning: every listener's ordinary fires before any
    /// listener's Late, even when a listener subscribes to both.
    ///
    /// Golden: ["A-ord", "B-ord", "C-ord", "A-late", "B-late", "C-late"]
    /// </summary>
    [Fact]
    public void Scenario2_AfterCardChangedPiles_DualPassOrdering()
    {
        var reg = new HookRegistry();
        var log = new List<string>();

        reg.Subscribe(
            HookType.AfterCardChangedPiles,
            new HookRegistration((_) => log.Add("A-ord"))
        );
        reg.Subscribe(
            HookType.AfterCardChangedPilesLate,
            new HookRegistration((_) => log.Add("A-late"))
        );
        reg.Subscribe(
            HookType.AfterCardChangedPiles,
            new HookRegistration((_) => log.Add("B-ord"))
        );
        reg.Subscribe(
            HookType.AfterCardChangedPilesLate,
            new HookRegistration((_) => log.Add("B-late"))
        );
        reg.Subscribe(
            HookType.AfterCardChangedPiles,
            new HookRegistration((_) => log.Add("C-ord"))
        );
        reg.Subscribe(
            HookType.AfterCardChangedPilesLate,
            new HookRegistration((_) => log.Add("C-late"))
        );

        reg.Fire(HookType.AfterCardChangedPiles, NewHookCtx());
        reg.Fire(HookType.AfterCardChangedPilesLate, NewHookCtx());

        Assert.Equal(new[] { "A-ord", "B-ord", "C-ord", "A-late", "B-late", "C-late" }, log);
    }

    /// <summary>
    /// SCENARIO 3 — BeforeCombatStart dual-pass with priority precedence.
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 217-229.
    ///
    ///     public static async Task BeforeCombatStart(...)
    ///     {
    ///         foreach (model in runState.IterateHookListeners(combatState))
    ///             await model.BeforeCombatStart();        // pass 1
    ///         foreach (model in runState.IterateHookListeners(combatState))
    ///             await model.BeforeCombatStartLate();    // pass 2
    ///     }
    ///
    /// Q1-ADR-006 layers explicit priority on top of upstream's
    /// IterateHookListeners order. Pinning: within a single pass, higher
    /// priority fires first; the Late pass still runs after the ordinary
    /// pass even if a Late subscriber has higher priority than an ordinary
    /// subscriber. Late means "later pass," not "lower priority."
    ///
    /// Golden: ["high-ord", "low-ord", "high-late", "low-late"]
    /// </summary>
    [Fact]
    public void Scenario3_BeforeCombatStart_PriorityAcrossPasses()
    {
        var reg = new HookRegistry();
        var log = new List<string>();

        reg.Subscribe(
            HookType.BeforeCombatStart,
            new HookRegistration((_) => log.Add("low-ord"), priority: 0)
        );
        reg.Subscribe(
            HookType.BeforeCombatStartLate,
            new HookRegistration((_) => log.Add("high-late"), priority: 100)
        );
        reg.Subscribe(
            HookType.BeforeCombatStart,
            new HookRegistration((_) => log.Add("high-ord"), priority: 100)
        );
        reg.Subscribe(
            HookType.BeforeCombatStartLate,
            new HookRegistration((_) => log.Add("low-late"), priority: 0)
        );

        reg.Fire(HookType.BeforeCombatStart, NewHookCtx());
        reg.Fire(HookType.BeforeCombatStartLate, NewHookCtx());

        // ordinary pass: high (priority 100) before low (priority 0).
        // late pass: high before low.
        Assert.Equal(new[] { "high-ord", "low-ord", "high-late", "low-late" }, log);
    }

    /// <summary>
    /// SCENARIO 4 — AfterCardPlayed dual-pass with sub-action enqueueing.
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 181-197.
    ///
    ///     public static async Task AfterCardPlayed(...)
    ///     {
    ///         foreach (model in combatState.IterateHookListeners())
    ///             await model.AfterCardPlayed(...);       // pass 1
    ///         foreach (model in combatState.IterateHookListeners())
    ///             await model.AfterCardPlayedLate(...);   // pass 2
    ///     }
    ///
    /// Pinning combines:
    ///   (a) two-pass ordering (ord before late),
    ///   (b) cascading-action semantics: a hook subscriber enqueues an
    ///       IAction during Fire; the action runs AFTER all hook subscribers
    ///       in the same pass complete, AFTER the Late pass also completes,
    ///       and ONLY when Drain is called.
    ///
    /// Golden: ["ord-A", "ord-B-enqueues", "ord-C", "late-A", "FOLLOWUP"]
    /// — followup runs once Drain proceeds past the hook-driven enqueue.
    /// </summary>
    [Fact]
    public void Scenario4_AfterCardPlayed_HookEnqueuesActionForLaterDrain()
    {
        var reg = new HookRegistry();
        var queue = new ActionQueue();
        var clock = new LogicalClock();
        var rng = new Rng(0u);
        var ctx = new ExecutionContext(clock, rng, reg, queue);
        var log = new List<string>();

        var followup = new LabelAction("FOLLOWUP", log);

        reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration((_) => log.Add("ord-A")));
        reg.Subscribe(
            HookType.AfterCardPlayed,
            new HookRegistration(
                (h) =>
                {
                    log.Add("ord-B-enqueues");
                    h.Execution.Queue.Enqueue(followup);
                }
            )
        );
        reg.Subscribe(HookType.AfterCardPlayed, new HookRegistration((_) => log.Add("ord-C")));
        reg.Subscribe(HookType.AfterCardPlayedLate, new HookRegistration((_) => log.Add("late-A")));

        reg.Fire(HookType.AfterCardPlayed, new HookContext(ctx));
        reg.Fire(HookType.AfterCardPlayedLate, new HookContext(ctx));
        queue.Drain(ctx);

        Assert.Equal(new[] { "ord-A", "ord-B-enqueues", "ord-C", "late-A", "FOLLOWUP" }, log);
    }

    /// <summary>
    /// SCENARIO 5 — ModifyBlock additive then multiplicative pinning.
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 984-1014.
    ///
    ///     public static decimal ModifyBlock(...)
    ///     {
    ///         decimal num = block;
    ///         foreach (item in combatState.IterateHookListeners())
    ///             num += item.ModifyBlockAdditive(...);
    ///         foreach (item in combatState.IterateHookListeners())
    ///             num *= item.ModifyBlockMultiplicative(...);
    ///         return Math.Max(0m, num);
    ///     }
    ///
    /// Numerical pinning: with base block=5, an additive subscriber of +3 and
    /// a multiplicative subscriber of *2:
    ///   - additive-then-multiplicative order: (5+3) * 2 = 16
    ///   - multiplicative-then-additive order: (5*2) + 3 = 13
    /// Upstream is additive-first; Q1 must match. The test asserts the firing
    /// order produces the additive-first golden 16.
    ///
    /// Golden: ["add+3", "mul*2"], final = 16.
    /// </summary>
    [Fact]
    public void Scenario5_ModifyBlock_AdditiveThenMultiplicative()
    {
        var reg = new HookRegistry();
        var log = new List<string>();
        decimal value = 5m;

        // Subscribe in interleaved order to verify the two-pass discipline.
        reg.Subscribe(
            HookType.ModifyBlockMultiplicative,
            new HookRegistration(
                (_) =>
                {
                    log.Add("mul*2");
                    value *= 2m;
                }
            )
        );
        reg.Subscribe(
            HookType.ModifyBlockAdditive,
            new HookRegistration(
                (_) =>
                {
                    log.Add("add+3");
                    value += 3m;
                }
            )
        );

        reg.Fire(HookType.ModifyBlockAdditive, NewHookCtx());
        reg.Fire(HookType.ModifyBlockMultiplicative, NewHookCtx());

        Assert.Equal(new[] { "add+3", "mul*2" }, log);
        Assert.Equal(16m, value);
    }

    /// <summary>
    /// SCENARIO 6 — BeforeTurnEnd VeryEarly → Early → Normal three-pass ordering
    /// with mixed-priority subscribers within passes.
    ///
    /// Upstream trace: godot/sts2/src/Core/Hooks/Hook.cs lines 905-941.
    ///
    ///     public static async Task BeforeTurnEnd(...)
    ///     {
    ///         foreach (item in combatState.IterateHookListeners())
    ///             ... item.BeforeTurnEndVeryEarly(...);   // pass 1
    ///         foreach (item in combatState.IterateHookListeners())
    ///             ... item.BeforeTurnEndEarly(...);       // pass 2
    ///         foreach (item in combatState.IterateHookListeners())
    ///             ... item.BeforeTurnEnd(...);            // pass 3
    ///     }
    ///
    /// Composition test:
    ///   - pass 1 fires VeryEarly subscribers in priority-then-registration
    ///     order;
    ///   - pass 2 fires Early subscribers in the same internal order;
    ///   - pass 3 fires Normal subscribers in the same internal order.
    /// Pinning ensures (a) pass discipline, (b) priority discipline within a
    /// pass, (c) registration-order tiebreak within priority within a pass.
    ///
    /// Golden:
    ///   [
    ///     "VE-hi-2", "VE-hi-1", "VE-lo",
    ///     "E-hi",     "E-lo-1",  "E-lo-2",
    ///     "N-only"
    ///   ]
    /// — within VeryEarly: priorities {hi:10, hi:10, lo:0}. The two hi share
    /// priority; registration order: "VE-hi-2" registered first, "VE-hi-1"
    /// second.
    /// </summary>
    [Fact]
    public void Scenario6_BeforeTurnEnd_VeryEarlyEarlyNormalWithPriorities()
    {
        var reg = new HookRegistry();
        var log = new List<string>();

        // Cross-pass interleaved subscriptions to verify pass discipline.
        reg.Subscribe(
            HookType.BeforeTurnEndEarly,
            new HookRegistration((_) => log.Add("E-lo-1"), priority: 0)
        );
        reg.Subscribe(
            HookType.BeforeTurnEndVeryEarly,
            new HookRegistration((_) => log.Add("VE-hi-2"), priority: 10)
        );
        reg.Subscribe(
            HookType.BeforeTurnEnd,
            new HookRegistration((_) => log.Add("N-only"), priority: 0)
        );
        reg.Subscribe(
            HookType.BeforeTurnEndVeryEarly,
            new HookRegistration((_) => log.Add("VE-lo"), priority: 0)
        );
        reg.Subscribe(
            HookType.BeforeTurnEndVeryEarly,
            new HookRegistration((_) => log.Add("VE-hi-1"), priority: 10)
        );
        reg.Subscribe(
            HookType.BeforeTurnEndEarly,
            new HookRegistration((_) => log.Add("E-hi"), priority: 5)
        );
        reg.Subscribe(
            HookType.BeforeTurnEndEarly,
            new HookRegistration((_) => log.Add("E-lo-2"), priority: 0)
        );

        // Reproduce upstream's three sequential foreach passes.
        reg.Fire(HookType.BeforeTurnEndVeryEarly, NewHookCtx());
        reg.Fire(HookType.BeforeTurnEndEarly, NewHookCtx());
        reg.Fire(HookType.BeforeTurnEnd, NewHookCtx());

        Assert.Equal(
            new[]
            {
                "VE-hi-2",
                "VE-hi-1",
                "VE-lo", // VeryEarly: hi (regOrder: 2 then 1), then lo
                "E-hi",
                "E-lo-1",
                "E-lo-2", // Early: hi, then los in regOrder
                "N-only", // Normal: single
            },
            log
        );
    }

    private sealed class LabelAction : IAction
    {
        private readonly string _label;
        private readonly List<string> _log;

        public LabelAction(string label, List<string> log)
        {
            _label = label;
            _log = log;
        }

        public void Execute(ExecutionContext ctx) => _log.Add(_label);
    }
}
