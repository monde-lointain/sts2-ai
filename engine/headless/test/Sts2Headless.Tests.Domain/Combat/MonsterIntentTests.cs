using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// Unit tests for <see cref="MonsterIntent.FromContentIntent(MonsterMove)"/> mapping
/// logic added in Wave-38/B: AttackDefend production, SelfBlockGain passthrough,
/// AppliesPowers population, Defend value migration, Status kind, and
/// PowerTarget Self/Player round-trip.
/// </summary>
public sealed class MonsterIntentTests
{
    // Helper: build a MonsterMove with just the parts we care about.
    private static MonsterMove Move(
        Intent intent,
        ImmutableList<MonsterIntentPower>? powers = null,
        int selfBlockGain = 0
    ) =>
        new MonsterMove(
            Id: "TEST",
            Intent: intent,
            FollowUpMoveId: "TEST",
            AppliesPowers: powers,
            SelfBlockGain: selfBlockGain
        );

    [Fact]
    public void Attack_with_zero_SelfBlockGain_produces_Kind_Attack()
    {
        var move = Move(Intent.Attack(9));
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Attack, intent.Kind);
        Assert.Equal(9, intent.DamagePerHit);
        Assert.Equal(0, intent.SelfBlockGain);
    }

    [Fact]
    public void Attack_with_positive_SelfBlockGain_produces_Kind_AttackDefend()
    {
        var move = Move(Intent.Attack(6), selfBlockGain: 5);
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.AttackDefend, intent.Kind);
        Assert.Equal(6, intent.DamagePerHit);
        Assert.Equal(5, intent.SelfBlockGain);
    }

    [Fact]
    public void Defend_maps_Intent_Value_to_SelfBlockGain()
    {
        var move = Move(Intent.Defend(14));
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Defend, intent.Kind);
        Assert.Equal(14, intent.SelfBlockGain);
        Assert.Equal(0, intent.DamagePerHit);
    }

    [Fact]
    public void Defend_with_extra_SelfBlockGain_sums_both()
    {
        var move = Move(Intent.Defend(10), selfBlockGain: 4);
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Defend, intent.Kind);
        Assert.Equal(14, intent.SelfBlockGain);
    }

    [Fact]
    public void Buff_populates_AppliesPowers_from_move()
    {
        var powers = ImmutableList.Create(
            new MonsterIntentPower(PowerIds.Strength, 2)
        );
        var move = Move(Intent.Buff(), powers: powers);
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Buff, intent.Kind);
        Assert.Single(intent.AppliesPowers);
        Assert.Equal(PowerIds.Strength, intent.AppliesPowers[0].PowerId);
        Assert.Equal(2, intent.AppliesPowers[0].Stacks);
        Assert.Equal(PowerTarget.Self, intent.AppliesPowers[0].Target);
    }

    [Fact]
    public void Debuff_with_Player_target_preserved()
    {
        var powers = ImmutableList.Create(
            new MonsterIntentPower(PowerIds.Weak, 2, PowerTarget.Player)
        );
        var move = Move(Intent.Debuff(), powers: powers);
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Debuff, intent.Kind);
        Assert.Single(intent.AppliesPowers);
        Assert.Equal(PowerTarget.Player, intent.AppliesPowers[0].Target);
    }

    [Fact]
    public void Status_maps_to_Kind_Status_not_Buff()
    {
        var move = Move(new Intent(IntentKind.Status, 3));
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Status, intent.Kind);
        // HitCount carries the card count for Status (overloaded, no engine consumer yet).
        Assert.Equal(3, intent.HitCount);
    }

    [Fact]
    public void Null_AppliesPowers_on_move_produces_empty_list()
    {
        var move = Move(Intent.Buff(), powers: null);
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Empty(intent.AppliesPowers);
    }

    [Fact]
    public void MultiHit_attack_preserves_HitCount()
    {
        var move = Move(Intent.MultiAttack(8, 2));
        var intent = MonsterIntent.FromContentIntent(move);
        Assert.Equal(MonsterIntentKind.Attack, intent.Kind);
        Assert.Equal(8, intent.DamagePerHit);
        Assert.Equal(2, intent.HitCount);
    }

    [Fact]
    public void BackCompat_FromContentIntent_Intent_overload_works()
    {
        // Ensure the back-compat shim compiles and produces the same result
        // as the MonsterMove path for a simple Attack.
        var intent = MonsterIntent.FromContentIntent(Intent.Attack(7));
        Assert.Equal(MonsterIntentKind.Attack, intent.Kind);
        Assert.Equal(7, intent.DamagePerHit);
        Assert.Empty(intent.AppliesPowers);
    }

    [Fact]
    public void MoveId_is_recorded_in_intent()
    {
        var move = Move(Intent.Attack(5));
        var intent = MonsterIntent.FromContentIntent(move, "SOME_MOVE");
        Assert.Equal("SOME_MOVE", intent.MoveId);
    }

    [Fact]
    public void Equals_includes_SelfBlockGain()
    {
        var a = new MonsterIntent(
            MonsterIntentKind.AttackDefend,
            6,
            1,
            ImmutableList<MonsterIntentPower>.Empty,
            "",
            5
        );
        var b = new MonsterIntent(
            MonsterIntentKind.AttackDefend,
            6,
            1,
            ImmutableList<MonsterIntentPower>.Empty,
            "",
            0
        );
        Assert.NotEqual(a, b);
    }
}
