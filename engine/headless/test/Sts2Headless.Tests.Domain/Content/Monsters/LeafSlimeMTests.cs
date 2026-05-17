using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-exact behavior checks for <see cref="LeafSlimeM"/> ported from upstream
/// <c>src/Core/Models/Monsters/LeafSlimeM.cs</c>. Wave 14 / B.1-ε.
/// </summary>
public class LeafSlimeMTests
{
    private static MoveBranchContext FullHp() =>
        new(CurrentHp: 100, MaxHp: 100, HasPower: _ => false, GetPowerStacks: _ => 0);

    [Fact]
    public void LeafSlimeM_canonical_properties()
    {
        LeafSlimeM m = new();
        Assert.Equal("LeafSlimeM", m.Id);
        Assert.Equal(LeafSlimeM.MinHp, m.MinInitialHp);
        Assert.Equal(LeafSlimeM.MaxHp, m.MaxInitialHp);
        Assert.Equal(32, LeafSlimeM.MinHp);
        Assert.Equal(35, LeafSlimeM.MaxHp);
        Assert.Equal(8, LeafSlimeM.ClumpDamage);
        Assert.Equal(2, LeafSlimeM.StickyStatusCount);
    }

    [Fact]
    public void LeafSlimeM_opens_with_STICKY_SHOT_status_intent()
    {
        LeafSlimeM m = new();
        Assert.Equal(LeafSlimeM.StickyShotMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Status, m.InitialIntent.Kind);
        Assert.Equal(LeafSlimeM.StickyStatusCount, m.InitialIntent.Value);
    }

    [Fact]
    public void LeafSlimeM_CLUMP_SHOT_is_attack_intent()
    {
        LeafSlimeM m = new();
        MonsterMove clump = m.GetMove(LeafSlimeM.ClumpShotMoveId);
        Assert.Equal(IntentKind.Attack, clump.Intent.Kind);
        Assert.Equal(LeafSlimeM.ClumpDamage, clump.Intent.Value);
    }

    [Fact]
    public void LeafSlimeM_rotation_strict_alternation()
    {
        // STICKY_SHOT → CLUMP_SHOT → STICKY_SHOT → CLUMP_SHOT
        LeafSlimeM m = new();
        MoveBranchContext ctx = FullHp();
        RunRngSet rng = new("leaf-slime-m-seed");
        string cursor = m.InitialMoveId; // STICKY_SHOT
        Assert.Equal(LeafSlimeM.StickyShotMoveId, cursor);

        cursor = m.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(LeafSlimeM.ClumpShotMoveId, cursor);

        cursor = m.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(LeafSlimeM.StickyShotMoveId, cursor);

        cursor = m.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(LeafSlimeM.ClumpShotMoveId, cursor);
    }

    [Fact]
    public void LeafSlimeM_RollInitialHp_stays_in_envelope()
    {
        LeafSlimeM m = new();
        for (uint seed = 0; seed < 50; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, LeafSlimeM.MinHp, LeafSlimeM.MaxHp);
        }
    }
}
