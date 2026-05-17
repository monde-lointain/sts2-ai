using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-exact behavior checks for <see cref="TwigSlimeM"/> ported from upstream
/// <c>src/Core/Models/Monsters/TwigSlimeM.cs</c>. Wave 14 / B.1-ε.
/// </summary>
public class TwigSlimeMTests
{
    private static MoveBranchContext FullHp() =>
        new(CurrentHp: 100, MaxHp: 100, HasPower: _ => false, GetPowerStacks: _ => 0);

    [Fact]
    public void TwigSlimeM_canonical_properties()
    {
        TwigSlimeM m = new();
        Assert.Equal("TwigSlimeM", m.Id);
        Assert.Equal(TwigSlimeM.MinHp, m.MinInitialHp);
        Assert.Equal(TwigSlimeM.MaxHp, m.MaxInitialHp);
        Assert.Equal(26, TwigSlimeM.MinHp);
        Assert.Equal(28, TwigSlimeM.MaxHp);
        Assert.Equal(11, TwigSlimeM.ClumpDamage);
        Assert.Equal(1, TwigSlimeM.StickyStatusCount);
    }

    [Fact]
    public void TwigSlimeM_opens_with_STICKY_SHOT_MOVE_status_intent()
    {
        TwigSlimeM m = new();
        Assert.Equal(TwigSlimeM.StickyShotMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Status, m.InitialIntent.Kind);
        Assert.Equal(TwigSlimeM.StickyStatusCount, m.InitialIntent.Value);
    }

    [Fact]
    public void TwigSlimeM_POKEY_POUNCE_is_attack_intent()
    {
        TwigSlimeM m = new();
        MonsterMove pounce = m.GetMove(TwigSlimeM.PokeyPounceMoveId);
        Assert.Equal(IntentKind.Attack, pounce.Intent.Kind);
        Assert.Equal(TwigSlimeM.ClumpDamage, pounce.Intent.Value);
    }

    [Fact]
    public void TwigSlimeM_rotation_is_deterministic_across_seeds()
    {
        TwigSlimeM a = new();
        TwigSlimeM b = new();
        MoveBranchContext ctx = FullHp();
        RunRngSet rngA = new("twig-slime-m-seed");
        RunRngSet rngB = new("twig-slime-m-seed");
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
    public void TwigSlimeM_RollInitialHp_stays_in_envelope()
    {
        TwigSlimeM m = new();
        for (uint seed = 0; seed < 50; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, TwigSlimeM.MinHp, TwigSlimeM.MaxHp);
        }
    }
}
