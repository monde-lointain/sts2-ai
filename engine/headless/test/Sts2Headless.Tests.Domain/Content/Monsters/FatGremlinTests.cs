using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Model-level tests for FatGremlin (Wave-26/Q1.D): HP envelope, move
/// rotation, and spawn-power absence (spawned bare by SurprisePower).
/// </summary>
public class FatGremlinTests
{
    [Fact]
    public void FatGremlin_canonical_id_is_FatGremlin()
    {
        FatGremlin m = new();
        Assert.Equal("FatGremlin", m.Id);
        Assert.Equal("FatGremlin", FatGremlin.CanonicalId);
    }

    [Fact]
    public void FatGremlin_hp_envelope_is_13_to_17()
    {
        FatGremlin m = new();
        Assert.Equal(13, m.MinInitialHp);
        Assert.Equal(17, m.MaxInitialHp);
        Assert.Equal(FatGremlin.MinHp, m.MinInitialHp);
        Assert.Equal(FatGremlin.MaxHp, m.MaxInitialHp);
    }

    [Fact]
    public void FatGremlin_initial_move_is_SPAWNED_MOVE_with_Stun_intent()
    {
        FatGremlin m = new();
        Assert.Equal(FatGremlin.SpawnedMoveId, m.InitialMoveId);
        Assert.Equal("SPAWNED_MOVE", FatGremlin.SpawnedMoveId);
        Assert.Equal(IntentKind.Stun, m.InitialIntent.Kind);
    }

    [Fact]
    public void FatGremlin_FLEE_MOVE_is_Unknown_intent_Escape_placeholder()
    {
        FatGremlin m = new();
        MonsterMove flee = m.GetMove(FatGremlin.FleeMoveId);
        Assert.Equal("FLEE_MOVE", FatGremlin.FleeMoveId);
        // Q1 maps EscapeIntent → Unknown (no engine Escape mechanic at Phase-1).
        Assert.Equal(IntentKind.Unknown, flee.Intent.Kind);
    }

    [Fact]
    public void FatGremlin_rotation_is_SPAWNED_then_FLEE_self_loop()
    {
        FatGremlin m = new();
        Assert.Equal(FatGremlin.FleeMoveId, m.GetMove(FatGremlin.SpawnedMoveId).FollowUpMoveId);
        Assert.Equal(FatGremlin.FleeMoveId, m.GetMove(FatGremlin.FleeMoveId).FollowUpMoveId);
    }

    [Fact]
    public void FatGremlin_has_no_spawn_powers()
    {
        FatGremlin m = new();
        Assert.Empty(m.SpawnPowers);
    }
}
