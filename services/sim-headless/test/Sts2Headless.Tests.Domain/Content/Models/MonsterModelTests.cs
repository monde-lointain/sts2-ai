using System.Collections.Generic;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// Behavior tests for <see cref="MonsterModel"/>: id/hp/moves round-trip;
/// intent rotation deterministic via the immutable <see cref="MonsterModel.AdvanceMoveId"/>
/// resolver (per-creature cursors live on the engine's
/// <see cref="Sts2Headless.Domain.Combat.MonsterIntent"/>, not on the model);
/// initial-HP roll consumes RNG deterministically; validation loud-fails on
/// bad config.
/// </summary>
public class MonsterModelTests
{
    /// <summary>Vanilla branch context — full HP, no powers. The smoke
    /// monsters used here either have no <c>BranchResolver</c> (deterministic
    /// follow-up only) or a self-loop, so the payload doesn't matter for
    /// rotation assertions.</summary>
    private static MoveBranchContext MakeBranchContextForTest()
        => new(CurrentHp: 100, MaxHp: 100,
            HasPower: _ => false, GetPowerStacks: _ => 0);

    /// <summary>
    /// Two-move cycle (BUFF → ATTACK → ATTACK → ...), modelled after the Cultist
    /// upstream rotation: first move is BUFF (incantation), then ATTACK loops on itself.
    /// </summary>
    private sealed class FakeCultist : MonsterModel
    {
        public FakeCultist() : base(
            id: "fake_cultist",
            minInitialHp: 38,
            maxInitialHp: 41,
            moves: new MonsterMove[]
            {
                new(Id: "INCANTATION", Intent: Intent.Buff(), FollowUpMoveId: "DARK_STRIKE"),
                new(Id: "DARK_STRIKE", Intent: Intent.Attack(9), FollowUpMoveId: "DARK_STRIKE"),
            },
            initialMoveId: "INCANTATION")
        { }
    }

    [Fact]
    public void Construction_assigns_canonical_properties()
    {
        FakeCultist m = new();
        Assert.Equal("fake_cultist", m.Id);
        Assert.Equal(38, m.MinInitialHp);
        Assert.Equal(41, m.MaxInitialHp);
        Assert.Equal(2, m.Moves.Count);
        Assert.Equal("INCANTATION", m.InitialMoveId);
    }

    [Fact]
    public void InitialIntent_reflects_initial_move()
    {
        FakeCultist m = new();
        Assert.Equal(IntentKind.Buff, m.InitialIntent.Kind);
    }

    [Fact]
    public void AdvanceMoveId_advances_to_follow_up()
    {
        FakeCultist m = new();
        MoveBranchContext ctx = MakeBranchContextForTest();
        string cursor = m.InitialMoveId;
        Assert.Equal("INCANTATION", cursor);

        cursor = m.AdvanceMoveId(cursor, ctx, new RunRngSet("seed-0"));
        Assert.Equal("DARK_STRIKE", cursor);

        MonsterMove next = m.GetMove(cursor);
        Assert.Equal(IntentKind.Attack, next.Intent.Kind);
        Assert.Equal(9, next.Intent.Value);
    }

    [Fact]
    public void AdvanceMoveId_loops_on_self_referential_follow_up()
    {
        FakeCultist m = new();
        MoveBranchContext ctx = MakeBranchContextForTest();
        RunRngSet rng = new("seed-0");
        string cursor = m.InitialMoveId;
        cursor = m.AdvanceMoveId(cursor, ctx, rng); // → DARK_STRIKE
        cursor = m.AdvanceMoveId(cursor, ctx, rng); // → DARK_STRIKE (self-loop)
        Assert.Equal("DARK_STRIKE", cursor);
    }

