using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Model-level tests for GremlinMerc (Wave-26/Q1.D): HP envelope, 3-cycle
/// rotation constants, spawn-power declarations.
/// </summary>
public class GremlinMercTests
{
    [Fact]
    public void GremlinMerc_canonical_id_is_GremlinMerc()
    {
        GremlinMerc m = new();
        Assert.Equal("GremlinMerc", m.Id);
        Assert.Equal("GremlinMerc", GremlinMerc.CanonicalId);
    }

    [Fact]
    public void GremlinMerc_hp_envelope_is_47_to_49()
    {
        GremlinMerc m = new();
        Assert.Equal(47, m.MinInitialHp);
        Assert.Equal(49, m.MaxInitialHp);
        Assert.Equal(GremlinMerc.MinHp, m.MinInitialHp);
        Assert.Equal(GremlinMerc.MaxHp, m.MaxInitialHp);
    }

    [Fact]
    public void GremlinMerc_initial_move_is_GIMME_MOVE()
    {
        GremlinMerc m = new();
        Assert.Equal(GremlinMerc.GimmeMoveId, m.InitialMoveId);
        Assert.Equal("GIMME_MOVE", GremlinMerc.GimmeMoveId);
    }

    [Fact]
    public void GremlinMerc_GIMME_MOVE_is_MultiAttack_7x2()
    {
        GremlinMerc m = new();
        MonsterMove gimme = m.GetMove(GremlinMerc.GimmeMoveId);
        Assert.Equal(IntentKind.Attack, gimme.Intent.Kind);
        Assert.Equal(GremlinMerc.GimmeDamage, gimme.Intent.Value);
        Assert.Equal(GremlinMerc.GimmeHitCount, gimme.Intent.HitCount);
        Assert.Equal(7, gimme.Intent.Value);
        Assert.Equal(2, gimme.Intent.HitCount);
    }

    [Fact]
    public void GremlinMerc_DOUBLE_SMASH_MOVE_is_MultiAttack_6x2()
    {
        GremlinMerc m = new();
        MonsterMove ds = m.GetMove(GremlinMerc.DoubleSmashMoveId);
        Assert.Equal(IntentKind.Attack, ds.Intent.Kind);
        Assert.Equal(GremlinMerc.DoubleSmashDamage, ds.Intent.Value);
        Assert.Equal(GremlinMerc.DoubleSmashHitCount, ds.Intent.HitCount);
        Assert.Equal(6, ds.Intent.Value);
        Assert.Equal(2, ds.Intent.HitCount);
    }

    [Fact]
    public void GremlinMerc_HEHE_MOVE_is_Attack_8()
    {
        GremlinMerc m = new();
        MonsterMove hehe = m.GetMove(GremlinMerc.HeheMoveId);
        Assert.Equal(IntentKind.Attack, hehe.Intent.Kind);
        Assert.Equal(GremlinMerc.HeheDamage, hehe.Intent.Value);
        Assert.Equal(1, hehe.Intent.HitCount);
        Assert.Equal(8, hehe.Intent.Value);
    }

    [Fact]
    public void GremlinMerc_rotation_is_GIMME_DOUBLE_SMASH_HEHE_loop()
    {
        GremlinMerc m = new();
        Assert.Equal(GremlinMerc.DoubleSmashMoveId, m.GetMove(GremlinMerc.GimmeMoveId).FollowUpMoveId);
        Assert.Equal(GremlinMerc.HeheMoveId, m.GetMove(GremlinMerc.DoubleSmashMoveId).FollowUpMoveId);
        Assert.Equal(GremlinMerc.GimmeMoveId, m.GetMove(GremlinMerc.HeheMoveId).FollowUpMoveId);
    }

    [Fact]
    public void GremlinMerc_spawn_powers_include_SurprisePower_1_and_ThieveryPower_20()
    {
        GremlinMerc m = new();
        Assert.Contains(m.SpawnPowers, p => p.PowerId == PowerIds.Surprise && p.Stacks == GremlinMerc.SurprisePowerStacks);
        Assert.Contains(m.SpawnPowers, p => p.PowerId == PowerIds.Thievery && p.Stacks == GremlinMerc.ThieveryPowerGold);
        Assert.Equal(1, GremlinMerc.SurprisePowerStacks);
        Assert.Equal(20, GremlinMerc.ThieveryPowerGold);
    }
}
