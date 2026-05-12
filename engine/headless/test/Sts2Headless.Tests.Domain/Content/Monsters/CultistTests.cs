using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-exact behavior checks for the two cultist monsters in the smoke encounter.
/// Verifies HP envelope, intent rotation, and the deterministic move-state machine
/// matches upstream's INCANTATION → DARK_STRIKE → DARK_STRIKE_loop pattern via
/// the immutable <see cref="MonsterModel.AdvanceMoveId"/> resolver.
/// </summary>
public class CultistTests
{
    /// <summary>Vanilla branch context for cultists — they have no
    /// <c>BranchResolver</c>, so the payload doesn't influence rotation.</summary>
    private static MoveBranchContext FullHpNoPowers()
        => new(CurrentHp: 100, MaxHp: 100,
            HasPower: _ => false, GetPowerStacks: _ => 0);

    // ===== CalcifiedCultist (upstream: src/Core/Models/Monsters/CalcifiedCultist.cs) =====

    [Fact]
    public void CalcifiedCultist_canonical_properties()
    {
        CalcifiedCultist m = new();
        Assert.Equal("CalcifiedCultist", m.Id);
        Assert.Equal(38, m.MinInitialHp);
        Assert.Equal(41, m.MaxInitialHp);
        Assert.Equal(9, CalcifiedCultist.DarkStrikeDamage);
        Assert.Equal(2, CalcifiedCultist.IncantationRitualStacks);
    }

    [Fact]
    public void CalcifiedCultist_starts_with_INCANTATION_intent()
    {
        CalcifiedCultist m = new();
        Assert.Equal(CalcifiedCultist.IncantationMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Buff, m.InitialIntent.Kind);
    }

    [Fact]
    public void CalcifiedCultist_rotation_INCANTATION_then_DARK_STRIKE_loop()
    {
        CalcifiedCultist m = new();
        MoveBranchContext ctx = FullHpNoPowers();
        RunRngSet rng = new("calcified-seed");
        string cursor = m.InitialMoveId;
        cursor = m.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(CalcifiedCultist.DarkStrikeMoveId, cursor);

        MonsterMove darkStrike = m.GetMove(cursor);
        Assert.Equal(IntentKind.Attack, darkStrike.Intent.Kind);
        Assert.Equal(9, darkStrike.Intent.Value);

        // Self-loop: DARK_STRIKE → DARK_STRIKE → DARK_STRIKE.
        for (int i = 0; i < 5; i++)
        {
            cursor = m.AdvanceMoveId(cursor, ctx, rng);
            Assert.Equal(CalcifiedCultist.DarkStrikeMoveId, cursor);
            Assert.Equal(9, m.GetMove(cursor).Intent.Value);
        }
    }

    // ===== DampCultist (upstream: src/Core/Models/Monsters/DampCultist.cs) =====

    [Fact]
    public void DampCultist_canonical_properties()
    {
        DampCultist m = new();
        Assert.Equal("DampCultist", m.Id);
        Assert.Equal(51, m.MinInitialHp);
        Assert.Equal(53, m.MaxInitialHp);
        Assert.Equal(1, DampCultist.DarkStrikeDamage);
        Assert.Equal(5, DampCultist.IncantationRitualStacks);
    }

    [Fact]
    public void DampCultist_starts_with_INCANTATION_intent()
    {
        DampCultist m = new();
        Assert.Equal(DampCultist.IncantationMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Buff, m.InitialIntent.Kind);
    }

    [Fact]
    public void DampCultist_rotation_INCANTATION_then_DARK_STRIKE_loop_with_damage_1()
    {
        DampCultist m = new();
        MoveBranchContext ctx = FullHpNoPowers();
        RunRngSet rng = new("damp-seed");
        string cursor = m.InitialMoveId;
        cursor = m.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(DampCultist.DarkStrikeMoveId, cursor);
        Assert.Equal(IntentKind.Attack, m.GetMove(cursor).Intent.Kind);
        Assert.Equal(1, m.GetMove(cursor).Intent.Value);
    }

    // ===== Intent rotation determinism across both cultists =====

    [Fact]
    public void Cultist_intent_rotation_is_deterministic_across_repeated_seeds()
    {
        // Two independent cursors driven by the same seed yield identical
        // intent sequences.
        CalcifiedCultist a = new();
        CalcifiedCultist b = new();
        MoveBranchContext ctx = FullHpNoPowers();
        RunRngSet rngA = new("calcified-12345");
        RunRngSet rngB = new("calcified-12345");
        string cursorA = a.InitialMoveId;
        string cursorB = b.InitialMoveId;
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(cursorA, cursorB);
            MonsterMove moveA = a.GetMove(cursorA);
            MonsterMove moveB = b.GetMove(cursorB);
            Assert.Equal(moveA.Intent.Kind, moveB.Intent.Kind);
            Assert.Equal(moveA.Intent.Value, moveB.Intent.Value);
            cursorA = a.AdvanceMoveId(cursorA, ctx, rngA);
            cursorB = b.AdvanceMoveId(cursorB, ctx, rngB);
        }
    }

    [Fact]
    public void Cultist_RollInitialHp_stays_in_envelope_and_deterministic_per_seed()
    {
        CalcifiedCultist m = new();
        for (uint seed = 0; seed < 50; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, CalcifiedCultist.MinHp, CalcifiedCultist.MaxHp);
        }
        Assert.Equal(m.RollInitialHp(new Rng(99u)), m.RollInitialHp(new Rng(99u)));
    }
}