    [Fact]
    public void Intent_rotation_deterministic_with_fixed_seed()
    {
        // Two independent cursors driven by the same seeded RNG must produce
        // identical intent sequences. Cultists don't branch on rng, but the
        // test asserts the contract for the base class so rng-branching
        // monsters land safely later.
        FakeCultist a = new();
        FakeCultist b = new();
        MoveBranchContext ctx = MakeBranchContextForTest();
        RunRngSet rngA = new("seed-42");
        RunRngSet rngB = new("seed-42");

        string cursorA = a.InitialMoveId;
        string cursorB = b.InitialMoveId;
        List<string> seqA = new();
        List<string> seqB = new();
        for (int i = 0; i < 5; i++)
        {
            seqA.Add(cursorA);
            seqB.Add(cursorB);
            cursorA = a.AdvanceMoveId(cursorA, ctx, rngA);
            cursorB = b.AdvanceMoveId(cursorB, ctx, rngB);
        }
        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void RollInitialHp_is_within_inclusive_range_and_deterministic_per_seed()
    {
        FakeCultist m = new();
        // Sample several seeds; every result must lie in [38, 41].
        for (uint seed = 0; seed < 100; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, 38, 41);
        }
        // Determinism: same seed yields same hp.
        Assert.Equal(m.RollInitialHp(new Rng(7u)), m.RollInitialHp(new Rng(7u)));
    }

    [Fact]
    public void RollInitialHp_can_hit_both_endpoints_across_seeds()
    {
        // With 4 possible values and a uniform-ish PRNG, scanning a few hundred seeds
        // should hit both 38 (the min) and 41 (the max). Sanity check that the
        // inclusive-both-ends contract isn't off-by-one.
        FakeCultist m = new();
        bool sawMin = false;
        bool sawMax = false;
        for (uint seed = 0; seed < 500; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            sawMin |= hp == 38;
            sawMax |= hp == 41;
            if (sawMin && sawMax) break;
        }
        Assert.True(sawMin, "Expected at least one seed to roll min HP (38).");
        Assert.True(sawMax, "Expected at least one seed to roll max HP (41).");
    }

    [Fact]
    public void GetMove_throws_on_unknown_id()
    {
        FakeCultist m = new();
        Assert.Throws<KeyNotFoundException>(() => m.GetMove("UNKNOWN"));
    }

    [Fact]
    public void Construction_rejects_invalid_HP_envelope()
    {
        // max < min
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new BadCultist(minInitialHp: 10, maxInitialHp: 5));
        // min <= 0
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new BadCultist(minInitialHp: 0, maxInitialHp: 10));
    }

    [Fact]
    public void Construction_rejects_duplicate_move_ids()
    {
        Assert.Throws<System.InvalidOperationException>(() => new DupeMoveCultist());
    }

    [Fact]
    public void Construction_rejects_unknown_initial_move_id()
    {
        Assert.Throws<System.InvalidOperationException>(() => new BadInitialMoveCultist());
    }

    [Fact]
    public void Construction_rejects_unknown_follow_up_move_id()
    {
        Assert.Throws<System.InvalidOperationException>(() => new BadFollowUpCultist());
    }

    [Fact]
    public void MonsterModel_subclass_registers_in_MonsterCatalog()
    {
        MonsterCatalog catalog = new();
        FakeCultist m = new();
        catalog.Register(m.Id, m);
        Assert.Same(m, catalog.Get("fake_cultist"));
    }

    private sealed class BadCultist : MonsterModel
    {
        public BadCultist(int minInitialHp, int maxInitialHp)
            : base("bad_cultist", minInitialHp, maxInitialHp,
                new MonsterMove[] { new("M", Intent.Buff(), "M") }, "M") { }
    }

    private sealed class DupeMoveCultist : MonsterModel
    {
        public DupeMoveCultist() : base("dupe", 1, 1,
            new MonsterMove[]
            {
                new("A", Intent.Buff(), "A"),
                new("A", Intent.Buff(), "A"),
            }, "A") { }
    }

    private sealed class BadInitialMoveCultist : MonsterModel
    {
        public BadInitialMoveCultist() : base("bad_init", 1, 1,
            new MonsterMove[] { new("A", Intent.Buff(), "A") }, "B") { }
    }

    private sealed class BadFollowUpCultist : MonsterModel
    {
        public BadFollowUpCultist() : base("bad_followup", 1, 1,
            new MonsterMove[] { new("A", Intent.Buff(), "NONEXISTENT") }, "A") { }
    }
}
