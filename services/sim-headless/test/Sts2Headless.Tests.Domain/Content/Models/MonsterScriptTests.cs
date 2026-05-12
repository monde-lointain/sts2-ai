using System.Collections.Generic;
using System.Collections.Immutable;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// B.1-gamma-T2: MonsterIntent state-machine refactor tests. Validates the
/// new <see cref="IMoveBranchResolver"/> extension point on
/// <see cref="MonsterMove"/> for RNG-branching and HP-threshold rotations,
/// while keeping the observable <see cref="MonsterIntent"/> shape unchanged.
/// </summary>
public sealed class MonsterScriptTests
{
    /// <summary>
    /// RNG-branching monster: from move A, weighted-pick between B (weight 1)
    /// and C (weight 1). Models upstream's <c>RandomBranchState</c> shape.
    /// </summary>
    private sealed class FakeRngBranchMonster : MonsterModel
    {
        public const string A = "A";
        public const string B = "B";
        public const string C = "C";

        public FakeRngBranchMonster() : base(
            id: "fake_rng_branch",
            minInitialHp: 20, maxInitialHp: 20,
            moves: new MonsterMove[]
            {
                new(A, Intent.Attack(1), FollowUpMoveId: A,
                    BranchResolver: new RngBranchResolver(
                        choices: ImmutableArray.Create(
                            new RngBranchChoice(B, 1f),
                            new RngBranchChoice(C, 1f)),
                        bucket: RunRngType.MonsterAi)),
                new(B, Intent.Attack(2), FollowUpMoveId: A),
                new(C, Intent.Buff(),    FollowUpMoveId: A),
            },
            initialMoveId: A)
        { }
    }

    /// <summary>
    /// HP-threshold monster: from move SLEEP, branches based on whether HP fraction
    /// is below 0.5. Mirrors Lagavulin's awake/asleep gate (upstream uses a
    /// <c>HasPower&lt;AsleepPower&gt;</c> check; for the test we use HP fraction
    /// as a deterministic proxy).
    /// </summary>
    private sealed class FakeHpThresholdMonster : MonsterModel
    {
        public const string SLEEP = "SLEEP";
        public const string ATTACK_HIGH = "ATTACK_HIGH";
        public const string ATTACK_LOW = "ATTACK_LOW";

        public FakeHpThresholdMonster() : base(
            id: "fake_hp_threshold",
            minInitialHp: 100, maxInitialHp: 100,
            moves: new MonsterMove[]
            {
                new(SLEEP, Intent.Buff(), FollowUpMoveId: ATTACK_HIGH,
                    BranchResolver: new HpThresholdResolver(
                        fraction: 0.5f,
                        belowMoveId: ATTACK_LOW,
                        aboveMoveId: ATTACK_HIGH)),
                new(ATTACK_HIGH, Intent.Attack(10), FollowUpMoveId: SLEEP),
                new(ATTACK_LOW,  Intent.Attack(5),  FollowUpMoveId: SLEEP),
            },
            initialMoveId: SLEEP)
        { }
    }

    private static MoveBranchContext MakeContext(int currentHp, int maxHp,
        params string[] activePowers)
    {
        HashSet<string> powers = new(activePowers);
        return new MoveBranchContext(
            CurrentHp: currentHp,
            MaxHp: maxHp,
            HasPower: id => powers.Contains(id),
            GetPowerStacks: id => powers.Contains(id) ? 1 : 0);
    }

    // ===== Plain (no-resolver) move keeps FollowUpMoveId behavior =====

    [Fact]
    public void AdvanceMoveId_without_resolver_returns_FollowUpMoveId()
    {
        var moves = new MonsterMove[]
        {
            new("INC", Intent.Buff(), "STRIKE"),
            new("STRIKE", Intent.Attack(9), "STRIKE"),
        };
        var model = new TestMonster("plain", 10, 10, moves, "INC");
        MoveBranchContext ctx = MakeContext(10, 10);
        var rng = new RunRngSet("seed-1");
        string next = model.AdvanceMoveId("INC", ctx, rng);
        Assert.Equal("STRIKE", next);
    }

    // ===== RNG-branch resolver =====

    [Fact]
    public void RngBranch_with_fixed_seed_is_deterministic()
    {
        var model = new FakeRngBranchMonster();
        MoveBranchContext ctx = MakeContext(20, 20);
        var rng1 = new RunRngSet("seed-deterministic-branch");
        var rng2 = new RunRngSet("seed-deterministic-branch");
        string next1 = model.AdvanceMoveId(FakeRngBranchMonster.A, ctx, rng1);
        string next2 = model.AdvanceMoveId(FakeRngBranchMonster.A, ctx, rng2);
        Assert.Equal(next1, next2);
        Assert.True(
            next1 == FakeRngBranchMonster.B || next1 == FakeRngBranchMonster.C,
            $"Expected next move to be B or C; got {next1}.");
    }

