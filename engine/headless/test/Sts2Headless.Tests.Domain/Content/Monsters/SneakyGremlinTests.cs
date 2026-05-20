using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Model-level tests for SneakyGremlin (Wave-26/Q1.D): HP envelope, move
/// rotation, and spawn-power absence (spawned bare by SurprisePower).
/// </summary>
public class SneakyGremlinTests
{
    [Fact]
    public void SneakyGremlin_canonical_id_is_SneakyGremlin()
    {
        SneakyGremlin m = new();
        Assert.Equal("SneakyGremlin", m.Id);
        Assert.Equal("SneakyGremlin", SneakyGremlin.CanonicalId);
    }

    [Fact]
    public void SneakyGremlin_hp_envelope_is_10_to_14()
    {
        SneakyGremlin m = new();
        Assert.Equal(10, m.MinInitialHp);
        Assert.Equal(14, m.MaxInitialHp);
        Assert.Equal(SneakyGremlin.MinHp, m.MinInitialHp);
        Assert.Equal(SneakyGremlin.MaxHp, m.MaxInitialHp);
    }

    [Fact]
    public void SneakyGremlin_initial_move_is_SPAWNED_MOVE_with_Stun_intent()
    {
        SneakyGremlin m = new();
        Assert.Equal(SneakyGremlin.SpawnedMoveId, m.InitialMoveId);
        Assert.Equal("SPAWNED_MOVE", SneakyGremlin.SpawnedMoveId);
        Assert.Equal(IntentKind.Stun, m.InitialIntent.Kind);
    }

    [Fact]
    public void SneakyGremlin_TACKLE_MOVE_is_Attack_9_single_hit()
    {
        SneakyGremlin m = new();
        MonsterMove tackle = m.GetMove(SneakyGremlin.TackleMoveId);
        Assert.Equal(IntentKind.Attack, tackle.Intent.Kind);
        Assert.Equal(SneakyGremlin.TackleDamage, tackle.Intent.Value);
        Assert.Equal(1, tackle.Intent.HitCount);
        Assert.Equal(9, tackle.Intent.Value);
    }

    [Fact]
    public void SneakyGremlin_rotation_is_SPAWNED_then_TACKLE_self_loop()
    {
        SneakyGremlin m = new();
        Assert.Equal(
            SneakyGremlin.TackleMoveId,
            m.GetMove(SneakyGremlin.SpawnedMoveId).FollowUpMoveId
        );
        Assert.Equal(
            SneakyGremlin.TackleMoveId,
            m.GetMove(SneakyGremlin.TackleMoveId).FollowUpMoveId
        );
    }

    [Fact]
    public void SneakyGremlin_has_no_spawn_powers()
    {
        SneakyGremlin m = new();
        Assert.Empty(m.SpawnPowers);
    }
}
