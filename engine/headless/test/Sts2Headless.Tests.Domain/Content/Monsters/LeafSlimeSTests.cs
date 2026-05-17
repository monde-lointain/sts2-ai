using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-exact behavior checks for <see cref="LeafSlimeS"/> ported from upstream
/// <c>src/Core/Models/Monsters/LeafSlimeS.cs</c>. Wave 14 / B.1-ε.
/// </summary>
public class LeafSlimeSTests
{
    private static MoveBranchContext FullHp() =>
        new(CurrentHp: 100, MaxHp: 100, HasPower: _ => false, GetPowerStacks: _ => 0);

    [Fact]
    public void LeafSlimeS_canonical_properties()
    {
        LeafSlimeS m = new();
        Assert.Equal("LeafSlimeS", m.Id);
        Assert.Equal(LeafSlimeS.MinHp, m.MinInitialHp);
        Assert.Equal(LeafSlimeS.MaxHp, m.MaxInitialHp);
        Assert.Equal(11, LeafSlimeS.MinHp);
        Assert.Equal(15, LeafSlimeS.MaxHp);
        Assert.Equal(3, LeafSlimeS.TackleDamage);
        Assert.Equal(1, LeafSlimeS.GoopStatusCount);
    }

    [Fact]
    public void LeafSlimeS_opens_with_TACKLE_MOVE_attack()
    {
        LeafSlimeS m = new();
        Assert.Equal(LeafSlimeS.TackleMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Attack, m.InitialIntent.Kind);
        Assert.Equal(LeafSlimeS.TackleDamage, m.InitialIntent.Value);
    }

    [Fact]
    public void LeafSlimeS_GOOP_MOVE_is_status_intent()
    {
        LeafSlimeS m = new();
        MonsterMove goopMove = m.GetMove(LeafSlimeS.GoopMoveId);
        Assert.Equal(IntentKind.Status, goopMove.Intent.Kind);
        Assert.Equal(LeafSlimeS.GoopStatusCount, goopMove.Intent.Value);
    }

    [Fact]
    public void LeafSlimeS_rotation_is_deterministic_across_seeds()
    {
        LeafSlimeS a = new();
        LeafSlimeS b = new();
        MoveBranchContext ctx = FullHp();
        RunRngSet rngA = new("leaf-slime-s-seed");
        RunRngSet rngB = new("leaf-slime-s-seed");
        string cursorA = a.InitialMoveId;
        string cursorB = b.InitialMoveId;
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(cursorA, cursorB);
            cursorA = a.AdvanceMoveId(cursorA, ctx, rngA);
            cursorB = b.AdvanceMoveId(cursorB, ctx, rngB);
        }
    }

    [Fact]
    public void LeafSlimeS_RollInitialHp_stays_in_envelope()
    {
        LeafSlimeS m = new();
        for (uint seed = 0; seed < 50; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, LeafSlimeS.MinHp, LeafSlimeS.MaxHp);
        }
    }
}