    [Fact]
    public void RngBranch_distributes_across_choices()
    {
        var model = new FakeRngBranchMonster();
        MoveBranchContext ctx = MakeContext(20, 20);
        HashSet<string> seen = new();
        for (int i = 0; i < 50; i++)
        {
            var rng = new RunRngSet($"branch-spread-{i}");
            seen.Add(model.AdvanceMoveId(FakeRngBranchMonster.A, ctx, rng));
            if (seen.Count == 2) break;
        }
        Assert.Contains(FakeRngBranchMonster.B, seen);
        Assert.Contains(FakeRngBranchMonster.C, seen);
    }

    // ===== HP-threshold resolver =====

    [Fact]
    public void HpThreshold_branches_above_threshold()
    {
        var model = new FakeHpThresholdMonster();
        MoveBranchContext ctx = MakeContext(currentHp: 80, maxHp: 100); // 0.80 > 0.5
        var rng = new RunRngSet("seed-irrelevant");
        string next = model.AdvanceMoveId(FakeHpThresholdMonster.SLEEP, ctx, rng);
        Assert.Equal(FakeHpThresholdMonster.ATTACK_HIGH, next);
    }

    [Fact]
    public void HpThreshold_branches_below_threshold()
    {
        var model = new FakeHpThresholdMonster();
        MoveBranchContext ctx = MakeContext(currentHp: 40, maxHp: 100); // 0.40 < 0.5
        var rng = new RunRngSet("seed-irrelevant");
        string next = model.AdvanceMoveId(FakeHpThresholdMonster.SLEEP, ctx, rng);
        Assert.Equal(FakeHpThresholdMonster.ATTACK_LOW, next);
    }

    [Fact]
    public void HpThreshold_at_exact_threshold_uses_above_branch()
    {
        var model = new FakeHpThresholdMonster();
        MoveBranchContext ctx = MakeContext(currentHp: 50, maxHp: 100); // exactly 0.5
        var rng = new RunRngSet("seed-irrelevant");
        string next = model.AdvanceMoveId(FakeHpThresholdMonster.SLEEP, ctx, rng);
        Assert.Equal(FakeHpThresholdMonster.ATTACK_HIGH, next);
    }

    // ===== HasPower resolver =====

    [Fact]
    public void HasPowerResolver_routes_by_presence()
    {
        var moves = new MonsterMove[]
        {
            new("SLEEP", Intent.Buff(), FollowUpMoveId: "ATTACK",
                BranchResolver: new HasPowerResolver("Asleep", "SLEEP", "ATTACK")),
            new("ATTACK", Intent.Attack(10), "SLEEP"),
        };
        var model = new TestMonster("sleeper", 10, 10, moves, "SLEEP");
        var rng = new RunRngSet("seed-x");

        MoveBranchContext asleep = MakeContext(10, 10, "Asleep");
        Assert.Equal("SLEEP", model.AdvanceMoveId("SLEEP", asleep, rng));

        MoveBranchContext awake = MakeContext(10, 10);
        Assert.Equal("ATTACK", model.AdvanceMoveId("SLEEP", awake, rng));
    }

    // ===== Observable-surface check (re-surface trigger) =====

    [Fact]
    public void MonsterIntent_observable_shape_unchanged()
    {
        // The policy-network-visible fields on MonsterIntent are: Kind,
        // DamagePerHit, HitCount, AppliesPowers, MoveId. The state-machine
        // refactor must not add NEW observable fields. We sanity-check by
        // listing the public-instance properties.
        var props = typeof(Sts2Headless.Domain.Combat.MonsterIntent)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains(nameof(Sts2Headless.Domain.Combat.MonsterIntent.Kind), props);
        Assert.Contains(nameof(Sts2Headless.Domain.Combat.MonsterIntent.DamagePerHit), props);
        Assert.Contains(nameof(Sts2Headless.Domain.Combat.MonsterIntent.HitCount), props);
        Assert.Contains(nameof(Sts2Headless.Domain.Combat.MonsterIntent.AppliesPowers), props);
        Assert.Contains(nameof(Sts2Headless.Domain.Combat.MonsterIntent.MoveId), props);
        // Allow the None static (it is a property too).
        Assert.True(props.Count <= 6,
            $"MonsterIntent has unexpected public properties: {string.Join(',', props)}");
    }

    /// <summary>Concrete test-only monster opening up the protected constructor.</summary>
    private sealed class TestMonster : MonsterModel
    {
        public TestMonster(string id, int min, int max, IEnumerable<MonsterMove> moves, string initialMoveId)
            : base(id, min, max, moves, initialMoveId)
        { }
    }
}
