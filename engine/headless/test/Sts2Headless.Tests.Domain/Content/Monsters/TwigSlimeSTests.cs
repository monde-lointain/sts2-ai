using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-exact behavior checks for <see cref="TwigSlimeS"/> ported from upstream
/// <c>src/Core/Models/Monsters/TwigSlimeS.cs</c>. Wave 14 / B.1-ε.
/// </summary>
public class TwigSlimeSTests
{
    private static MoveBranchContext FullHp() =>
        new(CurrentHp: 100, MaxHp: 100, HasPower: _ => false, GetPowerStacks: _ => 0);

    [Fact]
    public void TwigSlimeS_canonical_properties()
    {
        TwigSlimeS m = new();
        Assert.Equal("TwigSlimeS", m.Id);
        Assert.Equal(TwigSlimeS.MinHp, m.MinInitialHp);
        Assert.Equal(TwigSlimeS.MaxHp, m.MaxInitialHp);
        Assert.Equal(7, TwigSlimeS.MinHp);
        Assert.Equal(11, TwigSlimeS.MaxHp);
        Assert.Equal(4, TwigSlimeS.TackleDamage);
    }

    [Fact]
    public void TwigSlimeS_opens_with_TACKLE_MOVE_attack()
    {
        TwigSlimeS m = new();
        Assert.Equal(TwigSlimeS.TackleMoveId, m.InitialMoveId);
        Assert.Equal(IntentKind.Attack, m.InitialIntent.Kind);
        Assert.Equal(TwigSlimeS.TackleDamage, m.InitialIntent.Value);
    }

    [Fact]
    public void TwigSlimeS_TACKLE_MOVE_self_loops()
    {
        // Single-state machine: TACKLE_MOVE → TACKLE_MOVE → ... forever.
        TwigSlimeS m = new();
        MoveBranchContext ctx = FullHp();
        RunRngSet rng = new("twig-slime-s-seed");
        string cursor = m.InitialMoveId;
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(TwigSlimeS.TackleMoveId, cursor);
            cursor = m.AdvanceMoveId(cursor, ctx, rng);
        }
    }

    [Fact]
    public void TwigSlimeS_RollInitialHp_stays_in_envelope()
    {
        TwigSlimeS m = new();
        for (uint seed = 0; seed < 50; seed++)
        {
            int hp = m.RollInitialHp(new Rng(seed));
            Assert.InRange(hp, TwigSlimeS.MinHp, TwigSlimeS.MaxHp);
        }
    }
}
