using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Model-level tests for Nibbit (Wave-24/K.q1): HP envelope, move-intent kinds,
/// and FollowUp wiring.
/// </summary>
public class NibbitTests
{
    [Fact]
    public void Nibbit_canonical_id_is_Nibbit()
    {
        Nibbit n = new();
        Assert.Equal("Nibbit", n.Id);
        Assert.Equal("Nibbit", Nibbit.CanonicalId);
    }

    [Fact]
    public void Nibbit_hp_envelope_is_42_to_46()
    {
        Nibbit n = new();
        Assert.Equal(42, n.MinInitialHp);
        Assert.Equal(46, n.MaxInitialHp);
    }

    [Fact]
    public void Nibbit_BUTT_move_is_Attack_12()
    {
        Nibbit n = new();
        MonsterMove butt = n.GetMove(Nibbit.ButtMoveId);
        Assert.Equal(IntentKind.Attack, butt.Intent.Kind);
        Assert.Equal(12, butt.Intent.Value);
        Assert.Equal(1, butt.Intent.HitCount);
    }

    [Fact]
    public void Nibbit_SLICE_move_is_Attack_6()
    {
        Nibbit n = new();
        MonsterMove slice = n.GetMove(Nibbit.SliceMoveId);
        Assert.Equal(IntentKind.Attack, slice.Intent.Kind);
        Assert.Equal(6, slice.Intent.Value);
        Assert.Equal(1, slice.Intent.HitCount);
    }

    [Fact]
    public void Nibbit_HISS_move_is_Buff()
    {
        Nibbit n = new();
        MonsterMove hiss = n.GetMove(Nibbit.HissMoveId);
        Assert.Equal(IntentKind.Buff, hiss.Intent.Kind);
    }

    [Fact]
    public void Nibbit_initial_move_is_BUTT_MOVE()
    {
        Nibbit n = new();
        Assert.Equal(Nibbit.ButtMoveId, n.InitialMoveId);
        Assert.Equal("BUTT_MOVE", Nibbit.ButtMoveId);
    }

    [Fact]
    public void Nibbit_move_ids_have_correct_constant_values()
    {
        Assert.Equal("BUTT_MOVE", Nibbit.ButtMoveId);
        Assert.Equal("SLICE_MOVE", Nibbit.SliceMoveId);
        Assert.Equal("HISS_MOVE", Nibbit.HissMoveId);
    }

    [Fact]
    public void Nibbit_FollowUp_chain_is_BUTT_SLICE_HISS_BUTT()
    {
        Nibbit n = new();
        Assert.Equal(Nibbit.SliceMoveId, n.GetMove(Nibbit.ButtMoveId).FollowUpMoveId);
        Assert.Equal(Nibbit.HissMoveId, n.GetMove(Nibbit.SliceMoveId).FollowUpMoveId);
        Assert.Equal(Nibbit.ButtMoveId, n.GetMove(Nibbit.HissMoveId).FollowUpMoveId);
    }

    [Fact]
    public void Nibbit_has_no_spawn_powers()
    {
        Nibbit n = new();
        Assert.Empty(n.SpawnPowers);
    }
}
